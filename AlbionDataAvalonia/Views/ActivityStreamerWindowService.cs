using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Threading;
using System;

namespace AlbionDataAvalonia.Views;

/// <summary>Owns the single streamer window. Its view model reads activity only while the window is open.</summary>
public sealed class ActivityStreamerWindowService(ActivityTrackerService tracker, ActivityPersistence persistence, SettingsManager settings)
{
    private ActivityStreamerWindow? window;

    public bool IsOpen => window is not null;
    public event EventHandler? IsOpenChanged;

    public ActivityStreamerViewModel CreateViewModel() => new(tracker, persistence, settings.UserSettings);

    public void RestoreIfOpen()
    {
        if (settings.UserSettings.StreamerWindowOpen) Show();
    }

    public void Toggle()
    {
        if (window is null) Show();
        else window.Close();
    }

    public void Show()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (window is not null)
        {
            window.Activate();
            return;
        }
        if (App.IsShuttingDown) return;
        var viewModel = CreateViewModel();
        var opened = new ActivityStreamerWindow { DataContext = viewModel };
        // Its width follows the size setting and its height the sections shown, so only the spot is restored.
        var placement = new WindowPlacementTracker(opened, settings.UserSettings.StreamerWindowPlacement, restoreSize: false);
        opened.Closing += (_, _) =>
        {
            if (placement.Capture() is { } spot) settings.UserSettings.StreamerWindowPlacement = spot;
        };
        opened.Closed += (_, _) =>
        {
            viewModel.Dispose();
            if (!App.IsShuttingDown) settings.UserSettings.StreamerWindowOpen = false;
            if (window == opened) window = null;
            IsOpenChanged?.Invoke(this, EventArgs.Empty);
        };
        window = opened;
        settings.UserSettings.StreamerWindowOpen = true;
        opened.Show();
        viewModel.Start();
        IsOpenChanged?.Invoke(this, EventArgs.Empty);
    }
}
