using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Upscaler.App.Infrastructure;
using Upscaler.App.Models;

namespace Upscaler.App.Processing;

public sealed class FaceRefinementPipeline : IFaceRefiner, IDisposable
{
    private const float FaceExpandScale = 1.4f;
    private const int MinFaceSize = 32;
    private const int MaxFaces = 8;

    private readonly YuNetFaceDetector _detector;
    private readonly GfpganFaceRefiner _refiner;

    public FaceRefinementPipeline(ModelDefinition detectorModel, ModelDefinition refinerModel)
    {
        _detector = new YuNetFaceDetector(detectorModel);
        _refiner = new GfpganFaceRefiner(refinerModel);
    }

    public ImageTensor Refine(ImageTensor image, CancellationToken cancellationToken)
    {
        if (image.Data.Length == 0)
        {
            return image;
        }

        List<DetectedFace> faces;
        try
        {
            faces = _detector.Detect(image, cancellationToken);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Face detection failed: {ex.Message}");
            return image;
        }

        if (faces.Count == 0)
        {
            return image;
        }

        float[] output = (float[])image.Data.Clone();
        foreach (DetectedFace face in faces.OrderByDescending(f => f.Score).Take(MaxFaces))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (face.Width < MinFaceSize || face.Height < MinFaceSize)
            {
                continue;
            }

            FaceRegion region = FaceRegion.FromFace(face, image.Width, image.Height, FaceExpandScale);
            if (region.Width <= 0 || region.Height <= 0)
            {
                continue;
            }

            if (FaceAlignment.TryEstimate(face, _refiner.InputWidth, _refiner.InputHeight, out AffineTransform transform))
            {
                float[] aligned = ImageOps.WarpAffine(output, image.Width, image.Height, transform, _refiner.InputWidth, _refiner.InputHeight);
                float[] refinedAligned;
                try
                {
                    refinedAligned = _refiner.Refine(aligned, _refiner.InputWidth, _refiner.InputHeight, cancellationToken);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Face refinement skipped: {ex.Message}");
                    continue;
                }

                bool[] mask;
                float[] patch = ImageOps.ProjectAlignedToRegion(
                    output,
                    image.Width,
                    image.Height,
                    refinedAligned,
                    _refiner.InputWidth,
                    _refiner.InputHeight,
                    transform,
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height,
                    out mask);
                ImageOps.BlendPatchWithMask(output, image.Width, image.Height, patch, region.X, region.Y, region.Width, region.Height, region.Feather, mask);
            }
            else
            {
                float[] crop = ImageOps.Crop(output, image.Width, image.Height, region.X, region.Y, region.Width, region.Height);
                float[] refined;
                try
                {
                    refined = _refiner.Refine(crop, region.Width, region.Height, cancellationToken);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Face refinement skipped: {ex.Message}");
                    continue;
                }

                ImageOps.BlendPatch(output, image.Width, image.Height, refined, region.X, region.Y, region.Width, region.Height, region.Feather);
            }
        }

        return new ImageTensor
        {
            Width = image.Width,
            Height = image.Height,
            Data = output,
            Metadata = image.Metadata
        };
    }

    public void Dispose()
    {
        _detector.Dispose();
        _refiner.Dispose();
    }
}

internal sealed class YuNetFaceDetector : IDisposable
{
    private const float ScoreThreshold = 0.6f;
    private const float NmsThreshold = 0.3f;
    private const int MaxDetections = 5000;
    private static readonly int[] Strides = { 8, 16, 32 };

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly int _padWidth;
    private readonly int _padHeight;

    public YuNetFaceDetector(ModelDefinition model)
    {
        string path = ModelFileStore.GetModelFilePath(model);
        _session = SessionFactory.CreateSession(path, "YuNet");
        _inputName = _session.InputMetadata.Keys.First();
        var input = _session.InputMetadata[_inputName];
        _inputHeight = ResolveDimension(input.Dimensions, 2, 640);
        _inputWidth = ResolveDimension(input.Dimensions, 3, 640);
        _padWidth = ((Math.Max(_inputWidth, 1) - 1) / 32 + 1) * 32;
        _padHeight = ((Math.Max(_inputHeight, 1) - 1) / 32 + 1) * 32;
    }

    public List<DetectedFace> Detect(ImageTensor image, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        float[] resized = ImageOps.ResizeBilinear(image.Data, image.Width, image.Height, _inputWidth, _inputHeight);
        float[] bgr = ImageOps.ToBgr(resized, _inputWidth, _inputHeight, 255f);
        DenseTensor<float> inputTensor = new(bgr, new[] { 1, 3, _inputHeight, _inputWidth });

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(
            new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) });

        List<DisposableNamedOnnxValue> resultList = results.ToList();
        Dictionary<string, DenseTensor<float>> outputMap = resultList.ToDictionary(
            r => r.Name,
            r => r.AsTensor<float>() as DenseTensor<float> ?? throw new InvalidOperationException("Invalid output tensor."));

        DenseTensor<float>[] cls = new DenseTensor<float>[Strides.Length];
        DenseTensor<float>[] obj = new DenseTensor<float>[Strides.Length];
        DenseTensor<float>[] bbox = new DenseTensor<float>[Strides.Length];
        DenseTensor<float>[] kps = new DenseTensor<float>[Strides.Length];
        for (int i = 0; i < Strides.Length; i++)
        {
            cls[i] = OutputResolver.Get(outputMap, resultList, $"cls_{Strides[i]}", i);
            obj[i] = OutputResolver.Get(outputMap, resultList, $"obj_{Strides[i]}", i + Strides.Length);
            bbox[i] = OutputResolver.Get(outputMap, resultList, $"bbox_{Strides[i]}", i + Strides.Length * 2);
            kps[i] = OutputResolver.Get(outputMap, resultList, $"kps_{Strides[i]}", i + Strides.Length * 3);
        }

        List<DetectedFace> faces = new();
        float scaleX = (float)image.Width / _inputWidth;
        float scaleY = (float)image.Height / _inputHeight;

        for (int i = 0; i < Strides.Length; i++)
        {
            int stride = Strides[i];
            int cols = _padWidth / stride;
            int rows = _padHeight / stride;

            ReadOnlySpan<float> clsSpan = cls[i].Buffer.Span;
            ReadOnlySpan<float> objSpan = obj[i].Buffer.Span;
            ReadOnlySpan<float> bboxSpan = bbox[i].Buffer.Span;
            ReadOnlySpan<float> kpsSpan = kps[i].Buffer.Span;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    float clsScore = Math.Clamp(clsSpan[idx], 0f, 1f);
                    float objScore = Math.Clamp(objSpan[idx], 0f, 1f);
                    float score = MathF.Sqrt(clsScore * objScore);
                    if (score < ScoreThreshold)
                    {
                        continue;
                    }

                    float cx = (c + bboxSpan[idx * 4 + 0]) * stride;
                    float cy = (r + bboxSpan[idx * 4 + 1]) * stride;
                    float w = MathF.Exp(bboxSpan[idx * 4 + 2]) * stride;
                    float h = MathF.Exp(bboxSpan[idx * 4 + 3]) * stride;
                    float x1 = cx - w / 2f;
                    float y1 = cy - h / 2f;

                    int x = (int)MathF.Round(x1 * scaleX);
                    int y = (int)MathF.Round(y1 * scaleY);
                    int width = (int)MathF.Round(w * scaleX);
                    int height = (int)MathF.Round(h * scaleY);

                    if (width <= 0 || height <= 0)
                    {
                        continue;
                    }

                    x = Math.Clamp(x, 0, Math.Max(0, image.Width - 1));
                    y = Math.Clamp(y, 0, Math.Max(0, image.Height - 1));
                    width = Math.Clamp(width, 1, image.Width - x);
                    height = Math.Clamp(height, 1, image.Height - y);

                    float[] landmarks = new float[10];
                    for (int n = 0; n < 5; n++)
                    {
                        float lx = (kpsSpan[idx * 10 + 2 * n] + c) * stride;
                        float ly = (kpsSpan[idx * 10 + 2 * n + 1] + r) * stride;
                        landmarks[2 * n] = lx * scaleX;
                        landmarks[2 * n + 1] = ly * scaleY;
                    }

                    faces.Add(new DetectedFace(x, y, width, height, score, landmarks));
                }
            }
        }

        if (faces.Count == 0)
        {
            return faces;
        }

        return Nms.Apply(faces, NmsThreshold, MaxDetections);
    }

    public void Dispose() => _session.Dispose();

    private static int ResolveDimension(IReadOnlyList<long> dims, int index, int fallback)
    {
        if (index < 0 || index >= dims.Count)
        {
            return fallback;
        }

        long value = dims[index];
        if (value <= 0)
        {
            return fallback;
        }

        return (int)value;
    }

    private static int ResolveDimension(IReadOnlyList<int> dims, int index, int fallback)
    {
        if (index < 0 || index >= dims.Count)
        {
            return fallback;
        }

        int value = dims[index];
        if (value <= 0)
        {
            return fallback;
        }

        return value;
    }
}

internal sealed class GfpganFaceRefiner : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;

    public GfpganFaceRefiner(ModelDefinition model)
    {
        string path = ModelFileStore.GetModelFilePath(model);
        _session = SessionFactory.CreateSession(path, "GFPGAN");
        _inputName = _session.InputMetadata.Keys.First();
        _outputName = _session.OutputMetadata.Keys.First();
        var input = _session.InputMetadata[_inputName];
        _inputHeight = ResolveDimension(input.Dimensions, 2, 512);
        _inputWidth = ResolveDimension(input.Dimensions, 3, 512);
    }

    public int InputWidth => _inputWidth;
    public int InputHeight => _inputHeight;

    public float[] Refine(float[] rgb, int width, int height, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        float[] resized = ImageOps.ResizeBilinear(rgb, width, height, _inputWidth, _inputHeight);
        float[] normalized = ImageOps.NormalizeMinusOneToOne(resized);
        DenseTensor<float> inputTensor = new(normalized, new[] { 1, 3, _inputHeight, _inputWidth });

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(
            new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) });

        DenseTensor<float> outputTensor = results.First(r => r.Name == _outputName).AsTensor<float>() as DenseTensor<float>
            ?? throw new InvalidOperationException("Invalid GFPGAN output tensor.");

        int outHeight = ResolveDimension(outputTensor.Dimensions, 2, _inputHeight);
        int outWidth = ResolveDimension(outputTensor.Dimensions, 3, _inputWidth);
        float[] output = outputTensor.ToArray();
        ImageOps.DenormalizeMinusOneToOneInPlace(output);
        if (outWidth != width || outHeight != height)
        {
            output = ImageOps.ResizeBilinear(output, outWidth, outHeight, width, height);
        }

        return output;
    }

    public void Dispose() => _session.Dispose();

    private static int ResolveDimension(IReadOnlyList<long> dims, int index, int fallback)
    {
        if (index < 0 || index >= dims.Count)
        {
            return fallback;
        }

        long value = dims[index];
        if (value <= 0)
        {
            return fallback;
        }

        return (int)value;
    }

    private static int ResolveDimension(ReadOnlySpan<int> dims, int index, int fallback)
    {
        if (index < 0 || index >= dims.Length)
        {
            return fallback;
        }

        int value = dims[index];
        if (value <= 0)
        {
            return fallback;
        }

        return value;
    }
}

internal readonly struct DetectedFace
{
    public DetectedFace(int x, int y, int width, int height, float score, float[]? landmarks)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Score = score;
        Landmarks = landmarks;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public float Score { get; }
    public float[]? Landmarks { get; }
    public bool HasLandmarks => Landmarks != null && Landmarks.Length >= 6;
}

internal readonly struct FaceRegion
{
    public FaceRegion(int x, int y, int size, int feather)
    {
        X = x;
        Y = y;
        Width = size;
        Height = size;
        Feather = feather;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Feather { get; }

    public static FaceRegion FromFace(DetectedFace face, int imageWidth, int imageHeight, float scale)
    {
        int size = (int)MathF.Round(Math.Max(face.Width, face.Height) * scale);
        if (size <= 0)
        {
            return new FaceRegion(0, 0, 0, 0);
        }

        int cx = face.X + face.Width / 2;
        int cy = face.Y + face.Height / 2;
        int x = cx - size / 2;
        int y = cy - size / 2;

        if (x < 0)
        {
            x = 0;
        }

        if (y < 0)
        {
            y = 0;
        }

        if (x + size > imageWidth)
        {
            x = Math.Max(0, imageWidth - size);
        }

        if (y + size > imageHeight)
        {
            y = Math.Max(0, imageHeight - size);
        }

        size = Math.Min(size, Math.Min(imageWidth - x, imageHeight - y));
        int feather = Math.Clamp(size / 10, 2, 32);
        return new FaceRegion(x, y, size, feather);
    }
}

internal static class Nms
{
    public static List<DetectedFace> Apply(List<DetectedFace> faces, float threshold, int topK)
    {
        if (faces.Count <= 1)
        {
            return faces;
        }

        List<DetectedFace> sorted = faces
            .OrderByDescending(face => face.Score)
            .ToList();

        List<DetectedFace> keep = new();
        foreach (DetectedFace face in sorted)
        {
            bool shouldKeep = true;
            foreach (DetectedFace kept in keep)
            {
                if (ComputeIoU(face, kept) > threshold)
                {
                    shouldKeep = false;
                    break;
                }
            }

            if (shouldKeep)
            {
                keep.Add(face);
                if (keep.Count >= topK)
                {
                    break;
                }
            }
        }

        return keep;
    }

    private static float ComputeIoU(DetectedFace a, DetectedFace b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        int interWidth = Math.Max(0, x2 - x1);
        int interHeight = Math.Max(0, y2 - y1);
        int interArea = interWidth * interHeight;
        int areaA = a.Width * a.Height;
        int areaB = b.Width * b.Height;
        int union = areaA + areaB - interArea;
        if (union <= 0)
        {
            return 0f;
        }

        return (float)interArea / union;
    }
}

internal static class OutputResolver
{
    public static DenseTensor<float> Get(
        IReadOnlyDictionary<string, DenseTensor<float>> outputs,
        IReadOnlyList<DisposableNamedOnnxValue> outputList,
        string name,
        int fallbackIndex)
    {
        if (outputs.TryGetValue(name, out DenseTensor<float>? tensor))
        {
            return tensor;
        }

        if (fallbackIndex >= 0 && fallbackIndex < outputList.Count)
        {
            return outputList[fallbackIndex].AsTensor<float>() as DenseTensor<float>
                ?? throw new InvalidOperationException($"Output tensor '{name}' not found.");
        }

        throw new InvalidOperationException($"Output tensor '{name}' not found.");
    }
}

internal static class SessionFactory
{
    public static InferenceSession CreateSession(string path, string label)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new InvalidOperationException($"Model not found at {path}");
        }

        using SessionOptions options = new()
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        try
        {
            options.AppendExecutionProvider_DML(0);
            AppLogger.Info($"{label} DirectML execution provider enabled.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"{label} DirectML unavailable, falling back to CPU: {ex.Message}");
        }

        return new InferenceSession(path, options);
    }
}

internal static class ImageOps
{
    public static float[] Crop(float[] data, int width, int height, int x, int y, int cropWidth, int cropHeight)
    {
        float[] cropped = new float[cropWidth * cropHeight * 3];
        int srcHw = width * height;
        int dstHw = cropWidth * cropHeight;

        for (int cy = 0; cy < cropHeight; cy++)
        {
            int srcY = y + cy;
            if (srcY < 0 || srcY >= height)
            {
                continue;
            }

            int srcRow = srcY * width;
            int dstRow = cy * cropWidth;
            for (int cx = 0; cx < cropWidth; cx++)
            {
                int srcX = x + cx;
                if (srcX < 0 || srcX >= width)
                {
                    continue;
                }

                int srcIndex = srcRow + srcX;
                int dstIndex = dstRow + cx;
                cropped[dstIndex] = data[srcIndex];
                cropped[dstHw + dstIndex] = data[srcHw + srcIndex];
                cropped[2 * dstHw + dstIndex] = data[2 * srcHw + srcIndex];
            }
        }

        return cropped;
    }

    public static float[] ResizeBilinear(float[] data, int width, int height, int targetWidth, int targetHeight)
    {
        if (width == targetWidth && height == targetHeight)
        {
            return (float[])data.Clone();
        }

        float[] resized = new float[targetWidth * targetHeight * 3];
        int srcHw = width * height;
        int dstHw = targetWidth * targetHeight;

        float scaleX = (float)width / targetWidth;
        float scaleY = (float)height / targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            float fy = (y + 0.5f) * scaleY - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(fy), 0, height - 1);
            int y1 = Math.Clamp(y0 + 1, 0, height - 1);
            float wy = fy - y0;
            int dstRow = y * targetWidth;
            int srcRow0 = y0 * width;
            int srcRow1 = y1 * width;

            for (int x = 0; x < targetWidth; x++)
            {
                float fx = (x + 0.5f) * scaleX - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(fx), 0, width - 1);
                int x1 = Math.Clamp(x0 + 1, 0, width - 1);
                float wx = fx - x0;

                int dstIndex = dstRow + x;
                int srcIndex00 = srcRow0 + x0;
                int srcIndex01 = srcRow0 + x1;
                int srcIndex10 = srcRow1 + x0;
                int srcIndex11 = srcRow1 + x1;

                for (int c = 0; c < 3; c++)
                {
                    int srcOffset = c * srcHw;
                    float v00 = data[srcOffset + srcIndex00];
                    float v01 = data[srcOffset + srcIndex01];
                    float v10 = data[srcOffset + srcIndex10];
                    float v11 = data[srcOffset + srcIndex11];
                    float top = v00 + (v01 - v00) * wx;
                    float bottom = v10 + (v11 - v10) * wx;
                    resized[c * dstHw + dstIndex] = top + (bottom - top) * wy;
                }
            }
        }

        return resized;
    }

    public static float[] WarpAffine(
        float[] data,
        int width,
        int height,
        AffineTransform transform,
        int targetWidth,
        int targetHeight)
    {
        if (!transform.TryInvert(out AffineTransform inverse))
        {
            return ResizeBilinear(data, width, height, targetWidth, targetHeight);
        }

        float[] warped = new float[targetWidth * targetHeight * 3];
        int dstHw = targetWidth * targetHeight;
        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                float srcX = inverse.A * x + inverse.B * y + inverse.C;
                float srcY = inverse.D * x + inverse.E * y + inverse.F;
                SampleBilinear(data, width, height, srcX, srcY, out float r, out float g, out float b);
                int idx = y * targetWidth + x;
                warped[idx] = r;
                warped[dstHw + idx] = g;
                warped[2 * dstHw + idx] = b;
            }
        }

        return warped;
    }

    public static float[] ProjectAlignedToRegion(
        float[] source,
        int sourceWidth,
        int sourceHeight,
        float[] aligned,
        int alignedWidth,
        int alignedHeight,
        AffineTransform transform,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight,
        out bool[] mask)
    {
        float[] patch = Crop(source, sourceWidth, sourceHeight, regionX, regionY, regionWidth, regionHeight);
        mask = new bool[regionWidth * regionHeight];
        int patchHw = regionWidth * regionHeight;
        for (int y = 0; y < regionHeight; y++)
        {
            int srcY = regionY + y;
            if (srcY < 0 || srcY >= sourceHeight)
            {
                continue;
            }

            int row = y * regionWidth;
            for (int x = 0; x < regionWidth; x++)
            {
                int srcX = regionX + x;
                if (srcX < 0 || srcX >= sourceWidth)
                {
                    continue;
                }

                float u = transform.A * srcX + transform.B * srcY + transform.C;
                float v = transform.D * srcX + transform.E * srcY + transform.F;
                if (u < 0 || v < 0 || u > alignedWidth - 1 || v > alignedHeight - 1)
                {
                    continue;
                }

                SampleBilinear(aligned, alignedWidth, alignedHeight, u, v, out float r, out float g, out float b);
                int idx = row + x;
                patch[idx] = r;
                patch[patchHw + idx] = g;
                patch[2 * patchHw + idx] = b;
                mask[idx] = true;
            }
        }

        return patch;
    }

    public static float[] ToBgr(float[] rgb, int width, int height, float scale)
    {
        int hw = width * height;
        float[] bgr = new float[hw * 3];
        for (int i = 0; i < hw; i++)
        {
            float r = rgb[i] * scale;
            float g = rgb[hw + i] * scale;
            float b = rgb[2 * hw + i] * scale;
            bgr[i] = b;
            bgr[hw + i] = g;
            bgr[2 * hw + i] = r;
        }

        return bgr;
    }

    public static float[] NormalizeMinusOneToOne(float[] data)
    {
        float[] normalized = new float[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            normalized[i] = data[i] * 2f - 1f;
        }

        return normalized;
    }

    public static void DenormalizeMinusOneToOneInPlace(float[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            float value = (data[i] + 1f) * 0.5f;
            data[i] = Math.Clamp(value, 0f, 1f);
        }
    }

    public static void BlendPatch(
        float[] dest,
        int destWidth,
        int destHeight,
        float[] patch,
        int x,
        int y,
        int patchWidth,
        int patchHeight,
        int feather)
    {
        int destHw = destWidth * destHeight;
        int patchHw = patchWidth * patchHeight;

        for (int py = 0; py < patchHeight; py++)
        {
            int destY = y + py;
            if (destY < 0 || destY >= destHeight)
            {
                continue;
            }

            float wy = EdgeWeight(py, patchHeight, feather);
            int destRow = destY * destWidth;
            int patchRow = py * patchWidth;

            for (int px = 0; px < patchWidth; px++)
            {
                int destX = x + px;
                if (destX < 0 || destX >= destWidth)
                {
                    continue;
                }

                float wx = EdgeWeight(px, patchWidth, feather);
                float weight = MathF.Min(wx, wy);
                int destIndex = destRow + destX;
                int patchIndex = patchRow + px;

                for (int c = 0; c < 3; c++)
                {
                    int destOffset = c * destHw;
                    int patchOffset = c * patchHw;
                    float baseValue = dest[destOffset + destIndex];
                    float patchValue = patch[patchOffset + patchIndex];
                    dest[destOffset + destIndex] = baseValue * (1f - weight) + patchValue * weight;
                }
            }
        }
    }

    private static float EdgeWeight(int pos, int length, int feather)
    {
        if (feather <= 0)
        {
            return 1f;
        }

        if (pos < feather)
        {
            return (float)pos / feather;
        }

        if (pos >= length - feather)
        {
            return (float)(length - 1 - pos) / feather;
        }

        return 1f;
    }

    public static void BlendPatchWithMask(
        float[] dest,
        int destWidth,
        int destHeight,
        float[] patch,
        int x,
        int y,
        int patchWidth,
        int patchHeight,
        int feather,
        bool[] mask)
    {
        int destHw = destWidth * destHeight;
        int patchHw = patchWidth * patchHeight;

        for (int py = 0; py < patchHeight; py++)
        {
            int destY = y + py;
            if (destY < 0 || destY >= destHeight)
            {
                continue;
            }

            float wy = EdgeWeight(py, patchHeight, feather);
            int destRow = destY * destWidth;
            int patchRow = py * patchWidth;

            for (int px = 0; px < patchWidth; px++)
            {
                int idx = patchRow + px;
                if (!mask[idx])
                {
                    continue;
                }

                int destX = x + px;
                if (destX < 0 || destX >= destWidth)
                {
                    continue;
                }

                float wx = EdgeWeight(px, patchWidth, feather);
                float weight = MathF.Min(wx, wy);
                int destIndex = destRow + destX;

                for (int c = 0; c < 3; c++)
                {
                    int destOffset = c * destHw;
                    int patchOffset = c * patchHw;
                    float baseValue = dest[destOffset + destIndex];
                    float patchValue = patch[patchOffset + idx];
                    dest[destOffset + destIndex] = baseValue * (1f - weight) + patchValue * weight;
                }
            }
        }
    }

    private static void SampleBilinear(
        float[] data,
        int width,
        int height,
        float x,
        float y,
        out float r,
        out float g,
        out float b)
    {
        r = 0f;
        g = 0f;
        b = 0f;
        if (x < 0 || y < 0 || x > width - 1 || y > height - 1)
        {
            return;
        }

        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);

        float wx = x - x0;
        float wy = y - y0;

        int hw = width * height;
        int idx00 = y0 * width + x0;
        int idx01 = y0 * width + x1;
        int idx10 = y1 * width + x0;
        int idx11 = y1 * width + x1;

        float r00 = data[idx00];
        float r01 = data[idx01];
        float r10 = data[idx10];
        float r11 = data[idx11];

        float g00 = data[hw + idx00];
        float g01 = data[hw + idx01];
        float g10 = data[hw + idx10];
        float g11 = data[hw + idx11];

        float b00 = data[2 * hw + idx00];
        float b01 = data[2 * hw + idx01];
        float b10 = data[2 * hw + idx10];
        float b11 = data[2 * hw + idx11];

        float rTop = r00 + (r01 - r00) * wx;
        float rBottom = r10 + (r11 - r10) * wx;
        float gTop = g00 + (g01 - g00) * wx;
        float gBottom = g10 + (g11 - g10) * wx;
        float bTop = b00 + (b01 - b00) * wx;
        float bBottom = b10 + (b11 - b10) * wx;

        r = rTop + (rBottom - rTop) * wy;
        g = gTop + (gBottom - gTop) * wy;
        b = bTop + (bBottom - bTop) * wy;
    }
}

internal static class FaceAlignment
{
    private static readonly float[] TemplateYunetOrder =
    {
        318.90277f, 240.1936f,
        192.98138f, 239.94708f,
        256.63416f, 314.01935f,
        313.08905f, 371.15118f,
        201.26117f, 371.41043f
    };

    public static bool TryEstimate(DetectedFace face, int targetWidth, int targetHeight, out AffineTransform transform)
    {
        transform = default;
        if (!face.HasLandmarks || face.Landmarks == null)
        {
            return false;
        }

        float scaleX = targetWidth / 512f;
        float scaleY = targetHeight / 512f;
        FloatPoint src0 = new(face.Landmarks[0], face.Landmarks[1]);
        FloatPoint src1 = new(face.Landmarks[2], face.Landmarks[3]);
        FloatPoint src2 = new(face.Landmarks[4], face.Landmarks[5]);

        FloatPoint dst0 = new(TemplateYunetOrder[0] * scaleX, TemplateYunetOrder[1] * scaleY);
        FloatPoint dst1 = new(TemplateYunetOrder[2] * scaleX, TemplateYunetOrder[3] * scaleY);
        FloatPoint dst2 = new(TemplateYunetOrder[4] * scaleX, TemplateYunetOrder[5] * scaleY);

        if (!TrySolveAffine(src0, src1, src2, dst0.X, dst1.X, dst2.X, out float a, out float b, out float c))
        {
            return false;
        }

        if (!TrySolveAffine(src0, src1, src2, dst0.Y, dst1.Y, dst2.Y, out float d, out float e, out float f))
        {
            return false;
        }

        transform = new AffineTransform(a, b, c, d, e, f);
        return true;
    }

    private static bool TrySolveAffine(
        FloatPoint p0,
        FloatPoint p1,
        FloatPoint p2,
        float v0,
        float v1,
        float v2,
        out float a,
        out float b,
        out float c)
    {
        float det = p0.X * (p1.Y - p2.Y) + p1.X * (p2.Y - p0.Y) + p2.X * (p0.Y - p1.Y);
        if (Math.Abs(det) < 1e-6f)
        {
            a = b = c = 0f;
            return false;
        }

        a = (v0 * (p1.Y - p2.Y) + v1 * (p2.Y - p0.Y) + v2 * (p0.Y - p1.Y)) / det;
        b = (v0 * (p2.X - p1.X) + v1 * (p0.X - p2.X) + v2 * (p1.X - p0.X)) / det;
        c = (v0 * (p1.X * p2.Y - p2.X * p1.Y) + v1 * (p2.X * p0.Y - p0.X * p2.Y) + v2 * (p0.X * p1.Y - p1.X * p0.Y)) / det;
        return true;
    }
}

internal readonly struct AffineTransform
{
    public AffineTransform(float a, float b, float c, float d, float e, float f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    public float A { get; }
    public float B { get; }
    public float C { get; }
    public float D { get; }
    public float E { get; }
    public float F { get; }

    public bool TryInvert(out AffineTransform inverse)
    {
        float det = A * E - B * D;
        if (Math.Abs(det) < 1e-6f)
        {
            inverse = default;
            return false;
        }

        float invDet = 1f / det;
        float ia = E * invDet;
        float ib = -B * invDet;
        float id = -D * invDet;
        float ie = A * invDet;
        float ic = -(ia * C + ib * F);
        float iff = -(id * C + ie * F);
        inverse = new AffineTransform(ia, ib, ic, id, ie, iff);
        return true;
    }
}

internal readonly struct FloatPoint
{
    public FloatPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float X { get; }
    public float Y { get; }
}
