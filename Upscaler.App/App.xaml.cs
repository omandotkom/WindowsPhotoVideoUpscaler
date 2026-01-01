using Upscaler.App.Infrastructure;

namespace Upscaler.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();
        AppLogger.Initialize();
        AppLogger.Info("Application started.");
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        AppLogger.Info("Application exiting.");
        Processing.OnnxInferenceEngine.ClearAllSessions();
        base.OnExit(e);
    }
}
