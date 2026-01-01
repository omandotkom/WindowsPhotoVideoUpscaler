using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Upscaler.App.Processing;

public interface IImagePreprocessor
{
    Task<ImageTensor> LoadAsync(string path, ImageCrop? crop, double denoiseStrength, CancellationToken cancellationToken);
}

public interface ITileSplitter
{
    IReadOnlyList<ImageTile> Split(ImageTensor image, int tileSize, int overlap);
}

public interface IInferenceEngine
{
    int? PreferredTileSize { get; }
    Task<IReadOnlyList<ImageTile>> InferAsync(
        IReadOnlyList<ImageTile> tiles,
        IProgress<TileProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ITileMerger
{
    ImageTensor Merge(IReadOnlyList<ImageTile> tiles, int outputWidth, int outputHeight, int overlap);
}

public interface IImagePostprocessor
{
    Task SaveAsync(ImageTensor image, OutputOptions options, CancellationToken cancellationToken);
}
