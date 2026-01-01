using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExifLibrary;

namespace Upscaler.App.Processing;

public sealed class WicImagePostprocessor : IImagePostprocessor
{
    public Task SaveAsync(ImageTensor image, OutputOptions options, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int width = image.Width;
            int height = image.Height;
            int stride = width * 3;
            byte[] pixels = new byte[stride * height];
            int hw = width * height;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                int rowIndex = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowIndex + x;
                    pixels[rowOffset + x * 3] = ToByte(image.Data[idx]);
                    pixels[rowOffset + x * 3 + 1] = ToByte(image.Data[hw + idx]);
                    pixels[rowOffset + x * 3 + 2] = ToByte(image.Data[2 * hw + idx]);
                }
            }

            BitmapSource source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Rgb24,
                null,
                pixels,
                stride);

            BitmapEncoder encoder = CreateEncoder(options);
            BitmapFrame frame = CreateFrameWithMetadata(source, options.SourceMetadata);
            encoder.Frames.Add(frame);

            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? ".");
            using FileStream stream = new(options.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);

            TryCopyExif(options);
        }, cancellationToken);
    }

    private static BitmapEncoder CreateEncoder(OutputOptions options)
    {
        string format = options.Format.ToLowerInvariant();
        return format switch
        {
            "png" => new PngBitmapEncoder(),
            "jpeg" => CreateJpegEncoder(options.JpegQuality),
            "jpg" => CreateJpegEncoder(options.JpegQuality),
            "bmp" => new BmpBitmapEncoder(),
            "tiff" => new TiffBitmapEncoder(),
            "original" => new PngBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
    }

    private static BitmapEncoder CreateJpegEncoder(int quality)
    {
        JpegBitmapEncoder encoder = new()
        {
            QualityLevel = Math.Clamp(quality, 1, 100)
        };
        return encoder;
    }

    private static byte ToByte(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return (byte)Math.Round(value * 255f);
    }

    private static BitmapFrame CreateFrameWithMetadata(BitmapSource source, BitmapMetadata? metadata)
    {
        if (metadata == null)
        {
            return BitmapFrame.Create(source);
        }

        try
        {
            BitmapMetadata cloned = metadata.Clone() as BitmapMetadata ?? metadata;
            return BitmapFrame.Create(source, null, cloned, null);
        }
        catch
        {
            return BitmapFrame.Create(source);
        }
    }

    private static void TryCopyExif(OutputOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourcePath) || string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return;
        }

        string ext = Path.GetExtension(options.OutputPath).ToLowerInvariant();
        if (ext != ".jpg" && ext != ".jpeg" && ext != ".tiff")
        {
            return;
        }

        try
        {
            ImageFile source = ImageFile.FromFile(options.SourcePath);
            ImageFile target = ImageFile.FromFile(options.OutputPath);
            foreach (ExifProperty prop in source.Properties)
            {
                target.Properties.Remove(prop);
                target.Properties.Add(prop);
            }

            target.Save(options.OutputPath);
        }
        catch
        {
            // Best effort; ignore EXIF copy failures.
        }
    }
}
