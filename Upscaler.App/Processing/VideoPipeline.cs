using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Upscaler.App.Infrastructure;

namespace Upscaler.App.Processing;

public sealed class VideoPipeline
{
    private readonly ImagePipeline _imagePipeline;
    private readonly FfmpegRunner _ffmpeg;

    public VideoPipeline(ImagePipeline imagePipeline, FfmpegRunner ffmpeg)
    {
        _imagePipeline = imagePipeline;
        _ffmpeg = ffmpeg;
    }

    public async Task<string> UpscaleAsync(
        VideoUpscaleRequest request,
        IProgress<UpscaleProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InputPath))
        {
            throw new InvalidOperationException("No input video specified.");
        }

        string tempRoot = Path.Combine(AppPaths.CachePath, $"video_{Guid.NewGuid():N}");
        string inputFrames = Path.Combine(tempRoot, "input");
        string outputFrames = Path.Combine(tempRoot, "output");
        Directory.CreateDirectory(inputFrames);
        Directory.CreateDirectory(outputFrames);

        try
        {
            double durationSeconds = await _ffmpeg.GetDurationSecondsAsync(request.InputPath, cancellationToken);
            const double extractWeight = 0.15;
            const double upscaleWeight = 0.7;
            const double encodeWeight = 0.15;
            progress?.Report(new UpscaleProgress { Message = "Decoding frames..." });
            string inputPattern = Path.Combine(inputFrames, "frame_%06d.png");
            string hwDecode = request.UseHardwareDecode ? "-hwaccel auto " : string.Empty;
            string extractArgs = $"-y -hide_banner -loglevel error -progress pipe:1 -nostats {hwDecode}-i \"{request.InputPath}\" -vsync 0 \"{inputPattern}\"";
            await _ffmpeg.RunWithProgressAsync(
                extractArgs,
                durationSeconds,
                percent => progress?.Report(new UpscaleProgress
                {
                    OverallPercent = percent * extractWeight,
                    Message = $"Decoding frames ({percent:0}%)"
                }),
                cancellationToken);

            List<string> frames = Directory.EnumerateFiles(inputFrames, "frame_*.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (frames.Count == 0)
            {
                throw new InvalidOperationException("No frames extracted from the video.");
            }

            UpscaleRequest imageRequest = new()
            {
                InputFiles = frames,
                OutputFolder = outputFrames,
                Scale = request.Scale,
                Mode = request.Mode,
                Model = request.Model,
                TileSize = request.TileSize,
                TileOverlap = request.TileOverlap,
                OutputFormat = "Png",
                JpegQuality = request.JpegQuality,
                DenoiseStrength = request.DenoiseStrength,
                EnableFaceRefinement = false,
                EnableTemporalBlend = request.EnableTemporalBlend,
                TemporalBlendStrength = request.TemporalBlendStrength
            };

            Progress<UpscaleProgress> mappedProgress = new(p =>
            {
                double overall = extractWeight * 100 + p.OverallPercent * upscaleWeight;
                progress?.Report(new UpscaleProgress
                {
                    CurrentIndex = p.CurrentIndex,
                    Total = p.Total,
                    TileIndex = p.TileIndex,
                    TileTotal = p.TileTotal,
                    OverallPercent = overall,
                    Message = $"Upscaling frames ({p.OverallPercent:0}%)"
                });
            });
            await _imagePipeline.UpscaleAsync(imageRequest, mappedProgress, cancellationToken);

            progress?.Report(new UpscaleProgress { Message = "Encoding video..." });
            double fps = await _ffmpeg.GetFrameRateAsync(request.InputPath, cancellationToken);
            string outputPath = OutputNaming.BuildVideoOutputPath(request.InputPath, request.OutputFolder, request.Scale);
            string outputPattern = Path.Combine(outputFrames, "frame_%06d_upscaled.png");
            (int frameCount, int startNumber) = GetFrameSequenceInfo(outputFrames);
            AppLogger.Info($"Encoding {frameCount} frames starting at {startNumber}.");
            string encoder = ResolveVideoEncoder(request.VideoEncoder);
            try
            {
                await RunEncodeWithAudioFallbacksAsync(
                    outputPattern,
                    request.InputPath,
                    outputPath,
                    fps,
                    durationSeconds,
                    progress,
                    cancellationToken,
                    encoder,
                    startNumber,
                    (extractWeight + upscaleWeight) * 100,
                    encodeWeight);
            }
            catch (Exception ex)
            {
                if (!string.Equals(encoder, "mpeg4", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warn($"Video encoder '{encoder}' failed. Falling back to mpeg4. Error: {ex.Message}");
                    try
                    {
                        await RunEncodeWithAudioFallbacksAsync(
                            outputPattern,
                            request.InputPath,
                            outputPath,
                            fps,
                            durationSeconds,
                            progress,
                            cancellationToken,
                            "mpeg4",
                            startNumber,
                            (extractWeight + upscaleWeight) * 100,
                            encodeWeight);
                    }
                    catch (Exception fallbackEx)
                    {
                        AppLogger.Warn($"mpeg4 fallback failed. Error: {fallbackEx.Message}");
                        throw;
                    }
                }
                else
                {
                    AppLogger.Warn($"mpeg4 encoding failed. Error: {ex.Message}");
                    throw;
                }
            }

            return outputPath;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to clean temp video cache: {ex.Message}");
            }
        }
    }

    private static string BuildEncodeArgs(
        string framesPattern,
        string inputPath,
        string outputPath,
        double fps,
        string encoder,
        int startNumber,
        string audioMode)
    {
        string framerate = fps.ToString("0.###", CultureInfo.InvariantCulture);
        string audioArgs = audioMode switch
        {
            "aac" => "-c:a aac",
            "none" => "-an",
            _ => "-c:a copy"
        };
        return $"-y -hide_banner -loglevel error -framerate {framerate} -start_number {startNumber} -i \"{framesPattern}\" -i \"{inputPath}\" -map 0:v -map 1:a? -c:v {encoder} -pix_fmt yuv420p {audioArgs} \"{outputPath}\"";
    }

    private async Task RunEncodeWithAudioFallbacksAsync(
        string framesPattern,
        string inputPath,
        string outputPath,
        double fps,
        double durationSeconds,
        IProgress<UpscaleProgress>? progress,
        CancellationToken cancellationToken,
        string encoder,
        int startNumber,
        double basePercent,
        double weight)
    {
        string[] audioModes = { "copy", "aac", "none" };
        Exception? lastError = null;

        foreach (string audioMode in audioModes)
        {
            string args = BuildEncodeArgs(framesPattern, inputPath, outputPath, fps, encoder, startNumber, audioMode);
            string progressArgs = args.Replace("-hide_banner -loglevel error ", "-hide_banner -loglevel error -progress pipe:1 -nostats ");
            AppLogger.Info($"Encoding video with {encoder}, audio={audioMode}.");
            try
            {
                await _ffmpeg.RunWithProgressAsync(
                    progressArgs,
                    durationSeconds,
                    percent => progress?.Report(new UpscaleProgress
                    {
                        OverallPercent = basePercent + percent * weight,
                        Message = $"Encoding video ({percent:0}%)"
                    }),
                    cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Encoding failed with {encoder} audio={audioMode}. Error: {ex.Message}");
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("Encoding failed.");
    }

    private static string ResolveVideoEncoder(string selection)
    {
        if (selection.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return "h264_nvenc";
        }

        if (selection.StartsWith("AMD", StringComparison.OrdinalIgnoreCase))
        {
            return "h264_amf";
        }

        if (selection.StartsWith("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return "h264_qsv";
        }

        return "libx264";
    }

    private static (int count, int startNumber) GetFrameSequenceInfo(string outputFrames)
    {
        List<string> files = Directory.EnumerateFiles(outputFrames, "frame_*_upscaled.png")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            throw new InvalidOperationException("No upscaled frames found for encoding.");
        }

        int? minIndex = null;
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            const string prefix = "frame_";
            const string suffix = "_upscaled";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string number = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                minIndex = minIndex.HasValue ? Math.Min(minIndex.Value, value) : value;
            }
        }

        if (!minIndex.HasValue)
        {
            throw new InvalidOperationException("Failed to parse frame indices for encoding.");
        }

        return (files.Count, minIndex.Value);
    }
}
