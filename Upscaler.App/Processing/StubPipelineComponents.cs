using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Upscaler.App.Processing;

public sealed class StubPreprocessor : IImagePreprocessor
{
    public Task<ImageTensor> LoadAsync(string path, ImageCrop? crop, CancellationToken cancellationToken)
        => throw new NotSupportedException("Preprocessing not implemented yet.");
}

public sealed class StubTileSplitter : ITileSplitter
{
    public IReadOnlyList<ImageTile> Split(ImageTensor image, int tileSize, int overlap)
        => throw new NotSupportedException("Tiling not implemented yet.");
}

public sealed class StubInferenceEngine : IInferenceEngine
{
    public int? PreferredTileSize => null;

    public Task<IReadOnlyList<ImageTile>> InferAsync(
        IReadOnlyList<ImageTile> tiles,
        IProgress<TileProgress>? progress,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Inference not implemented yet.");
}

public sealed class StubTileMerger : ITileMerger
{
    public ImageTensor Merge(IReadOnlyList<ImageTile> tiles, int outputWidth, int outputHeight, int overlap)
        => throw new NotSupportedException("Tile merge not implemented yet.");
}

public sealed class StubPostprocessor : IImagePostprocessor
{
    public Task SaveAsync(ImageTensor image, OutputOptions options, CancellationToken cancellationToken)
        => throw new NotSupportedException("Postprocessing not implemented yet.");
}
