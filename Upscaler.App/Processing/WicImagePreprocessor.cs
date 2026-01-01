using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Upscaler.App.Infrastructure;

namespace Upscaler.App.Processing;

public sealed class WicImagePreprocessor : IImagePreprocessor
{
    public Task<ImageTensor> LoadAsync(string path, ImageCrop? crop, double denoiseStrength, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = File.OpenRead(path);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            BitmapFrame frame = decoder.Frames[0];
            BitmapMetadata? metadata = frame.Metadata as BitmapMetadata;

            BitmapSource source = frame;
            if (crop != null)
            {
                Int32Rect rect = new(crop.X, crop.Y, crop.Width, crop.Height);
                source = new CroppedBitmap(source, rect);
            }

            FormatConvertedBitmap converted = new();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Rgb24;
            converted.EndInit();
            converted.Freeze();

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 3;
            byte[] pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            float[] data = new float[width * height * 3];
            int hw = width * height;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                int rowIndex = y * width;
                for (int x = 0; x < width; x++)
                {
                    int pixelOffset = rowOffset + x * 3;
                    int baseIndex = rowIndex + x;
                    data[baseIndex] = pixels[pixelOffset] / 255f;
                    data[hw + baseIndex] = pixels[pixelOffset + 1] / 255f;
                    data[2 * hw + baseIndex] = pixels[pixelOffset + 2] / 255f;
                }
            }

            if (denoiseStrength > 0)
            {
                float strength = (float)Math.Clamp(denoiseStrength, 0.0, 1.0);
                ApplyBoxBlur(data, width, height, strength);
            }

            return new ImageTensor
            {
                Width = width,
                Height = height,
                Data = data,
                Metadata = metadata
            };
        }, cancellationToken);
    }

    private static void ApplyBoxBlur(float[] data, int width, int height, float strength)
    {
        if (strength <= 0)
        {
            return;
        }

        int hw = width * height;
        float[] blurred = new float[data.Length];
        int[] offsets = { -1, 0, 1 };

        for (int c = 0; c < 3; c++)
        {
            int channelOffset = c * hw;
            for (int y = 0; y < height; y++)
            {
                int yBase = y * width;
                for (int x = 0; x < width; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    foreach (int oy in offsets)
                    {
                        int yy = y + oy;
                        if (yy < 0 || yy >= height)
                        {
                            continue;
                        }
                        int row = yy * width;
                        foreach (int ox in offsets)
                        {
                            int xx = x + ox;
                            if (xx < 0 || xx >= width)
                            {
                                continue;
                            }
                            sum += data[channelOffset + row + xx];
                            count++;
                        }
                    }

                    blurred[channelOffset + yBase + x] = count > 0 ? sum / count : data[channelOffset + yBase + x];
                }
            }
        }

        float inv = 1f - strength;
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = data[i] * inv + blurred[i] * strength;
        }
    }
}
