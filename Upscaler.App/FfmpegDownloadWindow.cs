using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Upscaler.App;

public sealed class FfmpegDownloadWindow : Window
{
    private readonly System.Windows.Controls.TextBlock _status;
    private readonly System.Windows.Controls.ProgressBar _progress;

    public FfmpegDownloadWindow()
    {
        Title = "Downloading FFmpeg";
        Width = 360;
        Height = 140;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = System.Windows.Media.Brushes.White;

        StackPanel panel = new()
        {
            Margin = new Thickness(16)
        };

        _status = new System.Windows.Controls.TextBlock
        {
            Text = "Preparing download...",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59)),
            Margin = new Thickness(0, 0, 0, 12)
        };

        _progress = new System.Windows.Controls.ProgressBar
        {
            Height = 18,
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true
        };

        panel.Children.Add(_status);
        panel.Children.Add(_progress);
        Content = panel;
    }

    public void ReportStatus(string message)
    {
        Dispatcher.Invoke(() => _status.Text = message);
    }

    public void ReportProgress(double percent)
    {
        Dispatcher.Invoke(() =>
        {
            _progress.IsIndeterminate = false;
            _progress.Value = Math.Clamp(percent, 0, 100);
        });
    }
}
