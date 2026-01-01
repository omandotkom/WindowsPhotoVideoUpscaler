using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Upscaler.App.Infrastructure;
using Upscaler.App.Models;

namespace Upscaler.App.Processing;

public sealed class OnnxInferenceEngine : IInferenceEngine, IDisposable
{
    private static readonly ConcurrentDictionary<string, InferenceSession> SessionCache = new();
    private readonly ModelDefinition _model;
    private readonly string _modelPath;
    private InferenceSession? _session;
    private bool _usingCpu;
    private int? _preferredTileSize;

    public OnnxInferenceEngine(ModelDefinition model)
    {
        _model = model;
        _modelPath = ModelFileStore.GetModelFilePath(model);
    }

    public bool UsingCpuFallback => _usingCpu;

    public int? PreferredTileSize
    {
        get
        {
            EnsureSession();
            return _preferredTileSize;
        }
    }

    public async Task<IReadOnlyList<ImageTile>> InferAsync(
        IReadOnlyList<ImageTile> tiles,
        IProgress<TileProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (tiles.Count == 0)
        {
            return Array.Empty<ImageTile>();
        }

        EnsureSession();
        InferenceSession session = _session ?? throw new InvalidOperationException("Inference session not initialized.");
        string inputName = session.InputMetadata.Keys.First();
        string outputName = session.OutputMetadata.Keys.First();

        List<ImageTile> outputs = new(tiles.Count);
        int index = 0;
        foreach (ImageTile tile in tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            DenseTensor<float> tensor = new(tile.Data, new[] { 1, 3, tile.Height, tile.Width });
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(
                new[] { NamedOnnxValue.CreateFromTensor(inputName, tensor) });

            DenseTensor<float> output = results.First(r => r.Name == outputName).AsTensor<float>() as DenseTensor<float>
                                        ?? throw new InvalidOperationException("Invalid output tensor.");

            int outHeight = output.Dimensions[2];
            int outWidth = output.Dimensions[3];
            float[] outputData = output.ToArray();
            int scaleX = Math.Max(1, outWidth / Math.Max(1, tile.Width));
            int scaleY = Math.Max(1, outHeight / Math.Max(1, tile.Height));

            outputs.Add(new ImageTile
            {
                X = tile.X * scaleX,
                Y = tile.Y * scaleY,
                Width = outWidth,
                Height = outHeight,
                Data = outputData
            });

            progress?.Report(new TileProgress { Current = index, Total = tiles.Count });
        }

        await Task.CompletedTask;
        return outputs;
    }

    private void EnsureSession()
    {
        if (_session != null)
        {
            return;
        }

        if (!System.IO.File.Exists(_modelPath))
        {
            throw new InvalidOperationException($"Model not found at {_modelPath}");
        }

        _session = SessionCache.GetOrAdd(_modelPath, path =>
        {
            using SessionOptions options = new()
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            try
            {
                options.AppendExecutionProvider_DML(0);
                _usingCpu = false;
                AppLogger.Info("DirectML execution provider enabled.");
            }
            catch (Exception ex)
            {
                _usingCpu = true;
                AppLogger.Warn($"DirectML unavailable, falling back to CPU: {ex.Message}");
            }

            return new InferenceSession(path, options);
        });

        if (_preferredTileSize == null)
        {
            var input = _session.InputMetadata.First().Value;
            if (input.Dimensions.Count() >= 4)
            {
                int h = (int)input.Dimensions[^2];
                int w = (int)input.Dimensions[^1];
                if (h > 0 && w > 0 && h == w)
                {
                    _preferredTileSize = h;
                }
            }
        }
    }

    public void Dispose()
    {
        ClearCachedSession(_modelPath);
        _session = null;
    }

    public static void ClearCachedSession(string modelPath)
    {
        if (SessionCache.TryRemove(modelPath, out InferenceSession? session))
        {
            session.Dispose();
        }
    }

    public static void ClearAllSessions()
    {
        foreach (string key in SessionCache.Keys)
        {
            ClearCachedSession(key);
        }
    }
}
