using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.ViewModels;
using System;

namespace AlbionDataAvalonia.Views;

/// <summary>Owns the single streamer window. Its view model reads activity only while the window is open.</summary>
public sealed class ActivityStreamerWindowService
{
    private readonly ActivityTrackerService tracker;
    private readonly ActivityPersistence persistence;
    private readonly SettingsManager settings;
    private readonly SingleWindowHost<ActivityStreamerWindow, ActivityStreamerViewModel> host;

    public ActivityStreamerWindowService(ActivityTrackerService tracker, ActivityPersistence persistence, SettingsManager settings)
    {
        this.tracker = tracker;
        this.persistence = persistence;
        this.settings = settings;
        var user = settings.UserSettings;
        // Its width follows the size setting and its height the sections shown, so only the spot is restored.
        host = new(CreateViewModel, model => model.Start(), new WindowMemory(
            () => user.StreamerWindowOpen, open => user.StreamerWindowOpen = open,
            () => user.StreamerWindowPlacement, placement => user.StreamerWindowPlacement = placement), restoreSize: false);
    }

    public bool IsOpen => host.IsOpen;
    public string ButtonText => IsOpen ? "Close streamer window" : "Open streamer window";

    public event EventHandler? IsOpenChanged
    {
        add => host.IsOpenChanged += value;
        remove => host.IsOpenChanged -= value;
    }

    public ActivityStreamerViewModel CreateViewModel() => new(tracker, persistence, settings.UserSettings);

    public void RestoreIfOpen() => host.RestoreIfOpen();

    public void Toggle() => host.Toggle();

    public void Show() => host.Show();
}
