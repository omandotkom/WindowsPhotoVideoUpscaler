using System;
using System.Linq;
using System.Management;

namespace Upscaler.App.Infrastructure;

public static class DeviceInfoService
{
    public static string GetPrimaryGpuName()
    {
        try
        {
            using ManagementObjectSearcher searcher = new("select Name from Win32_VideoController");
            using ManagementObjectCollection results = searcher.Get();
            string? name = results.Cast<ManagementObject>()
                .Select(mo => mo["Name"]?.ToString())
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

            return name ?? "Unknown GPU";
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GPU detection failed: {ex.Message}");
            return "Unknown GPU";
        }
    }
}
