using AlbionDataAvalonia.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.ComponentModel;

namespace AlbionDataAvalonia.Views;

public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private bool _isShuttingDown;
    private bool _suppressMinimizeToTrayOnce;

    public MainWindow(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        InitializeComponent();
        Closing += OnClosing;
    }

    public void ShowMinimizedToTaskbar()
    {
        _suppressMinimizeToTrayOnce = true;
        Show();
        WindowState = WindowState.Minimized;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != WindowStateProperty || WindowState != WindowState.Minimized)
        {
            return;
        }

        if (_suppressMinimizeToTrayOnce)
        {
            _suppressMinimizeToTrayOnce = false;
            return;
        }

        if (_isShuttingDown || !_settingsManager.UserSettings.MinimizeButtonMinimizesToTray)
        {
            return;
        }

        Dispatcher.UIThread.Post(HideMinimizedWindowToTray);
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

    private void HideMinimizedWindowToTray()
    {
        if (_isShuttingDown || !_settingsManager.UserSettings.MinimizeButtonMinimizesToTray || WindowState != WindowState.Minimized)
        {
            return;
        }

        Hide();
        WindowState = WindowState.Normal;
    }
}
