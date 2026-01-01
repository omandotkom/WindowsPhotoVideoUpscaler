using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Upscaler.App.Infrastructure;

public static class AppLogger
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private static readonly object Sync = new();
    private static string? _currentLogPath;
    private static DateTime _currentDate;

    public static void Initialize()
    {
        lock (Sync)
        {
            _currentDate = DateTime.UtcNow.Date;
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
                TrimOldLogs();
            }
        }
        catch
        {
            // Swallow logging failures to keep UI responsive.
        }
    }

    private static string EnsureCurrentLog()
    {
        DateTime today = DateTime.UtcNow.Date;
        if (_currentLogPath == null || today != _currentDate)
        {
            _currentDate = today;
            _currentLogPath = ResolveLogPath();
        }

        FileInfo info = new(_currentLogPath);
        if (info.Exists && info.Length > MaxLogBytes)
        {
            _currentLogPath = ResolveLogPath();
        }

        return _currentLogPath;
    }

    private static string ResolveLogPath()
    {
        Directory.CreateDirectory(AppPaths.LogsPath);
        string datePart = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string basePath = Path.Combine(AppPaths.LogsPath, $"{datePart}.log");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        for (int i = 1; i < 100; i++)
        {
            string candidate = Path.Combine(AppPaths.LogsPath, $"{datePart}-{i}.log");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return basePath;
    }

    private static void TrimOldLogs()
    {
        try
        {
            DirectoryInfo dir = new(AppPaths.LogsPath);
            FileInfo[] files = dir.GetFiles("*.log");
            if (files.Length <= 20)
            {
                return;
            }

            Array.Sort(files, (a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));
            for (int i = 0; i < files.Length - 20; i++)
            {
                files[i].Delete();
            }
        }
        catch
        {
            // Ignore log trimming failures.
        }
    }
}
