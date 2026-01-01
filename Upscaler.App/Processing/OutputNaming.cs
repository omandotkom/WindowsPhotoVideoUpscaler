using System;
using System.IO;
using Upscaler.App.Infrastructure;

namespace Upscaler.App.Processing;

public static class OutputNaming
{
    public static string BuildOutputPath(string inputPath, string outputFolder, int scale, string format)
    {
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string ext = ResolveExtension(inputPath, format);
        string fileName = $"{name}_upscaled.{ext}";
        return Path.Combine(outputFolder, fileName);
    }

    private static string ResolveExtension(string inputPath, string format)
    {
        if (!string.Equals(format, "Original", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeExtension(format);
        }

        string originalExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        if (IsSupportedOutputExtension(originalExt))
        {
            return originalExt;
        }

        AppLogger.Warn($"Output format '{originalExt}' unsupported. Falling back to png.");
        return "png";
    }

    private static string NormalizeExtension(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "jpeg" => "jpg",
            "jpg" => "jpg",
            "png" => "png",
            "bmp" => "bmp",
            "tiff" => "tiff",
            _ => "png"
        };
    }

    private static bool IsSupportedOutputExtension(string ext)
    {
        return ext is "jpg" or "jpeg" or "png" or "bmp" or "tiff";
    }
}
