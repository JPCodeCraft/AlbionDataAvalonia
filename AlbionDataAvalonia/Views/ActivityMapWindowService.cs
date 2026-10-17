using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Maps;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Threading;
using System;

namespace AlbionDataAvalonia.Views;

/// <summary>Owns the single map window. Its view model reads the tracker only while the window is open.</summary>
public sealed class ActivityMapWindowService(ActivityTrackerService tracker, ActivityPersistence persistence, MapDataService maps, SettingsManager settings)
{
    private ActivityMapWindow? window;
    private ActivityMapViewModel? viewModel;

    public bool IsOpen => window is not null;
    public event EventHandler? IsOpenChanged;

    public void RestoreIfOpen()
    {
        if (settings.UserSettings.MapWindowOpen) Show();
    }

    public void Toggle()
    {
        if (window is null) Show();
        else window.Close();
    }

    public void ShowRun(Guid runId)
    {
        Show();
        viewModel?.ShowRun(runId);
    }

    public void Show()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (window is not null)
        {
            if (window.WindowState == Avalonia.Controls.WindowState.Minimized) window.WindowState = Avalonia.Controls.WindowState.Normal;
            window.Activate();
            return;
        }
        if (App.IsShuttingDown) return;
        var opened = new ActivityMapWindow();
        var model = new ActivityMapViewModel(tracker, persistence, maps, settings.UserSettings);
        opened.DataContext = model;
        var placement = new WindowPlacementTracker(opened, settings.UserSettings.MapWindowPlacement, restoreSize: true);
        opened.Closing += (_, _) =>
        {
            if (placement.Capture() is { } spot) settings.UserSettings.MapWindowPlacement = spot;
        };
        opened.Closed += (_, _) =>
        {
            model.Dispose();
            if (!App.IsShuttingDown) settings.UserSettings.MapWindowOpen = false;
            if (window == opened) { window = null; viewModel = null; }
            IsOpenChanged?.Invoke(this, EventArgs.Empty);
        };
        window = opened;
        viewModel = model;
        settings.UserSettings.MapWindowOpen = true;
        opened.Show();
        model.Start();
        IsOpenChanged?.Invoke(this, EventArgs.Empty);
    }
}
