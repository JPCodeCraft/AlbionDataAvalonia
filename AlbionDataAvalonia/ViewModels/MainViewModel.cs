using AlbionData.Models;
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
using System.Diagnostics;
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

    [ObservableProperty]
    private AlbionData.Models.Location location;

    [ObservableProperty]
    private string playerName = "Not set";

    [ObservableProperty]
    private AlbionServer? albionServer;

    [ObservableProperty]
    private bool showGetInGame = false;
    [ObservableProperty]
    private bool showChangeCity = false;
    [ObservableProperty]
    private bool showDataUi = false;

    [ObservableProperty]
    private object currentView;

    [ObservableProperty]
    private int uploadQueueSize;

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
    private bool redBlinking = false;
    [ObservableProperty]
    private bool greenBlinking = false;

    private int oldUploadQueueSize = 0;

    public MainViewModel()
    {
    }

    public MainViewModel(NetworkListenerService networkListener, PlayerState playerState, SettingsManager settingsManager, SettingsViewModel settingsViewModel, LogsViewModel logsViewModel)
    {
        _playerState = playerState;
        _networkListener = networkListener;
        _settingsManager = settingsManager;
        _settingsViewModel = settingsViewModel;
        _logsViewModel = logsViewModel;

        Location = _playerState.Location;
        PlayerName = _playerState.PlayerName;
        AlbionServer = _playerState.AlbionServer;
        UploadQueueSize = _playerState.UploadQueueSize;
        oldUploadQueueSize = UploadQueueSize;

        UpdateVisibilities();

        _playerState.OnPlayerStateChanged += UpdateState;
        _playerState.OnUploadedMarketRequestsCountChanged += count => UploadedMarketRequestsCount = count;
        _playerState.OnUploadedMarketOffersCountChanged += count => UploadedMarketOffersCount = count;
        _playerState.OnUploadedHistoriesCountDicChanged += dic =>
        {
            UploadedMonthlyHistoriesCount = dic[Timescale.Month];
            UploadedWeeklyHistoriesCount = dic[Timescale.Week];
            UploadedDailyHistoriesCount = dic[Timescale.Day];
        };
        _playerState.OnUploadedGoldHistoriesCountChanged += count => UploadedGoldHistoriesCount = count;

        if (NpCapInstallationChecker.IsNpCapInstalled())
        {
            CurrentView = new DashboardView();
        }
        else
        {
            CurrentView = new PCapView();
        }
        _logsViewModel = logsViewModel;
    }

    private void UpdateVisibilities()
    {
        ShowChangeCity = !_playerState.CheckLocationIDIsSet() && _playerState.IsInGame;
        ShowGetInGame = !_playerState.IsInGame;
        ShowDataUi = !(ShowChangeCity || ShowGetInGame);
    }

    private void UpdateState(object? sender, PlayerStateEventArgs e)
    {
        Location = e.Location;
        PlayerName = e.Name;
        AlbionServer = e.AlbionServer;
        UploadQueueSize = e.UploadQueueSize;

        UpdateVisibilities();

        if (UploadQueueSize > oldUploadQueueSize)
        {
            BlinkRed().ConfigureAwait(false);
        }
        else if (UploadQueueSize < oldUploadQueueSize)
        {
            BlinkGreen().ConfigureAwait(false);
        }

        oldUploadQueueSize = UploadQueueSize;
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
        Log.Verbose("Exiting application");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        };
    }
    [RelayCommand]
    private void ShowMainWindow()
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
