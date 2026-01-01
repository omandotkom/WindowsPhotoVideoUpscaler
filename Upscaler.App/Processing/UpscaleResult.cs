using System.Collections.Generic;

namespace Upscaler.App.Processing;

public sealed class UpscaleResult
{
    public IReadOnlyList<string> OutputFiles { get; init; } = new List<string>();
}
