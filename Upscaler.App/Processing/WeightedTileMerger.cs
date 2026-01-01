using System;
using System.Collections.Generic;

namespace Upscaler.App.Processing;

public sealed class WeightedTileMerger : ITileMerger
{
    public ImageTensor Merge(IReadOnlyList<ImageTile> tiles, int outputWidth, int outputHeight, int overlap)
    {
        if (tiles.Count == 0)
        {
            return new ImageTensor { Width = outputWidth, Height = outputHeight, Data = new float[0] };
        }

        float[] output = new float[outputWidth * outputHeight * 3];
        float[] weights = new float[outputWidth * outputHeight];
        int outputHw = outputWidth * outputHeight;

        foreach (ImageTile tile in tiles)
        {
            int tileHw = tile.Width * tile.Height;
            for (int ty = 0; ty < tile.Height; ty++)
            {
                int destY = tile.Y + ty;
                if (destY >= outputHeight)
                {
                    continue;
                }

                for (int tx = 0; tx < tile.Width; tx++)
                {
                    int destX = tile.X + tx;
                    if (destX >= outputWidth)
                    {
                        continue;
                    }

                    float w = ComputeWeight(tx, ty, tile.Width, tile.Height, overlap);
                    int destIndex = destY * outputWidth + destX;
                    weights[destIndex] += w;

                    int tileIndex = ty * tile.Width + tx;
                    output[destIndex] += tile.Data[tileIndex] * w;
                    output[outputHw + destIndex] += tile.Data[tileHw + tileIndex] * w;
                    output[2 * outputHw + destIndex] += tile.Data[2 * tileHw + tileIndex] * w;
                }
            }
        }

        for (int i = 0; i < weights.Length; i++)
        {
            float w = weights[i];
            if (w > 0)
            {
                output[i] /= w;
                output[outputHw + i] /= w;
                output[2 * outputHw + i] /= w;
            }
        }

        return new ImageTensor
        {
            Width = outputWidth,
            Height = outputHeight,
            Data = output
        };
    }

    private static float ComputeWeight(int x, int y, int width, int height, int overlap)
    {
        int safeOverlap = Math.Clamp(overlap, 0, Math.Min(width, height) / 2);
        if (safeOverlap <= 0)
        {
            return 1f;
        }

        float wx = 1f;
        if (x < safeOverlap)
        {
            wx = (float)x / safeOverlap;
        }
        else if (x > width - safeOverlap - 1)
        {
            wx = (float)(width - x - 1) / safeOverlap;
        }

        float wy = 1f;
        if (y < safeOverlap)
        {
            wy = (float)y / safeOverlap;
        }
        else if (y > height - safeOverlap - 1)
        {
            wy = (float)(height - y - 1) / safeOverlap;
        }

        return Math.Clamp(wx * wy, 0f, 1f);
    }
}
