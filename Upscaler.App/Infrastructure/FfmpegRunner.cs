using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Upscaler.App.Infrastructure;

public sealed class FfmpegRunner
{
    private readonly string _ffmpegPath;
    private readonly string? _ffprobePath;

    public FfmpegRunner()
    {
        _ffmpegPath = ResolveExecutable("ffmpeg.exe")
            ?? throw new InvalidOperationException("ffmpeg.exe not found. Add it to PATH or place it next to the executable.");
        _ffprobePath = ResolveExecutable("ffprobe.exe");
    }

    public async Task RunAsync(string arguments, CancellationToken cancellationToken)
    {
        await RunProcessAsync(_ffmpegPath, arguments, cancellationToken);
    }

    public async Task RunWithProgressAsync(
        string arguments,
        double durationSeconds,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        await RunProcessWithProgressAsync(_ffmpegPath, arguments, durationSeconds, reportProgress, cancellationToken);
    }

    public async Task<double> GetFrameRateAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (_ffprobePath == null)
        {
            AppLogger.Warn("ffprobe.exe not found. Defaulting frame rate to 30.");
            return 30.0;
        }

        string args = $"-v error -select_streams v:0 -show_entries stream=avg_frame_rate -of default=nk=1:nw=1 \"{inputPath}\"";
        string output = await RunProcessAsync(_ffprobePath, args, cancellationToken);
        if (TryParseFrameRate(output, out double fps))
        {
            return fps;
        }

        AppLogger.Warn("Failed to parse frame rate. Defaulting to 30.");
        return 30.0;
    }

    public async Task<double> GetDurationSecondsAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (_ffprobePath == null)
        {
            AppLogger.Warn("ffprobe.exe not found. Duration unknown.");
            return 0;
        }

        string args = $"-v error -show_entries format=duration -of default=nk=1:nw=1 \"{inputPath}\"";
        string output = await RunProcessAsync(_ffprobePath, args, cancellationToken);
        if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0)
        {
            return seconds;
        }

        AppLogger.Warn("Failed to parse duration.");
        return 0;
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {fileName}.");
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore kill failures.
            }
        });

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            string message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(message.Trim());
        }

        return stdout.Trim();
    }

    private static async Task RunProcessWithProgressAsync(
        string fileName,
        string arguments,
        double durationSeconds,
        Action<double>? reportProgress,
        CancellationToken cancellationToken)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {fileName}.");
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore kill failures.
            }
        });

        string? lastError = null;
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lastError = e.Data;
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data) || reportProgress == null || durationSeconds <= 0)
            {
                return;
            }

            string[] parts = e.Data.Split('=', 2);
            if (parts.Length != 2)
            {
                return;
            }

            if (parts[0] == "out_time_ms"
                && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long outMs))
            {
                double percent = Math.Clamp(outMs / (durationSeconds * 1000.0) * 100.0, 0.0, 100.0);
                reportProgress(percent);
            }
            else if (parts[0] == "progress" && parts[1] == "end")
            {
                reportProgress(100.0);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(lastError ?? $"ffmpeg failed with exit code {process.ExitCode}.");
        }
    }

    private static bool TryParseFrameRate(string text, out double fps)
    {
        fps = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        if (trimmed.Contains('/'))
        {
            string[] parts = trimmed.Split('/');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double num)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double den)
                && den > 0)
            {
                fps = num / den;
                return fps > 0;
            }
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out fps) && fps > 0;
    }

    private static string? ResolveExecutable(string fileName)
    {
        string appDataCandidate = Path.Combine(AppPaths.FfmpegPath, fileName);
        if (File.Exists(appDataCandidate))
        {
            return appDataCandidate;
        }

        string baseDirCandidate = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(baseDirCandidate))
        {
            return baseDirCandidate;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string segment in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            string candidate = Path.Combine(segment.Trim().Trim('"'), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
