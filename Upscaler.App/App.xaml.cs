using System;
using System.Threading.Tasks;
using Upscaler.App.Infrastructure;
using System.Windows;

namespace Upscaler.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();
        AppLogger.Initialize();
        AppLogger.Info("Application started.");

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new MainWindow();

        MainWindow?.Show();
        _ = EnsureFfmpegAsync();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        AppLogger.Info("Application exiting.");
        Processing.OnnxInferenceEngine.ClearAllSessions();
        base.OnExit(e);
    }

    private async Task EnsureFfmpegAsync()
    {
        if (FfmpegInstaller.IsAvailable())
        {
            return;
        }

        FfmpegDownloadWindow window = new();
        window.Show();
        try
        {
            bool ready = await FfmpegInstaller.EnsureAvailableAsync(window.ReportStatus, window.ReportProgress);
            if (!ready)
            {
                AppLogger.Warn("FFmpeg unavailable. Video upscaling will be disabled.");
                System.Windows.MessageBox.Show(
                    "FFmpeg download failed. Video upscaling will be unavailable until FFmpeg is installed.",
                    "FFmpeg unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("FFmpeg setup failed.", ex);
        }
        finally
        {
            window.Close();
        }
    }
}
