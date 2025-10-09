using System;
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

    private readonly TimeSpan powSolveStatsRefreshInterval = TimeSpan.FromSeconds(3);
    private DateTimeOffset lastPowSolveStatsRefresh = DateTimeOffset.MinValue;
    private IDisposable? pendingPowSolveStatsRefreshRegistration;

    public SettingsViewModel()
    {
    }

    public SettingsViewModel(SettingsManager settingsManager, PlayerState playerState)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;

        userSettings = _settingsManager.UserSettings;

        UpdatePowSolveStatistics();
        lastPowSolveStatsRefresh = DateTimeOffset.UtcNow;
        _playerState.OnPlayerStateChanged += (_, _) => MaybeUpdatePowSolveStatistics();
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

}
