using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Linq;

namespace AlbionDataAvalonia.Ui;

/// <summary>Finds the window that dialogs, pickers and the clipboard should use.</summary>
public static class UiWindows
{
    /// <summary>The focused app window, or the main window when none has focus.</summary>
    public static Window? Active => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
        ? desktop.Windows.FirstOrDefault(window => window.IsActive && window.IsVisible) ?? desktop.MainWindow
        : null;
}
