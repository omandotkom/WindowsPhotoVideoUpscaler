using Upscaler.App.Models;

namespace Upscaler.App.Processing;

public sealed class VideoUpscaleRequest
{
    public string InputPath { get; init; } = string.Empty;
    public string OutputFolder { get; init; } = string.Empty;
    public int Scale { get; init; }
    public string Mode { get; init; } = "Quality";
    public ModelDefinition? Model { get; init; }
    public int? TileSize { get; init; }
    public int TileOverlap { get; init; } = 32;
    public int JpegQuality { get; init; } = 92;
    public double DenoiseStrength { get; init; }
    public bool EnableTemporalBlend { get; init; }
    public double TemporalBlendStrength { get; init; } = 0.15;
    public bool UseHardwareDecode { get; init; }
    public string VideoEncoder { get; init; } = "CPU (libx264)";
}
