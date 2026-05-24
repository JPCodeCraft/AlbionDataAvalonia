using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Settings;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace AlbionDataAvalonia.ViewModels;

public partial class GatheringViewModel : ViewModelBase, IDisposable
{
    private readonly GatheringTrackerService? gatheringTracker;
    private readonly SettingsManager? settingsManager;
    private DispatcherTimer? elapsedTimer;

    [ObservableProperty]
    private string totalSessionValueText = "0";

    [ObservableProperty]
    private string silverPerHourText = "0";

    [ObservableProperty]
    private string totalAmountText = "0";

    [ObservableProperty]
    private string elapsedText = "00:00";

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool isGatheringTrackerDisabled;

    public bool IsGatheringTrackerEnabled => !IsGatheringTrackerDisabled;

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    public ObservableCollection<GatheringSummaryRowViewModel> SummaryRows { get; } = new();
    public ObservableCollection<GatheringBucketRowViewModel> BucketRows { get; } = new();

    public GatheringViewModel()
    {
    }

    public GatheringViewModel(GatheringTrackerService gatheringTracker, SettingsManager settingsManager)
    {
        this.gatheringTracker = gatheringTracker;
        this.settingsManager = settingsManager;
        isGatheringTrackerDisabled = settingsManager.UserSettings.DisableGatheringTracker;
        ApplySnapshot(gatheringTracker.CurrentSnapshot);
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        gatheringTracker.SnapshotChanged += OnSnapshotChanged;

        elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        elapsedTimer.Tick += OnElapsedTimerTick;
        elapsedTimer.Start();
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseButtonText));
    }

    partial void OnIsGatheringTrackerDisabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGatheringTrackerEnabled));
    }

    [RelayCommand]
    private void ClearSession()
    {
        gatheringTracker?.Reset();
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (gatheringTracker is null)
        {
            IsPaused = !IsPaused;
            return;
        }

        gatheringTracker.SetPaused(!IsPaused);
    }

    private void OnSnapshotChanged(GatheringTrackerSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableGatheringTracker))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (settingsManager is not null)
            {
                IsGatheringTrackerDisabled = settingsManager.UserSettings.DisableGatheringTracker;
            }
        });
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        if (gatheringTracker is null)
        {
            return;
        }

        ApplyHeader(gatheringTracker.CurrentSnapshot);
    }

    private void ApplySnapshot(GatheringTrackerSnapshot snapshot)
    {
        ApplyHeader(snapshot);
        SyncSummaryRows(snapshot);
        SyncBucketRows(snapshot);
    }

    private void ApplyHeader(GatheringTrackerSnapshot snapshot)
    {
        IsPaused = snapshot.IsPaused;
        TotalSessionValueText = FormatLong(snapshot.TotalEstimatedMarketValue);
        SilverPerHourText = FormatLong(CalculateSilverPerHour(snapshot));
        TotalAmountText = FormatLong(snapshot.TotalAmount);
        ElapsedText = FormatElapsed(snapshot.ActiveElapsed);
    }

    private void SyncSummaryRows(GatheringTrackerSnapshot snapshot)
    {
        SummaryRows.Clear();
        foreach (var row in snapshot.SummaryRows)
        {
            SummaryRows.Add(new GatheringSummaryRowViewModel(row));
        }
    }

    private void SyncBucketRows(GatheringTrackerSnapshot snapshot)
    {
        BucketRows.Clear();
        foreach (var row in snapshot.BucketRows)
        {
            BucketRows.Add(new GatheringBucketRowViewModel(row));
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private static string FormatLong(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static long CalculateSilverPerHour(GatheringTrackerSnapshot snapshot)
    {
        if (snapshot.TotalEstimatedMarketValue <= 0 || snapshot.ActiveElapsed.TotalSeconds <= 0)
        {
            return 0;
        }

        return (long)Math.Round(snapshot.TotalEstimatedMarketValue / snapshot.ActiveElapsed.TotalHours);
    }

    public void Dispose()
    {
        if (gatheringTracker is not null)
        {
            gatheringTracker.SnapshotChanged -= OnSnapshotChanged;
        }

        if (settingsManager is not null)
        {
            settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        }

        if (elapsedTimer is not null)
        {
            elapsedTimer.Stop();
            elapsedTimer.Tick -= OnElapsedTimerTick;
            elapsedTimer = null;
        }
    }
}

public sealed class GatheringSummaryRowViewModel
{
    public GatheringSummaryRowViewModel(GatheringSummaryRow row)
    {
        ItemName = row.ItemName;
        Amount = row.Amount;
        EstimatedMarketValue = row.EstimatedMarketValue;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        AmountPerHour = row.AmountPerHour;
    }

    public string ItemName { get; }
    public long Amount { get; }
    public long? EstimatedMarketValue { get; }
    public long? TotalEstimatedMarketValue { get; }
    public double AmountPerHour { get; }
    public string EstimatedMarketValueText => EstimatedMarketValue is null ? "-" : EstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue is null ? "-" : TotalEstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
}

public sealed class GatheringBucketRowViewModel
{
    public GatheringBucketRowViewModel(GatheringBucketRow row)
    {
        BucketStartedAtUtc = row.BucketStartedAtUtc;
        Amount = row.Amount;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        SilverPerHour = row.SilverPerHour;
    }

    public DateTime BucketStartedAtUtc { get; }
    public string BucketText => BucketStartedAtUtc.ToString("HH:mm", CultureInfo.CurrentCulture);
    public long Amount { get; }
    public long? TotalEstimatedMarketValue { get; }
    public long? SilverPerHour { get; }
    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue is null ? "-" : TotalEstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string SilverPerHourText => SilverPerHour is null ? "-" : SilverPerHour.Value.ToString("N0", CultureInfo.CurrentCulture);
}
