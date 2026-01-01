using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Upscaler.App.Infrastructure;

public static class FfmpegInstaller
{
    private const string FallbackDownloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n6.1-latest-win64-lgpl.zip";

    public static bool IsAvailable()
    {
        string ffmpeg = Path.Combine(AppPaths.FfmpegPath, "ffmpeg.exe");
        string ffprobe = Path.Combine(AppPaths.FfmpegPath, "ffprobe.exe");
        return File.Exists(ffmpeg) && File.Exists(ffprobe);
    }

    public static async Task<bool> EnsureAvailableAsync(Action<string>? status, Action<double>? progress)
    {
        if (IsAvailable())
        {
            return true;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}");
        string zipPath = Path.Combine(tempRoot, "ffmpeg.zip");
        Directory.CreateDirectory(tempRoot);

        try
        {
            status?.Invoke("Resolving FFmpeg download...");
            string downloadUrl = await ResolveDownloadUrlAsync();
            status?.Invoke("Downloading FFmpeg...");
            await DownloadAsync(downloadUrl, zipPath, progress);

            status?.Invoke("Extracting FFmpeg...");
            await Task.Run(() =>
            {
                string extractRoot = Path.Combine(tempRoot, "extract");
                ZipFile.ExtractToDirectory(zipPath, extractRoot);

                string? ffmpegExe = FindFile(extractRoot, "ffmpeg.exe");
                if (ffmpegExe == null)
                {
                    throw new InvalidOperationException("ffmpeg.exe not found in archive.");
                }

                string binDir = Path.GetDirectoryName(ffmpegExe) ?? throw new InvalidOperationException("Invalid FFmpeg path.");
                Directory.CreateDirectory(AppPaths.FfmpegPath);
                foreach (string file in Directory.EnumerateFiles(binDir))
                {
                    string target = Path.Combine(AppPaths.FfmpegPath, Path.GetFileName(file));
                    File.Copy(file, target, true);
                }
            });

            return IsAvailable();
        }
        catch (Exception ex)
        {
            AppLogger.Error("FFmpeg download failed.", ex);
            return false;
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
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private static async Task DownloadAsync(string url, string destination, Action<double>? progress)
    {
        using HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Upscaler", "1.0"));
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long read = 0;

        while (true)
        {
            int bytes = await input.ReadAsync(buffer);
            if (bytes == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, bytes));
            read += bytes;
            if (total.HasValue && total.Value > 0)
            {
                double percent = Math.Clamp((double)read / total.Value * 100.0, 0.0, 100.0);
                progress?.Invoke(percent);
            }
        }
    }

    private static async Task<string> ResolveDownloadUrlAsync()
    {
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Upscaler", "1.0"));
            using HttpResponseMessage response = await client.GetAsync("https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest");
            response.EnsureSuccessStatusCode();

            using Stream stream = await response.Content.ReadAsStreamAsync();
            using JsonDocument doc = await JsonDocument.ParseAsync(stream);
            if (doc.RootElement.TryGetProperty("assets", out JsonElement assets))
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out JsonElement nameElement)
                        || !asset.TryGetProperty("browser_download_url", out JsonElement urlElement))
                    {
                        continue;
                    }

                    string name = nameElement.GetString() ?? string.Empty;
                    if (name.Contains("win64", StringComparison.OrdinalIgnoreCase)
                        && name.Contains("lgpl", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        return urlElement.GetString() ?? FallbackDownloadUrl;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to resolve FFmpeg download URL. Falling back to default. Error: {ex.Message}");
        }

        return FallbackDownloadUrl;
    }

    private static string? FindFile(string root, string fileName)
    {
        foreach (string file in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
        {
            return file;
        }

        return null;
    }
}
