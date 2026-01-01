using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace Upscaler.App.Processing;

public sealed class ImageTensor
{
    public int Width { get; init; }
    public int Height { get; init; }
    public float[] Data { get; init; } = new float[0];
    public BitmapMetadata? Metadata { get; init; }
}

public sealed class ImageTile
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public float[] Data { get; init; } = new float[0];
}

public sealed class ImageCrop
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public sealed class OutputOptions
{
    public string OutputPath { get; init; } = string.Empty;
    public string Format { get; init; } = "Original";
    public int JpegQuality { get; init; } = 100;
    public BitmapMetadata? SourceMetadata { get; init; }
    public string? SourcePath { get; init; }
}
