using System;
using System.Collections.Generic;

namespace Upscaler.App.Processing;

public sealed class SimpleTileSplitter : ITileSplitter
{
    public IReadOnlyList<ImageTile> Split(ImageTensor image, int tileSize, int overlap)
    {
        if (tileSize <= 0)
        {
            return new List<ImageTile>
            {
                new()
                {
                    X = 0,
                    Y = 0,
                    Width = image.Width,
                    Height = image.Height,
                    Data = image.Data
                }
            };
        }

        int clampedOverlap = Math.Clamp(overlap, 0, Math.Max(0, tileSize / 4));
        int step = Math.Max(1, tileSize - clampedOverlap * 2);

        List<ImageTile> tiles = new();
        for (int y = 0; y < image.Height; y += step)
        {
            for (int x = 0; x < image.Width; x += step)
            {
                int width = tileSize;
                int height = tileSize;
                float[] tileData = ExtractTile(image, x, y, width, height);
                tiles.Add(new ImageTile
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    Data = tileData
                });

                if (x + width >= image.Width)
                {
                    break;
                }
            }

            if (y + tileSize >= image.Height)
            {
                break;
            }
        }

        return tiles;
    }

    private static float[] ExtractTile(ImageTensor image, int x, int y, int width, int height)
    {
        float[] tile = new float[width * height * 3];
        int hw = image.Width * image.Height;
        int tileHw = width * height;

        for (int ty = 0; ty < height; ty++)
        {
            int srcY = y + ty;
            int dstRow = ty * width;
            if (srcY < 0 || srcY >= image.Height)
            {
                continue;
            }

            int srcRow = srcY * image.Width;
            for (int tx = 0; tx < width; tx++)
            {
                int srcX = x + tx;
                if (srcX < 0 || srcX >= image.Width)
                {
                    continue;
                }

                int srcIndex = srcRow + srcX;
                int dstIndex = dstRow + tx;
                tile[dstIndex] = image.Data[srcIndex];
                tile[tileHw + dstIndex] = image.Data[hw + srcIndex];
                tile[2 * tileHw + dstIndex] = image.Data[2 * hw + srcIndex];
            }
        }

        return tile;
    }
}
