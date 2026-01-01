using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Upscaler.App.Infrastructure;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static string? _currentLogPath;

    public static void Initialize()
    {
        lock (Sync)
        {
            _currentLogPath = ResolveLogPath();
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Sync)
            {
                string logPath = EnsureCurrentLog();
                string timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                string line = $"[{timestamp}] {level} {message}";
                if (ex != null)
                {
                    line += $" | {ex}";
                }

                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Swallow logging failures to keep UI responsive.
        }
    }

    private static string EnsureCurrentLog()
    {
        if (_currentLogPath == null)
        {
            _currentLogPath = ResolveLogPath();
        }

        return _currentLogPath;
    }

    private static string ResolveLogPath()
    {
        Directory.CreateDirectory(AppPaths.LogsPath);
        return Path.Combine(AppPaths.LogsPath, "app.log");
    }
}
