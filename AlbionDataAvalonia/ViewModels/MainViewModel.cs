using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Players;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.State.Events;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.Ui.Services;
using AlbionDataAvalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private enum MainPage
    {
        Dashboard,
        Activity,
        Legendary,
        Trades,
        Settings,
        Logs
    }

    private readonly PlayerState _playerState;
    private readonly PlayerIdentityService? _playerIdentityService;
    private readonly NetworkListenerService _networkListener;
    private readonly SettingsManager _settingsManager;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly TradesViewModel _tradesViewModel;
    private readonly ActivityViewModel _activityViewModel;
    private readonly LegendaryViewModel _legendaryViewModel;
    private readonly Uploader _uploader;
    private readonly AuthService _authService;
    private readonly PacketDiagnostics? _packetDiagnostics;
    private readonly IClipboardService? _clipboard;

    [ObservableProperty]
    private string appVersion;

    [ObservableProperty]
    private string locationName;

    [ObservableProperty]
    private string playerName = "Detecting…";

    [ObservableProperty]
    private string playerAffiliationText = string.Empty;

    public bool HasPlayerAffiliation => !string.IsNullOrWhiteSpace(PlayerAffiliationText);

    partial void OnPlayerAffiliationTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPlayerAffiliation));
    }

    [ObservableProperty]
    private bool? hasPremium;

    public string PremiumStatusText => HasPremium switch
    {
        true => "Premium",
        false => "No Premium",
        null => "Premium Unknown"
    };

    public bool IsPremium => HasPremium == true;
    public bool IsNotPremium => HasPremium == false;

    partial void OnHasPremiumChanged(bool? value)
    {
        OnPropertyChanged(nameof(PremiumStatusText));
        OnPropertyChanged(nameof(IsPremium));
        OnPropertyChanged(nameof(IsNotPremium));
    }

    [ObservableProperty]
    private string albionServerName;

    [ObservableProperty]
    private bool isInitializing = true;

    [ObservableProperty]
    private bool uploadToAfmOnly = false;

    partial void OnUploadToAfmOnlyChanged(bool value)
    {
        _settingsManager.UserSettings.UploadToAfmOnly = value;
        _playerState.UploadToAfmOnly = value;
    }

    [ObservableProperty]
    private bool contributeToPublic = false;

    partial void OnContributeToPublicChanged(bool value)
    {
        _settingsManager.UserSettings.ContributeToPublic = value;
        _playerState.ContributeToPublic = value;
    }

    [ObservableProperty]
    private bool shareWithFriends = false;

    partial void OnShareWithFriendsChanged(bool value)
    {
        _settingsManager.UserSettings.ShareWithFriends = value;
        _playerState.ShareWithFriends = value;
    }

    [ObservableProperty]
    private UserSettings userSettings;

    [ObservableProperty]
    private bool showEncrypted = false;

    [ObservableProperty]
    private object currentView;

    private MainPage _selectedPage = MainPage.Dashboard;

    [ObservableProperty]
    private int uploadQueueSize;
    [ObservableProperty]
    private int runningTasksCount;

    [ObservableProperty]
    private int publicUploadSuccessCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPublicUploadFailures))]
    private int publicUploadFailedCount;
    [ObservableProperty]
    private int publicUploadedMarketOffersCount;
    [ObservableProperty]
    private int publicUploadedMarketRequestsCount;
    [ObservableProperty]
    private int publicUploadedGoldHistoriesCount;
    [ObservableProperty]
    private int publicUploadedMonthlyHistoriesCount;
    [ObservableProperty]
    private int publicUploadedWeeklyHistoriesCount;
    [ObservableProperty]
    private int publicUploadedDailyHistoriesCount;
    [ObservableProperty]
    private int publicUploadedBanditEventsCount;

    [ObservableProperty]
    private int privateUploadSuccessCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrivateUploadFailures))]
    private int privateUploadFailedCount;
    [ObservableProperty]
    private int privateUploadedMarketOffersCount;
    [ObservableProperty]
    private int privateUploadedMarketRequestsCount;
    [ObservableProperty]
    private int privateUploadedAchievementsCount;
    [ObservableProperty]
    private int privateUploadedGlobalMultipliersCount;
    [ObservableProperty]
    private int privateUploadedFestivitiesCount;
    [ObservableProperty]
    private int privateUploadedItemEstimatedMarketValuesCount;
    [ObservableProperty]
    private int privateIslandUploadsCount;

    public bool HasPublicUploadFailures => PublicUploadFailedCount > 0;
    public bool HasPrivateUploadFailures => PrivateUploadFailedCount > 0;

    [ObservableProperty]
    private bool isInstallingMacOSCapturePermissions = false;

    partial void OnIsInstallingMacOSCapturePermissionsChanged(bool value)
    {
        OnPropertyChanged(nameof(MacOSCapturePermissionButtonText));
    }

    [ObservableProperty]
    private FirebaseAuthResponse? firebaseUser = null;
    [ObservableProperty]
    private bool userLoggedIn = false;

    [ObservableProperty]
    private bool isLoginPending;

    [ObservableProperty]
    private bool showLoginStatus;

    [ObservableProperty]
    private string loginStatus = string.Empty;

    [ObservableProperty]
    private string loginAuthorizationUrl = string.Empty;

    public bool HasLoginAuthorizationUrl => !string.IsNullOrWhiteSpace(LoginAuthorizationUrl);

    partial void OnLoginAuthorizationUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasLoginAuthorizationUrl));
    }

    public bool IsMacOSCapturePermissionSetupOutdated =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        && _networkListener?.IsMacOSCapturePermissionSetupOutdated == true;
    public bool ShowMacOSCapturePermissionSetup =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        && (_networkListener?.IsMacOSCapturePermissionSetupRequired == true
            || IsMacOSCapturePermissionSetupOutdated);
    public string MacOSCapturePermissionButtonText =>
        IsInstallingMacOSCapturePermissions
            ? "Installing..."
            : IsMacOSCapturePermissionSetupOutdated
                ? "Update permissions"
                : "Install permissions";

    private CancellationTokenSource? loginCancellationTokenSource;
    private bool loginCanceledByUser;

    public MainViewModel()
    {
    }

    public MainViewModel(NetworkListenerService networkListener, PlayerState playerState, PlayerIdentityService playerIdentityService, SettingsManager settingsManager, SettingsViewModel settingsViewModel, LogsViewModel logsViewModel, TradesViewModel tradesViewModel, ActivityViewModel activityViewModel, LegendaryViewModel legendaryViewModel, Uploader uploader, AuthService authService, PacketDiagnostics? packetDiagnostics = null, IClipboardService? clipboard = null)
    {
        _playerState = playerState;
        _playerIdentityService = playerIdentityService;
        _networkListener = networkListener;
        _settingsManager = settingsManager;
        _settingsViewModel = settingsViewModel;
        _logsViewModel = logsViewModel;
        _tradesViewModel = tradesViewModel;
        _activityViewModel = activityViewModel;
        _legendaryViewModel = legendaryViewModel;
        _uploader = uploader;
        _authService = authService;
        _packetDiagnostics = packetDiagnostics;
        _clipboard = clipboard;
        Setup = new SetupStatusViewModel(networkListener, playerState, InstallNpCapCommand, InstallMacOSCapturePermissionsCommand);
        Setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupStatusViewModel.IsReady)) UpdateUploadStatus();
        };
        UploadToAfmOnly = _settingsManager.UserSettings.UploadToAfmOnly;
        ContributeToPublic = _settingsManager.UserSettings.ContributeToPublic;
        ShareWithFriends = _settingsManager.UserSettings.ShareWithFriends;
        _settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;

        LocationName = _playerState.Location.FriendlyName;
        PlayerName = string.IsNullOrWhiteSpace(_playerState.PlayerName) ? "Detecting…" : _playerState.PlayerName;
        HasPremium = _playerState.HasPremium;
        AlbionServerName = _playerState.AlbionServer?.Name ?? "Detecting…";
        RefreshPlayerAffiliation();

        UploadQueueSize = _uploader.uploadQueueCount;
        RunningTasksCount = _uploader.runningTasksCount;

        AppVersion = ClientUpdater.GetVersion() ?? "Unknown";

        UpdateVisibilities();

        _uploader.OnChange += UpdateUploadStats;

        _playerState.OnPlayerStateChanged += UpdateState;
        _playerIdentityService.IdentityChanged += OnPlayerIdentityChanged;
        _playerState.OnPublicUploadStatsChanged += ApplyPublicUploadStats;
        _playerState.OnPrivateUploadStatsChanged += ApplyPrivateUploadStats;
        ApplyPublicUploadStats(_playerState.PublicUploadStats);
        ApplyPrivateUploadStats(_playerState.PrivateUploadStats);

        _authService.FirebaseUserChanged += OnAuthUserChanged;

        FirebaseUser = _authService.CurrentFirebaseUser;
        UserLoggedIn = FirebaseUser is not null;

        _networkListener.MacOSCapturePermissionSetupRequiredChanged += NetworkListener_MacOSCapturePermissionSetupRequiredChanged;
        if (_packetDiagnostics is not null)
        {
            _packetDiagnostics.IsEnabledChanged += (_, _) => Dispatcher.UIThread.Post(() => IsDiagnosticsEnabled = _packetDiagnostics.IsEnabled);
        }

        userSettings = _settingsManager.UserSettings;

        IsDiagnosticsEnabled = _packetDiagnostics?.IsEnabled == true;
        UpdateUploadStatus();
        NavigateTo(MainPage.Dashboard);
    }

    public void CompleteInitialization()
    {
        IsInitializing = false;
    }

    public bool IsDashboardSelected => _selectedPage == MainPage.Dashboard;
    public bool IsActivitySelected => _selectedPage == MainPage.Activity;
    public bool IsLegendarySelected => _selectedPage == MainPage.Legendary;
    public bool IsTradesSelected => _selectedPage == MainPage.Trades;
    public bool IsSettingsSelected => _selectedPage == MainPage.Settings;
    public bool IsLogsSelected => _selectedPage == MainPage.Logs;

    private void NavigateTo(MainPage page)
    {
        if (_selectedPage != page)
        {
            _selectedPage = page;
            OnPropertyChanged(nameof(IsDashboardSelected));
            OnPropertyChanged(nameof(IsActivitySelected));
            OnPropertyChanged(nameof(IsLegendarySelected));
            OnPropertyChanged(nameof(IsTradesSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsLogsSelected));
        }

        CurrentView = page switch
        {
            MainPage.Dashboard => new DashboardView(),
            MainPage.Activity => new ActivityView(_activityViewModel),
            MainPage.Legendary => new LegendaryView(_legendaryViewModel),
            MainPage.Trades => new TradesView(_tradesViewModel),
            MainPage.Settings => new SettingsView(_settingsViewModel),
            MainPage.Logs => new LogsView(_logsViewModel),
            _ => new DashboardView()
        };

        if (page == MainPage.Legendary)
        {
            _legendaryViewModel.EnsureLoaded();
        }
        else if (page == MainPage.Trades)
        {
            _tradesViewModel.EnsureLoaded();
        }
    }

    // Settings > Uploads & sharing edits the same saved values as the sidebar quick settings.
    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(UserSettings.UploadToAfmOnly)
            or nameof(UserSettings.ContributeToPublic)
            or nameof(UserSettings.ShareWithFriends)))
        {
            return;
        }

        if (UiDispatch.Repost(() => OnUserSettingsPropertyChanged(sender, e))) return;

        UploadToAfmOnly = _settingsManager.UserSettings.UploadToAfmOnly;
        ContributeToPublic = _settingsManager.UserSettings.ContributeToPublic;
        ShareWithFriends = _settingsManager.UserSettings.ShareWithFriends;
    }

    private void UpdateVisibilities()
    {
        ShowEncrypted = _playerState.HasEncryptedData;
        Setup.OrdersEncrypted = ShowEncrypted;
    }

    // Token refreshes raise this from the thread pool.
    private void OnAuthUserChanged(FirebaseAuthResponse? user)
    {
        if (UiDispatch.Repost(() => OnAuthUserChanged(user))) return;

        FirebaseUser = user;
        UserLoggedIn = user is not null;
    }

    // Upload results arrive on uploader threads.
    private void ApplyPublicUploadStats(PublicUploadStatsSnapshot stats)
    {
        if (UiDispatch.Repost(() => ApplyPublicUploadStats(stats))) return;

        PublicUploadSuccessCount = stats.SuccessCount;
        PublicUploadFailedCount = stats.FailedCount;
        PublicUploadedMarketOffersCount = stats.MarketOffersCount;
        PublicUploadedMarketRequestsCount = stats.MarketRequestsCount;
        PublicUploadedGoldHistoriesCount = stats.GoldHistoriesCount;
        PublicUploadedMonthlyHistoriesCount = stats.MonthlyHistoriesCount;
        PublicUploadedWeeklyHistoriesCount = stats.WeeklyHistoriesCount;
        PublicUploadedDailyHistoriesCount = stats.DailyHistoriesCount;
        PublicUploadedBanditEventsCount = stats.BanditEventsCount;
    }

    private void ApplyPrivateUploadStats(PrivateUploadStatsSnapshot stats)
    {
        if (UiDispatch.Repost(() => ApplyPrivateUploadStats(stats))) return;

        PrivateUploadSuccessCount = stats.SuccessCount;
        PrivateUploadFailedCount = stats.FailedCount;
        PrivateUploadedMarketOffersCount = stats.MarketOffersCount;
        PrivateUploadedMarketRequestsCount = stats.MarketRequestsCount;
        PrivateUploadedAchievementsCount = stats.AchievementsCount;
        PrivateUploadedGlobalMultipliersCount = stats.GlobalMultipliersCount;
        PrivateUploadedFestivitiesCount = stats.FestivitiesCount;
        PrivateUploadedItemEstimatedMarketValuesCount = stats.ItemEstimatedMarketValuesCount;
        PrivateIslandUploadsCount = stats.IslandUploadsCount;
    }

    private void UpdateState(object? sender, PlayerStateEventArgs e)
    {
        if (UiDispatch.Repost(() => UpdateState(sender, e))) return;

        // Snapshots raised on capture threads are posted, so a late one can be older than the current state.
        LocationName = _playerState.Location.FriendlyName;
        PlayerName = string.IsNullOrWhiteSpace(_playerState.PlayerName) ? "Detecting…" : _playerState.PlayerName;
        HasPremium = _playerState.HasPremium;
        AlbionServerName = _playerState.AlbionServer?.Name ?? "Detecting…";
        RefreshPlayerAffiliation();
        // Queued player snapshots must not overwrite newer saved preferences.
        UploadToAfmOnly = _settingsManager.UserSettings.UploadToAfmOnly;
        ContributeToPublic = _settingsManager.UserSettings.ContributeToPublic;
        ShareWithFriends = _settingsManager.UserSettings.ShareWithFriends;

        UpdateVisibilities();
        Setup.Update();
    }

    private void OnPlayerIdentityChanged(PlayerIdentitySnapshot identity)
    {
        if (!string.Equals(
                identity.PlayerName,
                _playerState.PlayerName,
                StringComparison.OrdinalIgnoreCase)
            || (_playerState.AlbionServer?.Id is { } serverId
                && identity.ServerId is { } identityServerId
                && serverId != identityServerId))
        {
            return;
        }

        if (UiDispatch.Repost(() => OnPlayerIdentityChanged(identity))) return;

        PlayerAffiliationText = FormatPlayerAffiliation(identity.AllianceName, identity.GuildName);
    }

    private void RefreshPlayerAffiliation()
    {
        PlayerAffiliationText = string.Empty;
        var serverId = _playerState.AlbionServer?.Id;
        if (_playerIdentityService?.TryGetByName(
                serverId,
                _playerState.PlayerName,
                out var identity) != true
            || (serverId is not null
                && identity.ServerId is not null
                && serverId != identity.ServerId))
        {
            return;
        }

        PlayerAffiliationText = FormatPlayerAffiliation(identity.AllianceName, identity.GuildName);
    }

    private static string FormatPlayerAffiliation(string allianceName, string guildName)
    {
        var alliance = allianceName.Trim();
        var guild = guildName.Trim();
        if (string.IsNullOrWhiteSpace(alliance))
        {
            return guild;
        }

        return string.IsNullOrWhiteSpace(guild)
            ? $"[{alliance}]"
            : $"[{alliance}] {guild}";
    }

    private void NetworkListener_MacOSCapturePermissionSetupRequiredChanged(object? sender, EventArgs e)
    {
        if (UiDispatch.Repost(() => NetworkListener_MacOSCapturePermissionSetupRequiredChanged(sender, e))) return;

        OnPropertyChanged(nameof(ShowMacOSCapturePermissionSetup));
        Setup.Update();
    }

    private void UpdateUploadStats()
    {
        if (UiDispatch.Repost(UpdateUploadStats)) return;

        UploadQueueSize = _uploader.uploadQueueCount;
        RunningTasksCount = _uploader.runningTasksCount;
        UpdateUploadStatus();
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        Log.Information("Exiting application");
        await App.ShutdownAsync();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
    [RelayCommand]
    public void ShowMainWindow()
    {
        Log.Verbose("Showing MainWindow");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow != null)
            {
                desktop.MainWindow.WindowState = WindowState.Normal;
                desktop.MainWindow.IsVisible = true;
                desktop.MainWindow.Activate();
            }
            else
            {
                if (desktop.MainWindow == null)
                {
                    desktop.MainWindow = new MainWindow(_settingsManager);
                    desktop.MainWindow.DataContext = this;
                }
                desktop.MainWindow.Show();
                desktop.MainWindow.Activate();
            }
        }
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        NavigateTo(MainPage.Dashboard);
    }

    [RelayCommand]
    private void ShowActivity()
    {
        NavigateTo(MainPage.Activity);
    }


    [RelayCommand]
    private void ShowLegendary()
    {
        NavigateTo(MainPage.Legendary);
    }

    [RelayCommand]
    private void ShowSettings()
    {
        NavigateTo(MainPage.Settings);
    }

    [RelayCommand]
    private void ShowLogs()
    {
        NavigateTo(MainPage.Logs);
    }

    [RelayCommand]
    private void ShowUploadSettings()
    {
        _settingsViewModel.SelectedTabIndex = SettingsViewModel.UploadsTabIndex;
        NavigateTo(MainPage.Settings);
    }

    [RelayCommand]
    private void ShowTrades()
    {
        NavigateTo(MainPage.Trades);
    }

    [RelayCommand]
    private void OpenAFMWebsite()
    {
        SystemLauncher.TryOpenUrl(SystemLauncher.AfmWebsiteUrl);
    }

    [RelayCommand]
    private void OpenAODPWebsite()
    {
        SystemLauncher.TryOpenUrl(SystemLauncher.AodpWebsiteUrl);
    }

    [RelayCommand]
    private void InstallNpCap()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                OpenUrl(_settingsManager.AppSettings.NPCapDownloadUrl);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"sudo apt-get install libpcap-dev\"",
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred while trying to install NpCap: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task InstallMacOSCapturePermissions()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        try
        {
            IsInstallingMacOSCapturePermissions = true;
            var installed = await _networkListener.InstallMacOSCapturePermissionsAsync();
            if (installed)
            {
                Log.Information("Restarting AFM Data Client after macOS packet capture permission setup.");
                if (await TryRestartApplicationAsync())
                {
                    return;
                }

                Log.Warning("Unable to restart AFM Data Client automatically. Restart the app manually to apply macOS packet capture permissions.");
            }
        }
        finally
        {
            IsInstallingMacOSCapturePermissions = false;
            OnPropertyChanged(nameof(IsMacOSCapturePermissionSetupOutdated));
            OnPropertyChanged(nameof(ShowMacOSCapturePermissionSetup));
            OnPropertyChanged(nameof(MacOSCapturePermissionButtonText));
            Setup.Update();
        }
    }

    private static async Task<bool> TryRestartApplicationAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        try
        {
            var appBundlePath = GetCurrentMacOSAppBundlePath();
            if (string.IsNullOrWhiteSpace(appBundlePath))
            {
                Log.Warning("Unable to find the current macOS app bundle path for automatic restart.");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("sleep 1; open -n " + QuoteForPosixShell(appBundlePath));

            await App.ShutdownAsync();
            Process.Start(startInfo);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unable to restart AFM Data Client automatically.");
            return false;
        }
    }

    private static string? GetCurrentMacOSAppBundlePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (string.Equals(directory.Extension, ".app", StringComparison.OrdinalIgnoreCase))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string QuoteForPosixShell(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    [RelayCommand]
    private async Task Login()
    {
        if (loginCancellationTokenSource is not null)
        {
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        loginCancellationTokenSource = cancellationTokenSource;
        loginCanceledByUser = false;
        IsLoginPending = true;
        UpdateLoginStatus("Preparing Google sign-in...");

        try
        {
            await _authService.SignInAsync(OnLoginAuthorizationReady, cancellationTokenSource.Token);
            ShowLoginStatus = false;
            LoginStatus = string.Empty;
        }
        catch (AuthServiceException ex)
        {
            Log.Error(ex, "Authentication error");
            UpdateLoginStatus(ex.Kind switch
            {
                AuthServiceErrorKind.LoopbackUnavailable =>
                    "AFM could not reserve a local sign-in port. Close other AFM instances and try again.",
                AuthServiceErrorKind.AuthorizationDenied =>
                    "Google sign-in was canceled or denied.",
                _ => "Sign-in failed. Check your connection and try again."
            });
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
            UpdateLoginStatus(loginCanceledByUser
                ? "Sign-in canceled."
                : "Sign-in timed out. Try again.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An unexpected error occurred during login");
            UpdateLoginStatus("Sign-in failed. Check your connection and try again.");
        }
        finally
        {
            if (ReferenceEquals(loginCancellationTokenSource, cancellationTokenSource))
            {
                loginCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
            IsLoginPending = false;
            LoginAuthorizationUrl = string.Empty;
        }
    }

    [RelayCommand]
    private void OpenLoginBrowser()
    {
        if (!Uri.TryCreate(LoginAuthorizationUrl, UriKind.Absolute, out var uri))
        {
            UpdateLoginStatus("The sign-in link is not ready yet.");
            return;
        }

        UpdateLoginStatus(SystemLauncher.TryOpenUrl(uri)
            ? "Waiting for Google sign-in in your browser..."
            : "AFM could not open your browser. Copy the sign-in link and open it in a browser on this computer.");
    }

    [RelayCommand]
    private async Task CopyLoginLink()
    {
        if (string.IsNullOrWhiteSpace(LoginAuthorizationUrl)) return;
        UpdateLoginStatus(_clipboard is not null && await _clipboard.SetTextAsync(LoginAuthorizationUrl)
            ? "Sign-in link copied. Open it in a browser on this computer."
            : "Could not copy the sign-in link. Use Open browser again instead.");
    }

    [RelayCommand]
    private void CancelLogin()
    {
        if (loginCancellationTokenSource is null)
        {
            return;
        }

        loginCanceledByUser = true;
        UpdateLoginStatus("Canceling sign-in...");
        loginCancellationTokenSource.Cancel();
    }

    [RelayCommand]
    private async Task Logout()
    {
        await _authService.LogOut();
    }

    public void OpenUrl(object? urlObj)
    {
        if (urlObj is not string url)
        {
            Log.Error("Invalid URL: {Url}", urlObj);
            return;
        }

        SystemLauncher.TryOpenUrl(url);
    }

    public void UpdateLoginStatus(string message)
    {
        LoginStatus = message;
        ShowLoginStatus = !string.IsNullOrWhiteSpace(message);
    }

    private void OnLoginAuthorizationReady(Uri authorizationUri)
    {
        if (UiDispatch.Repost(() => OnLoginAuthorizationReady(authorizationUri))) return;

        if (!IsLoginPending)
        {
            return;
        }

        LoginAuthorizationUrl = authorizationUri.AbsoluteUri;
        UpdateLoginStatus(SystemLauncher.TryOpenUrl(authorizationUri)
            ? "Waiting for Google sign-in in your browser..."
            : "AFM could not open your browser. Copy the sign-in link and open it in a browser on this computer.");
    }
}
