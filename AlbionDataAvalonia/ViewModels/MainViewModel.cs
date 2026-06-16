using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.State.Events;
using AlbionDataAvalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private enum MainPage
    {
        Dashboard,
        Combat,
        Gathering,
        Loot,
        Trades,
        Mails,
        Settings,
        Logs
    }

    private readonly PlayerState _playerState;
    private readonly NetworkListenerService _networkListener;
    private readonly SettingsManager _settingsManager;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly MailsViewModel _mailsViewModel;
    private readonly TradesViewModel _tradesViewModel;
    private readonly CombatViewModel _combatViewModel;
    private readonly GatheringViewModel _gatheringViewModel;
    private readonly LootViewModel _lootViewModel;
    private readonly Uploader _uploader;
    private readonly AuthService _authService;

    [ObservableProperty]
    private string appVersion;

    [ObservableProperty]
    private string locationName;

    [ObservableProperty]
    private string playerName = "Not set";

    [ObservableProperty]
    private string albionServerName;

    [ObservableProperty]
    private bool uploadToAfmOnly = false;

    partial void OnUploadToAfmOnlyChanged(bool value)
    {
        _playerState.UploadToAfmOnly = value;
    }

    [ObservableProperty]
    private bool contributeToPublic = false;

    partial void OnContributeToPublicChanged(bool value)
    {
        _playerState.ContributeToPublic = value;
    }

    [ObservableProperty]
    private bool shareWithFriends = false;

    partial void OnShareWithFriendsChanged(bool value)
    {
        _playerState.ShareWithFriends = value;
    }

    [ObservableProperty]
    private UserSettings userSettings;

    [ObservableProperty]
    private bool showGetInGame = false;

    [ObservableProperty]
    private bool showChangeCity = false;
    [ObservableProperty]
    private string changeCityText;
    [ObservableProperty]
    private bool showEncrypted = false;

    [ObservableProperty]
    private bool showDataUi = false;

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
    private int privateUploadedItemEstimatedMarketValuesCount;

    [ObservableProperty]
    private bool redBlinking = false;
    [ObservableProperty]
    private bool greenBlinking = false;

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

    public ObservableCollection<SidebarStatusItem> SidebarStatusItems { get; } = new();
    public bool ShowMacOSCapturePermissionSetup =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        && _networkListener?.IsMacOSCapturePermissionSetupRequired == true;
    public string MacOSCapturePermissionButtonText =>
        IsInstallingMacOSCapturePermissions ? "Installing..." : "Install permissions";

    private int oldUploadQueueSize = 0;
    private int oldRunningTasksCount = 0;
    private bool sidebarStatusRefreshQueued;

    public MainViewModel()
    {
        SidebarStatusItems.Add(SidebarStatusItem.Ok("Ready", "Capture state looks ready."));
    }

    public MainViewModel(NetworkListenerService networkListener, PlayerState playerState, SettingsManager settingsManager, SettingsViewModel settingsViewModel, LogsViewModel logsViewModel, MailsViewModel mailsViewModel, TradesViewModel tradesViewModel, CombatViewModel combatViewModel, GatheringViewModel gatheringViewModel, LootViewModel lootViewModel, Uploader uploader, AuthService authService)
    {
        _playerState = playerState;
        _networkListener = networkListener;
        _settingsManager = settingsManager;
        _settingsViewModel = settingsViewModel;
        _logsViewModel = logsViewModel;
        _mailsViewModel = mailsViewModel;
        _tradesViewModel = tradesViewModel;
        _combatViewModel = combatViewModel;
        _gatheringViewModel = gatheringViewModel;
        _lootViewModel = lootViewModel;
        _uploader = uploader;
        _authService = authService;

        LocationName = _playerState.Location.FriendlyName;
        PlayerName = _playerState.PlayerName;
        AlbionServerName = _playerState.AlbionServer?.Name ?? "Unknown";

        UploadQueueSize = _uploader.uploadQueueCount;
        oldUploadQueueSize = UploadQueueSize;
        RunningTasksCount = _uploader.runningTasksCount;
        oldRunningTasksCount = RunningTasksCount;

        AppVersion = ClientUpdater.GetVersion() ?? "Unknown";

        UpdateVisibilities();

        _uploader.OnChange += UpdateUploadStats;

        _playerState.OnPlayerStateChanged += UpdateState;
        _playerState.OnPublicUploadStatsChanged += ApplyPublicUploadStats;
        _playerState.OnPrivateUploadStatsChanged += ApplyPrivateUploadStats;
        ApplyPublicUploadStats(_playerState.PublicUploadStats);
        ApplyPrivateUploadStats(_playerState.PrivateUploadStats);

        _authService.FirebaseUserChanged += user =>
        {
            FirebaseUser = user;
            UserLoggedIn = FirebaseUser is not null;
            RefreshSidebarStatus();
        };

        FirebaseUser = _authService.CurrentFirebaseUser;
        UserLoggedIn = FirebaseUser is not null;
        RefreshSidebarStatus();

        _networkListener.MacOSCapturePermissionSetupRequiredChanged += NetworkListener_MacOSCapturePermissionSetupRequiredChanged;

        userSettings = _settingsManager.UserSettings;

        NavigateTo(MainPage.Dashboard);
    }

    public bool IsDashboardSelected => _selectedPage == MainPage.Dashboard;
    public bool IsCombatSelected => _selectedPage == MainPage.Combat;
    public bool IsGatheringSelected => _selectedPage == MainPage.Gathering;
    public bool IsLootSelected => _selectedPage == MainPage.Loot;
    public bool IsTradesSelected => _selectedPage == MainPage.Trades;
    public bool IsMailsSelected => _selectedPage == MainPage.Mails;
    public bool IsSettingsSelected => _selectedPage == MainPage.Settings;
    public bool IsLogsSelected => _selectedPage == MainPage.Logs;

    private void NavigateTo(MainPage page)
    {
        if (_selectedPage != page)
        {
            _selectedPage = page;
            OnPropertyChanged(nameof(IsDashboardSelected));
            OnPropertyChanged(nameof(IsCombatSelected));
            OnPropertyChanged(nameof(IsGatheringSelected));
            OnPropertyChanged(nameof(IsLootSelected));
            OnPropertyChanged(nameof(IsTradesSelected));
            OnPropertyChanged(nameof(IsMailsSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsLogsSelected));
        }

        CurrentView = page switch
        {
            MainPage.Dashboard => NpCapInstallationChecker.IsNpCapInstalled() ? new DashboardView() : new PCapView(),
            MainPage.Combat => new CombatView(_combatViewModel),
            MainPage.Gathering => new GatheringView(_gatheringViewModel),
            MainPage.Loot => new LootView(_lootViewModel),
            MainPage.Trades => new TradesView(_tradesViewModel),
            MainPage.Mails => new MailsView(_mailsViewModel),
            MainPage.Settings => new SettingsView(_settingsViewModel),
            MainPage.Logs => new LogsView(_logsViewModel),
            _ => NpCapInstallationChecker.IsNpCapInstalled() ? new DashboardView() : new PCapView()
        };

        if (page == MainPage.Trades)
        {
            _tradesViewModel.EnsureLoaded();
        }
        else if (page == MainPage.Mails)
        {
            _mailsViewModel.EnsureLoaded();
        }
    }

    private void UpdateVisibilities()
    {
        ShowChangeCity = !_playerState.CheckLocationIsSet() && _playerState.IsInGame;
        ShowGetInGame = !_playerState.IsInGame;
        ShowEncrypted = _playerState.HasEncryptedData;
        ShowDataUi = !(ShowChangeCity || ShowGetInGame);

        if (_playerState.Location == AlbionLocations.Unknown)
        {
            ChangeCityText = "Current location is not supported. Go to a relevant market.";
        }
        else if (_playerState.Location == AlbionLocations.Unset)
        {
            ChangeCityText = "Location has not been set. Please change maps.";
        }
    }

    private void ApplyPublicUploadStats(PublicUploadStatsSnapshot stats)
    {
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
        PrivateUploadSuccessCount = stats.SuccessCount;
        PrivateUploadFailedCount = stats.FailedCount;
        PrivateUploadedMarketOffersCount = stats.MarketOffersCount;
        PrivateUploadedMarketRequestsCount = stats.MarketRequestsCount;
        PrivateUploadedAchievementsCount = stats.AchievementsCount;
        PrivateUploadedGlobalMultipliersCount = stats.GlobalMultipliersCount;
        PrivateUploadedItemEstimatedMarketValuesCount = stats.ItemEstimatedMarketValuesCount;
    }

    private void UpdateState(object? sender, PlayerStateEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateState(sender, e));
            return;
        }

        LocationName = e.Location.FriendlyName;
        PlayerName = e.Name;
        AlbionServerName = e.AlbionServer?.Name ?? "Unknown";
        UploadToAfmOnly = e.UploadToAfmOnly;
        ContributeToPublic = e.ContributeToPublic;
        ShareWithFriends = e.ShareWithFriends;

        UpdateVisibilities();
        RefreshSidebarStatus();
    }

    private void RefreshSidebarStatus()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (sidebarStatusRefreshQueued)
            {
                return;
            }

            sidebarStatusRefreshQueued = true;
            Dispatcher.UIThread.Post(RefreshSidebarStatus);
            return;
        }

        sidebarStatusRefreshQueued = false;

        if (_playerState == null)
        {
            return;
        }

        SidebarStatusItems.Clear();

        if (!UserLoggedIn)
        {
            AddSidebarStatus(SidebarStatusItem.Warning(
                "AFM Logged Out",
                "Sign in to upload private AFM data."));
        }

        if (ShowMacOSCapturePermissionSetup)
        {
            AddSidebarStatus(SidebarStatusItem.Warning(
                "Capture Blocked",
                "macOS denied packet capture access. Install capture permissions to allow AFM Data Client to read network packets."));
        }

        if (!_playerState.IsInGame)
        {
            AddSidebarStatus(SidebarStatusItem.Warning(
                "Not In Game",
                "Open Albion Online and enter the game."));
        }

        if (_playerState.HasEncryptedData)
        {
            AddSidebarStatus(SidebarStatusItem.Warning(
                "Encrypted",
                "Market orders are encrypted. Go to AFM Discord to understand what's going on."));
        }

        if (_playerState.AlbionServer is null)
        {
            AddSidebarStatus(SidebarStatusItem.Warning(
                "Server Undetected",
                "The Albion server has not been detected yet."));
        }

        if (string.IsNullOrWhiteSpace(_playerState.PlayerName)
            || string.Equals(_playerState.PlayerName, "Not set", StringComparison.OrdinalIgnoreCase))
        {
            AddSidebarStatus(SidebarStatusItem.Warning(
                "User Undetected",
                "Your character has not been detected yet. Change maps."));
        }

        if (_playerState.Location == AlbionLocations.Unset || _playerState.Location == AlbionLocations.Unknown)
        {
            AddSidebarStatus(SidebarStatusItem.Warning(
                "Location Undetected",
                "Your location has not been detected yet. Change maps."));
        }

        if (SidebarStatusItems.Count == 0)
        {
            AddSidebarStatus(SidebarStatusItem.Ok(
                "Ready",
                "Capture state looks ready."));
        }
    }

    private void NetworkListener_MacOSCapturePermissionSetupRequiredChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => NetworkListener_MacOSCapturePermissionSetupRequiredChanged(sender, e));
            return;
        }

        OnPropertyChanged(nameof(ShowMacOSCapturePermissionSetup));
        RefreshSidebarStatus();
    }

    private void AddSidebarStatus(SidebarStatusItem item)
    {
        foreach (var existingItem in SidebarStatusItems)
        {
            if (string.Equals(existingItem.Text, item.Text, StringComparison.Ordinal)
                && existingItem.Severity == item.Severity)
            {
                return;
            }
        }

        SidebarStatusItems.Add(item);
    }

    private void UpdateUploadStats()
    {
        UploadQueueSize = _uploader.uploadQueueCount;
        RunningTasksCount = _uploader.runningTasksCount;

        if (UploadQueueSize > oldUploadQueueSize)
        {
            BlinkRed().ConfigureAwait(false);
        }
        else if (RunningTasksCount < oldRunningTasksCount)
        {
            BlinkGreen().ConfigureAwait(false);
        }

        oldUploadQueueSize = UploadQueueSize;
        oldRunningTasksCount = RunningTasksCount;
    }

    private async Task BlinkRed()
    {
        RedBlinking = true;
        await Task.Delay(TimeSpan.FromSeconds(0.25));
        RedBlinking = false;
    }

    private async Task BlinkGreen()
    {
        GreenBlinking = true;
        await Task.Delay(TimeSpan.FromSeconds(0.25));
        GreenBlinking = false;
    }

    [RelayCommand]
    private void Exit()
    {
        Log.Information("Exiting application");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        ;
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
    private void ShowCombat()
    {
        NavigateTo(MainPage.Combat);
    }

    [RelayCommand]
    private void ShowGathering()
    {
        NavigateTo(MainPage.Gathering);
    }

    [RelayCommand]
    private void ShowLoot()
    {
        NavigateTo(MainPage.Loot);
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
    private void ShowMails()
    {
        NavigateTo(MainPage.Mails);
    }

    [RelayCommand]
    private void ShowTrades()
    {
        NavigateTo(MainPage.Trades);
    }

    [RelayCommand]
    private void OpenAFMWebsite()
    {
        OpenUrl("https://www.albionfreemarket.com");
    }

    [RelayCommand]
    private void OpenAODPWebsite()
    {
        OpenUrl("https://www.albion-online-data.com/");
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
                if (TryRestartApplication())
                {
                    return;
                }

                Log.Warning("Unable to restart AFM Data Client automatically. Restart the app manually to apply macOS packet capture permissions.");
            }
        }
        finally
        {
            IsInstallingMacOSCapturePermissions = false;
            OnPropertyChanged(nameof(ShowMacOSCapturePermissionSetup));
            RefreshSidebarStatus();
        }
    }

    private static bool TryRestartApplication()
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
        try
        {
            await _authService.SignInAsync();
        }
        catch (AuthServiceException ex)
        {
            Log.Error("Authentication error: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("An unexpected error occurred during login: {Message}", ex.Message);
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _authService.LogOut();
    }

    public void OpenUrl(object? urlObj)
    {
        var url = urlObj as string;

        if (url == null)
        {
            Log.Error("Invalid URL: {Url}", url);
            return;
        }

        try
        {
            var uri = new Uri(url);
        }
        catch (UriFormatException)
        {
            Log.Error("Invalid URL: {Url}", url);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("x-www-browser", url);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
            return;
        }

        return;
    }
}

public sealed class SidebarStatusItem
{
    private SidebarStatusItem(string text, string tooltip, SidebarStatusSeverity severity)
    {
        Text = text;
        Tooltip = tooltip;
        Severity = severity;
    }

    public string Text { get; }
    public string Tooltip { get; }
    public SidebarStatusSeverity Severity { get; }
    public bool IsOk => Severity == SidebarStatusSeverity.Ok;
    public bool IsWarning => Severity == SidebarStatusSeverity.Warning;

    public static SidebarStatusItem Ok(string text, string tooltip) => new(text, tooltip, SidebarStatusSeverity.Ok);
    public static SidebarStatusItem Warning(string text, string tooltip) => new(text, tooltip, SidebarStatusSeverity.Warning);
}

public enum SidebarStatusSeverity
{
    Ok,
    Warning
}
