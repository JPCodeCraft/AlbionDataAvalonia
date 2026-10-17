using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Maps;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.ViewModels;
using System;

namespace AlbionDataAvalonia.Views;

/// <summary>Owns the single map window. Its view model reads the tracker only while the window is open.</summary>
public sealed class ActivityMapWindowService
{
    private readonly SingleWindowHost<ActivityMapWindow, ActivityMapViewModel> host;

    public ActivityMapWindowService(ActivityTrackerService tracker, ActivityPersistence persistence, MapDataService maps, SettingsManager settings)
    {
        var user = settings.UserSettings;
        host = new(() => new ActivityMapViewModel(tracker, persistence, maps, user), model => model.Start(), new WindowMemory(
            () => user.MapWindowOpen, open => user.MapWindowOpen = open,
            () => user.MapWindowPlacement, placement => user.MapWindowPlacement = placement), restoreSize: true);
    }

    public bool IsOpen => host.IsOpen;
    public string ButtonText => IsOpen ? "Close map" : "Open map";

    public event EventHandler? IsOpenChanged
    {
        add => host.IsOpenChanged += value;
        remove => host.IsOpenChanged -= value;
    }

    public void RestoreIfOpen() => host.RestoreIfOpen();

    public void Toggle() => host.Toggle();

    public void Show() => host.Show();

    public void ShowRun(Guid runId)
    {
        host.Show();
        host.ViewModel?.ShowRun(runId);
    }
}
