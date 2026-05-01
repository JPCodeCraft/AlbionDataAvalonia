using System;
using System.ComponentModel;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionDataAvalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly CombatTrackerService? combatTracker;

    [ObservableProperty]
    private UserSettings userSettings;

    [ObservableProperty]
    private double powSolveTimeAverage;

    [ObservableProperty]
    private double powSolveTimeMedian;

    [ObservableProperty]
    private double powSolveTimePercentile95;

    [ObservableProperty]
    private double powSolveTimeMin;

    [ObservableProperty]
    private double powSolveTimeMax;

    [ObservableProperty]
    private double powSolveTimeLatest;

    [ObservableProperty]
    private int powSolveSampleCount;

    [ObservableProperty]
    private double powSolveTimeStandardDeviation;

    [ObservableProperty]
    private int combatRetainedEncounterCount;

    [ObservableProperty]
    private int combatRetentionLimit;

    [ObservableProperty]
    private int pendingCombatEncounterRetentionLimit = UserSettings.DefaultCombatEncounterRetentionLimit;

    [ObservableProperty]
    private int combatKnownEntityCount;

    [ObservableProperty]
    private int combatTimeBucketCount;

    [ObservableProperty]
    private int combatParticipantTotalCount;

    [ObservableProperty]
    private string estimatedCombatHistoryMemoryText = "~0 B";

    private readonly TimeSpan powSolveStatsRefreshInterval = TimeSpan.FromSeconds(3);
    private DateTimeOffset lastPowSolveStatsRefresh = DateTimeOffset.MinValue;
    private IDisposable? pendingPowSolveStatsRefreshRegistration;
    private readonly TimeSpan combatStatsRefreshInterval = TimeSpan.FromSeconds(3);
    private DateTimeOffset lastCombatStatsRefresh = DateTimeOffset.MinValue;
    private IDisposable? pendingCombatStatsRefreshRegistration;
    private readonly TimeSpan combatRetentionCommitDelay = TimeSpan.FromMilliseconds(750);
    private IDisposable? pendingCombatRetentionCommitRegistration;
    private bool applyingPendingCombatEncounterRetentionLimit;

    public SettingsViewModel()
    {
        UserSettings = new UserSettings();
        CombatRetentionLimit = UserSettings.CombatEncounterRetentionLimit;
        PendingCombatEncounterRetentionLimit = UserSettings.CombatEncounterRetentionLimit;
    }

    public SettingsViewModel(SettingsManager settingsManager, PlayerState playerState, CombatTrackerService combatTracker)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        this.combatTracker = combatTracker;

        UserSettings = _settingsManager.UserSettings;
        PendingCombatEncounterRetentionLimit = UserSettings.CombatEncounterRetentionLimit;
        UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;

        UpdatePowSolveStatistics();
        lastPowSolveStatsRefresh = DateTimeOffset.UtcNow;
        _playerState.OnPlayerStateChanged += (_, _) => MaybeUpdatePowSolveStatistics();

        UpdateCombatStatistics();
        lastCombatStatsRefresh = DateTimeOffset.UtcNow;
        this.combatTracker.SnapshotChanged += _ => MaybeUpdateCombatStatistics();
    }

    public int PowSolveWindowSize => _playerState?.PowSolveWindowSize ?? 0;

    [RelayCommand]
    private void ClearPowSolveStats()
    {
        if (_playerState == null)
        {
            return;
        }

        _playerState.ClearPowSolveStatistics();
        pendingPowSolveStatsRefreshRegistration?.Dispose();
        pendingPowSolveStatsRefreshRegistration = null;
        lastPowSolveStatsRefresh = DateTimeOffset.MinValue;
        UpdatePowSolveStatistics();
    }

    private void MaybeUpdatePowSolveStatistics()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(MaybeUpdatePowSolveStatistics);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = now - lastPowSolveStatsRefresh;
        if (elapsed < powSolveStatsRefreshInterval)
        {
            if (pendingPowSolveStatsRefreshRegistration == null)
            {
                var remaining = powSolveStatsRefreshInterval - elapsed;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }

                pendingPowSolveStatsRefreshRegistration = DispatcherTimer.RunOnce(() =>
                {
                    pendingPowSolveStatsRefreshRegistration = null;
                    lastPowSolveStatsRefresh = DateTimeOffset.UtcNow;
                    UpdatePowSolveStatistics();
                }, remaining);
            }

            return;
        }

        pendingPowSolveStatsRefreshRegistration?.Dispose();
        pendingPowSolveStatsRefreshRegistration = null;
        lastPowSolveStatsRefresh = now;
        UpdatePowSolveStatistics();
    }

    private void UpdatePowSolveStatistics()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdatePowSolveStatistics);
            return;
        }

        if (_playerState == null)
        {
            PowSolveSampleCount = 0;
            PowSolveTimeAverage = 0;
            PowSolveTimeMedian = 0;
            PowSolveTimePercentile95 = 0;
            PowSolveTimeMin = 0;
            PowSolveTimeMax = 0;
            PowSolveTimeLatest = 0;
            PowSolveTimeStandardDeviation = 0;
            return;
        }

        PowSolveSampleCount = _playerState.PowSolveSampleCount;
        PowSolveTimeAverage = _playerState.PowSolveTimeAverage;
        PowSolveTimeMedian = _playerState.PowSolveTimeMedian;
        PowSolveTimePercentile95 = _playerState.PowSolveTimePercentile95;
        PowSolveTimeMin = _playerState.PowSolveTimeMin;
        PowSolveTimeMax = _playerState.PowSolveTimeMax;
        PowSolveTimeLatest = _playerState.PowSolveTimeLatest;
        PowSolveTimeStandardDeviation = _playerState.PowSolveTimeStandardDeviation;
    }

    partial void OnPendingCombatEncounterRetentionLimitChanged(int value)
    {
        if (applyingPendingCombatEncounterRetentionLimit)
        {
            return;
        }

        var clampedValue = ClampCombatEncounterRetentionLimit(value);
        if (clampedValue != value)
        {
            SyncPendingCombatEncounterRetentionLimit(clampedValue);
            return;
        }

        var currentLimit = UserSettings.CombatEncounterRetentionLimit;
        if (clampedValue >= currentLimit)
        {
            CommitCombatEncounterRetentionLimit(clampedValue);
            return;
        }

        DebounceCombatEncounterRetentionLimitCommit(clampedValue);
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.CombatEncounterRetentionLimit))
        {
            return;
        }

        pendingCombatRetentionCommitRegistration?.Dispose();
        pendingCombatRetentionCommitRegistration = null;
        SyncPendingCombatEncounterRetentionLimit(UserSettings.CombatEncounterRetentionLimit);
        UpdateCombatStatistics();
    }

    private void DebounceCombatEncounterRetentionLimitCommit(int value)
    {
        pendingCombatRetentionCommitRegistration?.Dispose();
        pendingCombatRetentionCommitRegistration = DispatcherTimer.RunOnce(() =>
        {
            pendingCombatRetentionCommitRegistration = null;
            CommitCombatEncounterRetentionLimit(value);
        }, combatRetentionCommitDelay);
    }

    private void CommitCombatEncounterRetentionLimit(int value)
    {
        pendingCombatRetentionCommitRegistration?.Dispose();
        pendingCombatRetentionCommitRegistration = null;
        UserSettings.CombatEncounterRetentionLimit = ClampCombatEncounterRetentionLimit(value);
    }

    private void SyncPendingCombatEncounterRetentionLimit(int value)
    {
        applyingPendingCombatEncounterRetentionLimit = true;
        try
        {
            PendingCombatEncounterRetentionLimit = ClampCombatEncounterRetentionLimit(value);
        }
        finally
        {
            applyingPendingCombatEncounterRetentionLimit = false;
        }
    }

    private static int ClampCombatEncounterRetentionLimit(int value)
    {
        return Math.Clamp(
            value,
            UserSettings.MinCombatEncounterRetentionLimit,
            UserSettings.MaxCombatEncounterRetentionLimit);
    }

    private void MaybeUpdateCombatStatistics()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(MaybeUpdateCombatStatistics);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = now - lastCombatStatsRefresh;
        if (elapsed < combatStatsRefreshInterval)
        {
            if (pendingCombatStatsRefreshRegistration == null)
            {
                var remaining = combatStatsRefreshInterval - elapsed;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }

                pendingCombatStatsRefreshRegistration = DispatcherTimer.RunOnce(() =>
                {
                    pendingCombatStatsRefreshRegistration = null;
                    lastCombatStatsRefresh = DateTimeOffset.UtcNow;
                    UpdateCombatStatistics();
                }, remaining);
            }

            return;
        }

        pendingCombatStatsRefreshRegistration?.Dispose();
        pendingCombatStatsRefreshRegistration = null;
        lastCombatStatsRefresh = now;
        UpdateCombatStatistics();
    }

    private void UpdateCombatStatistics()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateCombatStatistics);
            return;
        }

        if (combatTracker == null)
        {
            CombatRetainedEncounterCount = 0;
            CombatRetentionLimit = UserSettings.CombatEncounterRetentionLimit;
            CombatKnownEntityCount = 0;
            CombatTimeBucketCount = 0;
            CombatParticipantTotalCount = 0;
            EstimatedCombatHistoryMemoryText = "~0 B";
            return;
        }

        var statistics = combatTracker.GetStatistics();
        CombatRetainedEncounterCount = statistics.RetainedEncounterCount;
        CombatRetentionLimit = statistics.RetentionLimit;
        CombatKnownEntityCount = statistics.KnownEntityCount;
        CombatTimeBucketCount = statistics.TimeBucketCount;
        CombatParticipantTotalCount = statistics.ParticipantTotalCount;
        EstimatedCombatHistoryMemoryText = FormatEstimatedBytes(statistics.EstimatedHistoryBytes);
    }

    private static string FormatEstimatedBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        var unitIndex = 0;
        var displayValue = (double)Math.Max(bytes, 0);
        while (displayValue >= 1024 && unitIndex < units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"~{displayValue:N0} {units[unitIndex]}"
            : $"~{displayValue:N1} {units[unitIndex]}";
    }

}
