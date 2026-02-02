using AlbionDataAvalonia.Auth.Models;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.State.Events;
using AlbionDataAvalonia.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly PlayerState _playerState;
    private readonly NetworkListenerService _networkListener;
    private readonly SettingsManager _settingsManager;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly LogsViewModel _logsViewModel;
    private readonly MailsViewModel _mailsViewModel;
    private readonly TradesViewModel _tradesViewModel;
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

    [ObservableProperty]
    private int uploadQueueSize;
    [ObservableProperty]
    private int runningTasksCount;

    [ObservableProperty]
    private int uploadedMarketOffersCount;
    [ObservableProperty]
    private int uploadedMarketRequestsCount;
    [ObservableProperty]
    private int uploadedMonthlyHistoriesCount;
    [ObservableProperty]
    private int uploadedWeeklyHistoriesCount;
    [ObservableProperty]
    private int uploadedDailyHistoriesCount;
    [ObservableProperty]
    private int uploadedGoldHistoriesCount;
    [ObservableProperty]
    private int uploadSuccessCount;
    [ObservableProperty]
    private int uploadFailedCount;
    [ObservableProperty]
    private int uploadSkippedCount;

    [ObservableProperty]
    private bool redBlinking = false;
    [ObservableProperty]
    private bool greenBlinking = false;

    [ObservableProperty]
    private FirebaseAuthResponse? firebaseUser = null;
    [ObservableProperty]
    private bool userLoggedIn = false;

    private int oldUploadQueueSize = 0;
    private int oldRunningTasksCount = 0;

    public MainViewModel()
    {
    }

    public MainViewModel(NetworkListenerService networkListener, PlayerState playerState, SettingsManager settingsManager, SettingsViewModel settingsViewModel, LogsViewModel logsViewModel, MailsViewModel mailsViewModel, TradesViewModel tradesViewModel, Uploader uploader, AuthService authService)
    {
        _playerState = playerState;
        _networkListener = networkListener;
        _settingsManager = settingsManager;
        _settingsViewModel = settingsViewModel;
        _logsViewModel = logsViewModel;
        _mailsViewModel = mailsViewModel;
        _tradesViewModel = tradesViewModel;
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

        _playerState.OnUploadedMarketRequestsCountChanged += count => UploadedMarketRequestsCount = count;
        _playerState.OnUploadedMarketOffersCountChanged += count => UploadedMarketOffersCount = count;
        _playerState.OnUploadedHistoriesCountDicChanged += dic =>
        {
            UploadedMonthlyHistoriesCount = dic.ContainsKey(Timescale.Month) ? dic[Timescale.Month] : 0;
            UploadedWeeklyHistoriesCount = dic.ContainsKey(Timescale.Week) ? dic[Timescale.Week] : 0;
            UploadedDailyHistoriesCount = dic.ContainsKey(Timescale.Day) ? dic[Timescale.Day] : 0;
        };
        _playerState.OnUploadedGoldHistoriesCountChanged += count => UploadedGoldHistoriesCount = count;

        _playerState.OnUploadStatusCountDicChanged += UpdateUploadStatusCount;

        _authService.FirebaseUserChanged += user =>
        {
            FirebaseUser = user;
            UserLoggedIn = FirebaseUser is not null ? true : false;
        };
        
        userSettings = _settingsManager.UserSettings;

        if (NpCapInstallationChecker.IsNpCapInstalled())
        {
            CurrentView = new DashboardView();
        }
        else
        {
            CurrentView = new PCapView();
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

    private void UpdateUploadStatusCount(ConcurrentDictionary<UploadStatus, int> dic)
    {
        UploadSuccessCount = dic.TryGetValue(UploadStatus.Success, out int value) ? value : 0;
        UploadFailedCount = dic.TryGetValue(UploadStatus.Failed, out value) ? value : 0;
        UploadSkippedCount = dic.TryGetValue(UploadStatus.Skipped, out value) ? value : 0;
    }

    private void UpdateState(object? sender, PlayerStateEventArgs e)
    {
        LocationName = e.Location.FriendlyName;
        PlayerName = e.Name;
        AlbionServerName = e.AlbionServer?.Name ?? "Unknown";
        UploadToAfmOnly = e.UploadToAfmOnly;
        ContributeToPublic = e.ContributeToPublic;

        UpdateVisibilities();
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
        if (NpCapInstallationChecker.IsNpCapInstalled())
        {
            CurrentView = new DashboardView();
        }
        else
        {
            CurrentView = new PCapView();
        }
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentView = new SettingsView(_settingsViewModel);
    }

    [RelayCommand]
    private void ShowLogs()
    {
        CurrentView = new LogsView(_logsViewModel);
    }

    [RelayCommand]
    private void ShowMails()
    {
        CurrentView = new MailsView(_mailsViewModel);
    }

    [RelayCommand]
    private void ShowTrades()
    {
        CurrentView = new TradesView(_tradesViewModel);
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
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"sudo apt-get install libpcap-dev\"",
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Log.Error("Install requirements error (Linux): {Message}", ex.Message);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                // Prefer script inside the .app bundle exposed by run.sh
                var candidates = new System.Collections.Generic.List<string>();
                var bundleDir = Environment.GetEnvironmentVariable("AFM_APP_DIR");
                if (!string.IsNullOrWhiteSpace(bundleDir))
                    candidates.Add(Path.Combine(bundleDir!, "install", "install_access_bpf.sh"));

                // Fallback to AppContext.BaseDirectory (single-file extraction dir)
                candidates.Add(Path.Combine(AppContext.BaseDirectory, "install", "install_access_bpf.sh"));

                // Fallback to default install location
                candidates.Add("/Applications/AFM Data Client.app/Contents/MacOS/install/install_access_bpf.sh");

                string? scriptPath = null;
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { scriptPath = c; break; }
                }

                if (scriptPath == null)
                {
                    Log.Error("Install requirements script not found. Tried: {Candidates}", string.Join(", ", candidates));
                    return;
                }

                var osa = new ProcessStartInfo
                {
                    FileName = "osascript",
                    UseShellExecute = false
                };
                // do shell script "<path>" with administrator privileges
                osa.ArgumentList.Add("-e");
                osa.ArgumentList.Add($"do shell script \"{scriptPath}\" with administrator privileges");
                Process.Start(osa);
            }
            catch (Exception ex)
            {
                Log.Error("Install requirements error (macOS): {Message}", ex.Message);
            }
        }
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred while trying to install NpCap: {ex.Message}");
        }
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
