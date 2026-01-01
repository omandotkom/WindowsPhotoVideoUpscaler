using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Upscaler.App.Infrastructure;

namespace Upscaler.App.Processing;

public sealed class ImagePipeline
{
    private readonly IImagePreprocessor _preprocessor;
    private readonly ITileSplitter _tileSplitter;
    private readonly IInferenceEngine _inference;
    private readonly ITileMerger _merger;
    private readonly IImagePostprocessor _postprocessor;

    public ImagePipeline(
        IImagePreprocessor preprocessor,
        ITileSplitter tileSplitter,
        IInferenceEngine inference,
        ITileMerger merger,
        IImagePostprocessor postprocessor)
    {
        _preprocessor = preprocessor;
        _tileSplitter = tileSplitter;
        _inference = inference;
        _merger = merger;
        _postprocessor = postprocessor;
    }

    public async Task<UpscaleResult> UpscaleAsync(
        UpscaleRequest request,
        IProgress<UpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request.InputFiles.Count == 0)
        {
            throw new InvalidOperationException("No input files specified.");
        }

        List<string> outputs = new();
        ImageTensor? previousOutput = null;
        for (int i = 0; i < request.InputFiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string input = request.InputFiles[i];
            int currentImage = i + 1;
            progress?.Report(new UpscaleProgress
            {
                CurrentIndex = currentImage,
                Total = request.InputFiles.Count,
                TileIndex = 0,
                TileTotal = 0,
                OverallPercent = (double)(currentImage - 1) / request.InputFiles.Count * 100,
                Message = $"Processing {System.IO.Path.GetFileName(input)}"
            });

            AppLogger.Info($"Upscale started: {input}");
            ImageTensor image = await _preprocessor.LoadAsync(input, request.PreviewCrop, request.DenoiseStrength, cancellationToken);
            int tileSize = request.TileSize ?? 0;
            if (_inference.PreferredTileSize.HasValue)
            {
                tileSize = _inference.PreferredTileSize.Value;
            }
            IReadOnlyList<ImageTile> tiles = _tileSplitter.Split(image, tileSize, request.TileOverlap);
            int tileTotal = Math.Max(1, tiles.Count);

            Progress<TileProgress> tileProgress = new(p =>
            {
                double tileFraction = tileTotal == 0 ? 1 : (double)p.Current / tileTotal;
                double overall = (currentImage - 1 + tileFraction) / request.InputFiles.Count;
                progress?.Report(new UpscaleProgress
                {
                    CurrentIndex = currentImage,
                    Total = request.InputFiles.Count,
                    TileIndex = p.Current,
                    TileTotal = tileTotal,
                    OverallPercent = overall * 100,
                    Message = $"Tile {p.Current}/{tileTotal}"
                });
            });

            IReadOnlyList<ImageTile> outputsTiles = await _inference.InferAsync(tiles, tileProgress, cancellationToken);

            int outputWidth = image.Width * request.Scale;
            int outputHeight = image.Height * request.Scale;
            ImageTensor merged = _merger.Merge(outputsTiles, outputWidth, outputHeight, request.TileOverlap * request.Scale);
            ImageTensor outputImage = merged;
            if (request.EnableTemporalBlend && previousOutput != null)
            {
                float alpha = (float)Math.Clamp(request.TemporalBlendStrength, 0.0, 1.0);
                if (alpha > 0 && previousOutput.Data.Length == merged.Data.Length)
                {
                    outputImage = BlendWithPrevious(merged, previousOutput, alpha);
                }
                else if (previousOutput.Data.Length != merged.Data.Length)
                {
                    AppLogger.Warn("Temporal blend skipped due to mismatched frame sizes.");
                }
            }

            string outputPath = OutputNaming.BuildOutputPath(input, request.OutputFolder, request.Scale, request.OutputFormat);
            string resolvedFormat = System.IO.Path.GetExtension(outputPath).TrimStart('.');
            OutputOptions options = new()
            {
                OutputPath = outputPath,
                Format = string.IsNullOrWhiteSpace(resolvedFormat) ? request.OutputFormat : resolvedFormat,
                JpegQuality = request.JpegQuality,
                SourceMetadata = image.Metadata,
                SourcePath = input
            };
            await _postprocessor.SaveAsync(outputImage, options, cancellationToken);
            outputs.Add(outputPath);
            previousOutput = outputImage;
            AppLogger.Info($"Upscale finished: {outputPath}");
        }

        return new UpscaleResult { OutputFiles = outputs };
    }

    private static ImageTensor BlendWithPrevious(ImageTensor current, ImageTensor previous, float alpha)
    {
        float[] blended = new float[current.Data.Length];
        float invAlpha = 1f - alpha;
        for (int i = 0; i < blended.Length; i++)
        {
            blended[i] = current.Data[i] * invAlpha + previous.Data[i] * alpha;
        }

        return new ImageTensor
        {
            Width = current.Width,
            Height = current.Height,
            Data = blended,
            Metadata = current.Metadata
        };
    }
}
