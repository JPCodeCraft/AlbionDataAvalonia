using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.Views;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionDataAvalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private const int MaxSharedFriendsLimit = 5;
    public const int UploadsTabIndex = 1;
    public const int StreamerTabIndex = 3;

    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly ActivityTrackerService? activityTracker;
    private readonly AuthService? authService;
    private readonly AFMUploader? afmUploader;
    private readonly DatabaseBackupService? databaseBackupService;
    private readonly ActivityStreamerWindowService? streamerWindow;

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

    [ObservableProperty]
    private string friendInput = string.Empty;

    [ObservableProperty]
    private string sharingStatus = string.Empty;

    [ObservableProperty]
    private bool hasSharingStatus;

    [ObservableProperty]
    private bool isSharingBusy;

    [ObservableProperty]
    private string unresolvedEntriesText = string.Empty;

    [ObservableProperty]
    private bool hasUnresolvedEntries;

    private readonly TimeSpan powSolveStatsRefreshInterval = TimeSpan.FromSeconds(3);
    private DateTimeOffset lastPowSolveStatsRefresh = DateTimeOffset.MinValue;
    private IDisposable? pendingPowSolveStatsRefreshRegistration;

    public ObservableCollection<PrivateOrderShareEntryViewModel> SharedUsers { get; } = new();

    public bool CanOpenStreamerWindow => streamerWindow is not null;
    public string StreamerWindowButtonText => streamerWindow?.IsOpen == true ? "Close streamer window" : "Open streamer window";

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
        DataCleanupService? dataCleanup = null)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        this.activityTracker = activityTracker;
        this.authService = authService;
        this.afmUploader = afmUploader;
        this.databaseBackupService = databaseBackupService;
        this.streamerWindow = streamerWindow;
        this.dataCleanup = dataCleanup;
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
    public int MaxSharedFriends => MaxSharedFriendsLimit;
    public string SharedFriendsLimitText => $"{SharedUsers.Count} / {MaxSharedFriendsLimit} friends";
    public string RedactedCurrentFirebaseUserId => RedactUserId(CurrentFirebaseUserId);
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool CanUseSharingSettings => IsUserLoggedIn && !IsSharingBusy;
    public bool CanAddSharedUser => CanUseSharingSettings && SharedUsers.Count < MaxSharedFriendsLimit;
    public bool HasNormalSharingStatus => HasSharingStatus && !HasUnresolvedEntries;
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

    public IReadOnlyList<ActivityOption<int>> StartupWindowModeOptions { get; } = [new(0, "Show window"), new(1, "Minimize to taskbar"), new(2, "Hide in tray")];

    public int StartupWindowModeIndex
    {
        get => (int)UserSettings.StartupWindowMode;
        set => UserSettings.StartupWindowMode = (StartupWindowMode)Math.Clamp(value, 0, StartupWindowModeOptions.Count - 1);
    }

    public IReadOnlyList<ActivityOption<int>> UpdateChannelOptions { get; } = [new(0, "Stable"), new(1, "Beta")];

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

    public IReadOnlyList<ActivityOption<int>> StreamerSizeOptions { get; } = [new(0, "Small"), new(1, "Medium"), new(2, "Large")];
    public IReadOnlyList<ActivityOption<int>> StreamerChartModeOptions { get; } = [new(0, "Combat"), new(1, "Gathering"), new(2, "Fame")];

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

    [RelayCommand]
    private void OpenDataFolder() => SystemLauncher.OpenFolder(DataFolderPath);

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

    [RelayCommand]
    private async Task AddSharedUser()
    {
        var value = FriendInput.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (SharedUsers.Count >= MaxSharedFriendsLimit)
        {
            SetSharingStatus($"You can share with up to {MaxSharedFriendsLimit} friends.");
            return;
        }

        if (SharedUsers.Any(x => string.Equals(GetInputDedupeKey(x.Value), GetInputDedupeKey(value), StringComparison.Ordinal)))
        {
            FriendInput = string.Empty;
            return;
        }

        await SavePrivateOrderSharesAsync(
            SharedUsers.Select(x => x.Value).Append(value),
            successStatus: "Sharing list updated.");
    }

    [RelayCommand]
    private async Task RemoveSharedUser(PrivateOrderShareEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        await SavePrivateOrderSharesAsync(
            SharedUsers.Where(x => !ReferenceEquals(x, entry)).Select(x => x.Value),
            successStatus: "Sharing list updated.");
    }

    [RelayCommand]
    private async Task LoadPrivateOrderShares()
    {
        await LoadPrivateOrderSharesAsync(showStatus: true);
    }

    private async Task SavePrivateOrderSharesAsync(IEnumerable<string> sharedUsers, string successStatus)
    {
        if (!IsUserLoggedIn || afmUploader is null)
        {
            SetSharingStatus("Sign in to manage private sharing.");
            return;
        }

        var expectedUserId = CurrentFirebaseUserId;
        await RunSharingOperationAsync(async () =>
        {
            UnresolvedEntriesText = string.Empty;
            SetSharingStatus("Saving sharing list...");
            var response = await afmUploader.SavePrivateOrderSharesAsync(sharedUsers);
            if (!IsCurrentSharingUser(expectedUserId))
            {
                return;
            }

            if (response is null)
            {
                SetSharingStatus("Could not save the friends list. Try again in a moment.");
                return;
            }

            ApplySharedUsers(response.SharedUsers);
            ApplyUnresolvedEntries(response.UnresolvedEntries);
            FriendInput = string.Empty;
            SetSharingStatus(response.UnresolvedEntries.Count == 0
                ? successStatus
                : FormatUnableToAddMessage(response.UnresolvedEntries));
        });
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
        OnPropertyChanged(nameof(CanUseSharingSettings));
        OnPropertyChanged(nameof(CanAddSharedUser));
    }

    partial void OnCurrentFirebaseUserIdChanged(string value)
    {
        OnPropertyChanged(nameof(RedactedCurrentFirebaseUserId));
    }

    partial void OnIsSharingBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUseSharingSettings));
        OnPropertyChanged(nameof(CanAddSharedUser));
    }

    partial void OnSharingStatusChanged(string value)
    {
        HasSharingStatus = !string.IsNullOrWhiteSpace(value);
        OnPropertyChanged(nameof(HasNormalSharingStatus));
    }

    partial void OnUnresolvedEntriesTextChanged(string value)
    {
        HasUnresolvedEntries = !string.IsNullOrWhiteSpace(value);
        OnPropertyChanged(nameof(HasNormalSharingStatus));
    }

    private void OnFirebaseUserChanged(FirebaseAuthResponse? user)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnFirebaseUserChanged(user));
            return;
        }

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

        if (user is null)
        {
            SharedUsers.Clear();
            RefreshSharedFriendsLimit();
            FriendInput = string.Empty;
            UnresolvedEntriesText = string.Empty;
            SetSharingStatus("Sign in to manage private sharing.");
            return;
        }

        _ = LoadPrivateOrderSharesAsync(showStatus: false);
    }

    private async Task LoadPrivateOrderSharesAsync(bool showStatus)
    {
        if (!IsUserLoggedIn || afmUploader is null)
        {
            if (showStatus)
            {
                SetSharingStatus("Sign in to manage private sharing.");
            }

            return;
        }

        var expectedUserId = CurrentFirebaseUserId;
        await RunSharingOperationAsync(async () =>
        {
            var response = await afmUploader.GetPrivateOrderSharesAsync();
            if (!IsCurrentSharingUser(expectedUserId))
            {
                return;
            }

            if (response is null)
            {
                if (showStatus)
                {
                    SetSharingStatus("Failed to load private sharing settings.");
                }

                return;
            }

            ApplySharedUsers(response.SharedUsers);
            UnresolvedEntriesText = string.Empty;

            if (showStatus)
            {
                SetSharingStatus("Private sharing settings loaded.");
            }
        });
    }

    private bool IsCurrentSharingUser(string expectedUserId)
    {
        return IsUserLoggedIn
            && !string.IsNullOrWhiteSpace(expectedUserId)
            && string.Equals(CurrentFirebaseUserId, expectedUserId, StringComparison.Ordinal);
    }

    private async Task RunSharingOperationAsync(Func<Task> operation)
    {
        IsSharingBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            IsSharingBusy = false;
        }
    }

    private void ApplySharedUsers(IEnumerable<PrivateOrderShareEntry> entries)
    {
        SharedUsers.Clear();
        foreach (var entry in entries)
        {
            SharedUsers.Add(new PrivateOrderShareEntryViewModel(entry.Value, entry.Type, entry.Resolved));
        }

        RefreshSharedFriendsLimit();
    }

    private void RefreshSharedFriendsLimit()
    {
        OnPropertyChanged(nameof(SharedFriendsLimitText));
        OnPropertyChanged(nameof(CanAddSharedUser));
    }

    private void ApplyUnresolvedEntries(IReadOnlyCollection<string> unresolvedEntries)
    {
        UnresolvedEntriesText = unresolvedEntries.Count == 0
            ? string.Empty
            : string.Join(", ", unresolvedEntries);
    }

    private static string FormatUnableToAddMessage(IReadOnlyCollection<string> unresolvedEntries)
    {
        return unresolvedEntries.Count == 1
            ? $"Unable to add {unresolvedEntries.First()}. Check the user ID, email or AFM username. If the entry is ambiguous, use the email instead."
            : $"Unable to add {string.Join(", ", unresolvedEntries)}. Check the user IDs, emails or AFM usernames. For ambiguous entries, use email instead.";
    }

    private void SetSharingStatus(string status)
    {
        SharingStatus = status;
    }

    private static string GetInputDedupeKey(string value)
    {
        return value.Contains('@', StringComparison.Ordinal)
            ? $"email:{value.Trim().ToLowerInvariant()}"
            : $"userId:{value.Trim()}";
    }

    private static string RedactUserId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        const int visibleStart = 4;
        const int visibleEnd = 4;
        if (value.Length <= visibleStart + visibleEnd)
        {
            return new string('*', value.Length);
        }

        return $"{value[..visibleStart]}{new string('*', value.Length - visibleStart - visibleEnd)}{value[^visibleEnd..]}";
    }

    private void UpdateActivityRecordingStatus()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateActivityRecordingStatus);
            return;
        }
        ActivityRecordingStatus = activityTracker?.StatusText ??
            (UserSettings.ActivityRecordingEnabled ? "Recording enabled." : "Recording disabled. Saved history remains available.");
    }
}

public sealed class PrivateOrderShareEntryViewModel
{
    public PrivateOrderShareEntryViewModel(string value, string type, bool resolved)
    {
        Value = value;
        Type = type;
        Resolved = resolved;
    }

    public string Value { get; }
    public string Type { get; }
    public bool Resolved { get; }
    public string TypeLabel => Type.ToLowerInvariant() switch
    {
        "email" => "Email",
        "username" => "AFM username",
        _ => "User ID"
    };
}
