using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionDataAvalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private const int MaxSharedFriendsLimit = 5;

    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly CombatTrackerService? combatTracker;
    private readonly AuthService? authService;
    private readonly AFMUploader? afmUploader;
    private readonly DatabaseBackupService? databaseBackupService;

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
    private readonly TimeSpan combatStatsRefreshInterval = TimeSpan.FromSeconds(3);
    private DateTimeOffset lastCombatStatsRefresh = DateTimeOffset.MinValue;
    private IDisposable? pendingCombatStatsRefreshRegistration;
    private readonly TimeSpan combatRetentionCommitDelay = TimeSpan.FromMilliseconds(750);
    private IDisposable? pendingCombatRetentionCommitRegistration;
    private bool applyingPendingCombatEncounterRetentionLimit;

    public ObservableCollection<PrivateOrderShareEntryViewModel> SharedUsers { get; } = new();

    public SettingsViewModel()
    {
        UserSettings = new UserSettings();
        CombatRetentionLimit = UserSettings.CombatEncounterRetentionLimit;
        PendingCombatEncounterRetentionLimit = UserSettings.CombatEncounterRetentionLimit;
    }

    public SettingsViewModel(
        SettingsManager settingsManager,
        PlayerState playerState,
        CombatTrackerService combatTracker,
        AuthService authService,
        AFMUploader afmUploader,
        DatabaseBackupService databaseBackupService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        this.combatTracker = combatTracker;
        this.authService = authService;
        this.afmUploader = afmUploader;
        this.databaseBackupService = databaseBackupService;

        UserSettings = _settingsManager.UserSettings;
        PendingCombatEncounterRetentionLimit = UserSettings.CombatEncounterRetentionLimit;
        UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;

        UpdatePowSolveStatistics();
        lastPowSolveStatsRefresh = DateTimeOffset.UtcNow;
        _playerState.OnPlayerStateChanged += (_, _) => MaybeUpdatePowSolveStatistics();

        UpdateCombatStatistics();
        lastCombatStatsRefresh = DateTimeOffset.UtcNow;
        this.combatTracker.SnapshotChanged += _ => MaybeUpdateCombatStatistics();

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

    public bool IsShowMainWindowOnStartup
    {
        get => UserSettings.StartupWindowMode == StartupWindowMode.ShowMainWindow;
        set
        {
            if (value)
            {
                UserSettings.StartupWindowMode = StartupWindowMode.ShowMainWindow;
            }
        }
    }

    public bool IsMinimizedToTaskbarOnStartup
    {
        get => UserSettings.StartupWindowMode == StartupWindowMode.MinimizedToTaskbar;
        set
        {
            if (value)
            {
                UserSettings.StartupWindowMode = StartupWindowMode.MinimizedToTaskbar;
            }
        }
    }

    public bool IsHiddenToTrayOnStartup
    {
        get => UserSettings.StartupWindowMode == StartupWindowMode.HiddenToTray;
        set
        {
            if (value)
            {
                UserSettings.StartupWindowMode = StartupWindowMode.HiddenToTray;
            }
        }
    }

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
        if (e.PropertyName == nameof(UserSettings.ShutDownOnClose))
        {
            OnPropertyChanged(nameof(CloseButtonHidesToTray));
            return;
        }

        if (e.PropertyName == nameof(UserSettings.StartupWindowMode))
        {
            OnPropertyChanged(nameof(IsShowMainWindowOnStartup));
            OnPropertyChanged(nameof(IsMinimizedToTaskbarOnStartup));
            OnPropertyChanged(nameof(IsHiddenToTrayOnStartup));
            return;
        }

        if (e.PropertyName != nameof(UserSettings.CombatEncounterRetentionLimit))
        {
            return;
        }

        pendingCombatRetentionCommitRegistration?.Dispose();
        pendingCombatRetentionCommitRegistration = null;
        SyncPendingCombatEncounterRetentionLimit(UserSettings.CombatEncounterRetentionLimit);
        UpdateCombatStatistics();
    }

    partial void OnIsUserLoggedInChanged(bool value)
    {
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
            ? $"Unable to add {unresolvedEntries.First()}. Check the ID or email and try again."
            : $"Unable to add {string.Join(", ", unresolvedEntries)}. Check the IDs or emails and try again.";
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
    public string TypeLabel => string.Equals(Type, "email", StringComparison.OrdinalIgnoreCase) ? "Email" : "User ID";
}
