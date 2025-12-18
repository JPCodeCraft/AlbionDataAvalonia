using AlbionDataAvalonia.Settings;
using Avalonia.Controls;
using System.ComponentModel;

namespace AlbionDataAvalonia.Views;

public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    public MainWindow(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // On macOS, close the window to quit (standard behavior)
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            return; // allow close
        }
        // On other OSes, hide to tray
        e.Cancel = true;
        IsVisible = false;
    }
}
