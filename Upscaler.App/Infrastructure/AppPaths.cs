using System;
using System.IO;

namespace Upscaler.App.Infrastructure;

public static class AppPaths
{
    private const string AppDataFolderName = "AppData";
    private const string AppName = "Upscaler";

    public static string BasePath { get; } = ResolveBasePath();
    public static string ModelsPath { get; } = Path.Combine(BasePath, "models");
    public static string CachePath { get; } = Path.Combine(BasePath, "cache");
    public static string LogsPath { get; } = Path.Combine(BasePath, "logs");
    public static string OutputPath { get; } = ResolveOutputPath();

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(BasePath);
        Directory.CreateDirectory(ModelsPath);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(OutputPath);
    }

    private static string ResolveBasePath()
    {
        string exeDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(exeDir, AppDataFolderName);
        if (IsWritable(candidate))
        {
            return candidate;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppName);
    }

    private static string ResolveOutputPath()
    {
        string exeDir = AppContext.BaseDirectory;
        string candidate = Path.Combine(exeDir, "output");
        if (IsWritable(candidate))
        {
            return candidate;
        }

        return Path.Combine(BasePath, "output");
    }

    private static bool IsWritable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            string testFile = Path.Combine(path, ".write_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
