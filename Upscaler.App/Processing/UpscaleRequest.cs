using System.Collections.Generic;
using Upscaler.App.Models;

namespace Upscaler.App.Processing;

public sealed class UpscaleRequest
{
    public IReadOnlyList<string> InputFiles { get; init; } = new List<string>();
    public string OutputFolder { get; init; } = string.Empty;
    public int Scale { get; init; }
    public string Mode { get; init; } = "Quality";
    public ModelDefinition? Model { get; init; }
    public int? TileSize { get; init; }
    public int TileOverlap { get; init; } = 32;
    public bool SkipDuplicates { get; init; } = true;
    public string OutputFormat { get; init; } = "Original";
    public int JpegQuality { get; init; } = 92;
    public ImageCrop? PreviewCrop { get; init; }
}
