using AlbionDataAvalonia.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.ComponentModel;

namespace AlbionDataAvalonia.Views;

public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private bool _isShuttingDown;

    public MainWindow(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_settingsManager.UserSettings.ShutDownOnClose)
        {
            if (!_isShuttingDown && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _isShuttingDown = true;
                e.Cancel = true;
                desktop.Shutdown();
            }

            return;
        }

        e.Cancel = true;
        IsVisible = false;
    }
}
