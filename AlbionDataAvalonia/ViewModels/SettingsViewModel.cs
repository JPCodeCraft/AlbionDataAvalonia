using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.Ui.Services;
using AlbionDataAvalonia.Views;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionDataAvalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public const int UploadsTabIndex = 1;
    public const int StreamerTabIndex = 3;

    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly ActivityTrackerService? activityTracker;
    private readonly AuthService? authService;
    private readonly DatabaseBackupService? databaseBackupService;
    private readonly ActivityStreamerWindowService? streamerWindow;
    private readonly PacketDiagnostics? packetDiagnostics;
    private readonly IDialogService? dialogs;

    [ObservableProperty]
    private UserSettings userSettings;

    [ObservableProperty]
    private int selectedTabIndex;

    // A live copy of the streamer window, read only while the Streamer tab is on screen.
    [ObservableProperty]
    private ActivityStreamerViewModel? streamerPreview;

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
    private string activityRecordingStatus = "Saved activity remains available until explicitly deleted.";
    [ObservableProperty]
    private string currentFirebaseUserId = string.Empty;

    [ObservableProperty]
    private bool isUserLoggedIn;

    private readonly TimeSpan powSolveStatsRefreshInterval = TimeSpan.FromSeconds(3);
    private DateTimeOffset lastPowSolveStatsRefresh = DateTimeOffset.MinValue;
    private IDisposable? pendingPowSolveStatsRefreshRegistration;

    public bool CanOpenStreamerWindow => streamerWindow is not null;
    public string StreamerWindowButtonText => streamerWindow?.ButtonText ?? "";

    [RelayCommand] private void ToggleStreamerWindow() => streamerWindow?.Toggle();

    partial void OnSelectedTabIndexChanged(int value)
    {
        UpdateStreamerPreview(value == StreamerTabIndex);
        if (value == DataTabIndex) _ = RefreshStorageAsync();
    }

    public void UpdateStreamerPreview(bool visible)
    {
        if (visible && StreamerPreview is null && streamerWindow is not null)
        {
            var preview = streamerWindow.CreateViewModel();
            StreamerPreview = preview;
            preview.Start();
        }
        else if (!visible && StreamerPreview is { } preview)
        {
            StreamerPreview = null;
            preview.Dispose();
        }
    }

    public SettingsViewModel()
    {
        UserSettings = new UserSettings();
    }

    public SettingsViewModel(
        SettingsManager settingsManager,
        PlayerState playerState,
        ActivityTrackerService activityTracker,
        AuthService authService,
        AFMUploader afmUploader,
        DatabaseBackupService databaseBackupService,
        ActivityStreamerWindowService? streamerWindow = null,
        DataCleanupService? dataCleanup = null,
        PacketDiagnostics? packetDiagnostics = null,
        IDialogService? dialogs = null,
        IClipboardService? clipboard = null)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        this.activityTracker = activityTracker;
        this.authService = authService;
        Sharing = new PrivateSharingViewModel(afmUploader, clipboard);
        this.databaseBackupService = databaseBackupService;
        this.streamerWindow = streamerWindow;
        this.dataCleanup = dataCleanup;
        this.packetDiagnostics = packetDiagnostics;
        this.dialogs = dialogs;
        if (packetDiagnostics is not null)
            packetDiagnostics.IsEnabledChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(DiagnosticsEnabled));
                OnPropertyChanged(nameof(DiagnosticsFilePath));
            });
        InitializeCleanup();
        if (streamerWindow is not null)
            streamerWindow.IsOpenChanged += (_, _) => OnPropertyChanged(nameof(StreamerWindowButtonText));

        UserSettings = _settingsManager.UserSettings;
        UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;

        UpdatePowSolveStatistics();
        lastPowSolveStatsRefresh = DateTimeOffset.UtcNow;
        _playerState.OnPlayerStateChanged += (_, _) => MaybeUpdatePowSolveStatistics();

        UpdateActivityRecordingStatus();
        this.activityTracker.Changed += (_, _) => UpdateActivityRecordingStatus();

        this.authService.FirebaseUserChanged += OnFirebaseUserChanged;
        ApplyFirebaseUser(this.authService.CurrentFirebaseUser);
    }

    public int PowSolveWindowSize => _playerState?.PowSolveWindowSize ?? 0;
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public string BackupFolderPath => AppData.BackupDirectoryPath;
    public string DataFolderPath => AppData.DataDirectoryPath;

    public bool CloseButtonHidesToTray
    {
        get => !UserSettings.ShutDownOnClose;
        set
        {
            if (UserSettings.ShutDownOnClose == value)
            {
                UserSettings.ShutDownOnClose = !value;
                OnPropertyChanged(nameof(CloseButtonHidesToTray));
            }
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        databaseBackupService?.OpenBackupFolder();
    }

    public IReadOnlyList<Option<int>> StartupWindowModeOptions { get; } = [new(0, "Show window"), new(1, "Minimize to taskbar"), new(2, "Hide in tray")];

    public int StartupWindowModeIndex
    {
        get => (int)UserSettings.StartupWindowMode;
        set => UserSettings.StartupWindowMode = (StartupWindowMode)Math.Clamp(value, 0, StartupWindowModeOptions.Count - 1);
    }

    public IReadOnlyList<Option<int>> UpdateChannelOptions { get; } = [new(0, "Stable"), new(1, "Beta")];

    public int UpdateChannelIndex
    {
        get => UserSettings.JoinBetaProgram ? 1 : 0;
        set => UserSettings.JoinBetaProgram = value == 1;
    }

    public string UpdateChannelText => UserSettings.JoinBetaProgram
        ? "Beta builds arrive first and can have bugs."
        : "Stable builds only.";

    public string AppVersionText => $"AFM Data Client {ClientUpdater.GetVersion() ?? "Unknown"}";
    public string UpdatesText => IsWindows ? "Updates install by themselves." : "You'll see a notice when an update is ready.";

    public IReadOnlyList<Option<int>> StreamerSizeOptions { get; } = [new(0, "Small"), new(1, "Medium"), new(2, "Large")];
    public IReadOnlyList<Option<int>> StreamerChartModeOptions { get; } = [new(0, "Combat"), new(1, "Gathering"), new(2, "Fame")];

    public string StreamerSizeText => UserSettings.StreamerSizeIndex switch
    {
        0 => "Main numbers only, without the chart.",
        2 => "Adds breakdowns and the session name.",
        _ => "Numbers and chart."
    };

    public string StreamerChartText => UserSettings.StreamerSizeIndex == 0 ? "Hidden at Small size." : "";

    public bool TrackAwakenedItems
    {
        get => !UserSettings.DisableAwakeningItemsTracker;
        set => UserSettings.DisableAwakeningItemsTracker = !value;
    }

    public bool IsPublicMode
    {
        get => !UserSettings.UploadToAfmOnly;
        set
        {
            if (value) UserSettings.UploadToAfmOnly = false;
        }
    }

    public bool IsPrivateMode
    {
        get => UserSettings.UploadToAfmOnly;
        set
        {
            if (value) UserSettings.UploadToAfmOnly = true;
        }
    }

    public bool CanUsePrivateSharing => IsUserLoggedIn && UserSettings.UploadToAfmOnly;

    /// <summary>The friends who see your private market orders.</summary>
    public PrivateSharingViewModel Sharing { get; } = new();

    [RelayCommand]
    private void OpenDataFolder() => SystemLauncher.OpenFolder(DataFolderPath);

    public bool DiagnosticsEnabled
    {
        get => packetDiagnostics?.IsEnabled == true;
        set => packetDiagnostics?.SetEnabled(value);
    }

    public string DiagnosticsFilePath => packetDiagnostics?.CurrentFilePath ?? string.Empty;

    [RelayCommand]
    private void OpenLogsFolder() => SystemLauncher.OpenFolder(PacketDiagnostics.LogsDirectoryPath);

    [RelayCommand]
    private void OpenAfmWebsite() => SystemLauncher.TryOpenUrl(SystemLauncher.AfmWebsiteUrl);

    [RelayCommand]
    private void OpenAodpWebsite() => SystemLauncher.TryOpenUrl(SystemLauncher.AodpWebsiteUrl);

    [RelayCommand]
    private void OpenDiscord() => SystemLauncher.TryOpenUrl(SystemLauncher.AfmDiscordUrl);

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
        if (UiDispatch.Repost(MaybeUpdatePowSolveStatistics)) return;

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
        if (UiDispatch.Repost(UpdatePowSolveStatistics)) return;

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

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.ShutDownOnClose))
        {
            OnPropertyChanged(nameof(CloseButtonHidesToTray));
            return;
        }

        if (e.PropertyName == nameof(UserSettings.StartupWindowMode))
        {
            OnPropertyChanged(nameof(StartupWindowModeIndex));
            return;
        }

        if (e.PropertyName == nameof(UserSettings.JoinBetaProgram))
        {
            OnPropertyChanged(nameof(UpdateChannelIndex));
            OnPropertyChanged(nameof(UpdateChannelText));
            return;
        }

        if (e.PropertyName == nameof(UserSettings.StreamerSizeIndex))
        {
            OnPropertyChanged(nameof(StreamerSizeText));
            OnPropertyChanged(nameof(StreamerChartText));
            return;
        }

        if (e.PropertyName == nameof(UserSettings.DisableAwakeningItemsTracker))
        {
            OnPropertyChanged(nameof(TrackAwakenedItems));
            return;
        }

        if (e.PropertyName == nameof(UserSettings.UploadToAfmOnly))
        {
            OnPropertyChanged(nameof(IsPublicMode));
            OnPropertyChanged(nameof(IsPrivateMode));
            OnPropertyChanged(nameof(CanUsePrivateSharing));
            return;
        }

        if (e.PropertyName == nameof(UserSettings.ActivityRecordingEnabled))
        {
            UpdateActivityRecordingStatus();
        }
    }

    partial void OnIsUserLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUsePrivateSharing));
    }

    private void OnFirebaseUserChanged(FirebaseAuthResponse? user)
    {
        if (UiDispatch.Repost(() => OnFirebaseUserChanged(user))) return;

        ApplyFirebaseUser(user);
    }

    private void ApplyFirebaseUser(FirebaseAuthResponse? user)
    {
        var userId = user?.LocalId ?? string.Empty;
        if (user is not null
            && IsUserLoggedIn
            && string.Equals(CurrentFirebaseUserId, userId, StringComparison.Ordinal))
        {
            return;
        }

        IsUserLoggedIn = user is not null;
        CurrentFirebaseUserId = userId;
        Sharing.SetUser(userId);
    }

    private void UpdateActivityRecordingStatus()
    {
        if (UiDispatch.Repost(UpdateActivityRecordingStatus)) return;
        ActivityRecordingStatus = activityTracker?.StatusText ??
            (UserSettings.ActivityRecordingEnabled ? "Recording enabled." : "Recording disabled. Saved history remains available.");
    }
}
