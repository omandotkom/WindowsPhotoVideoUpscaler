using System;

namespace Upscaler.App.Processing;

public sealed class UpscaleProgress
{
    public int CurrentIndex { get; init; }
    public int Total { get; init; }
    public int TileIndex { get; init; }
    public int TileTotal { get; init; }
    public double OverallPercent { get; init; }
    public TimeSpan? Eta { get; init; }
    public string Message { get; init; } = string.Empty;
}
