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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly PlayerState _playerState;
    private readonly NetworkListenerService _networkListener;
    private readonly SettingsManager _settingsManager;
    private readonly SettingsViewModel _settingsViewModel;

    [ObservableProperty]
    private AlbionData.Models.Location location;

    [ObservableProperty]
    private string playerName = "Not set";

    [ObservableProperty]
    private AlbionServer? albionServer;

    [ObservableProperty]
    private bool isInGame;

    [ObservableProperty]
    private bool isNotInGame;

    [ObservableProperty]
    private object currentView;

    [ObservableProperty]
    private int uploadQueueSize;

    [ObservableProperty]
    private int uploadedMarketOffersCount;
    [ObservableProperty]
    private int uploadedMarketRequestsCount;
    [ObservableProperty]
    private ObservableCollection<KeyValuePair<Timescale, int>> uploadedHistoriesCountCollection = new();
    [ObservableProperty]
    private int uploadedGoldHistoriesCount;

    public MainViewModel()
    {
    }

    public MainViewModel(NetworkListenerService networkListener, PlayerState playerState, SettingsManager settingsManager, SettingsViewModel settingsViewModel)
    {
        _playerState = playerState;
        _networkListener = networkListener;
        _settingsManager = settingsManager;
        _settingsViewModel = settingsViewModel;

        Location = _playerState.Location;
        PlayerName = _playerState.PlayerName;
        AlbionServer = _playerState.AlbionServer;
        IsNotInGame = !IsInGame;
        UploadQueueSize = _playerState.UploadQueueSize;

        UploadedHistoriesCountCollection = new ObservableCollection<KeyValuePair<Timescale, int>>(_playerState.UploadedHistoriesCountDic);

        _playerState.OnPlayerStateChanged += UpdateState;
        _playerState.OnUploadedMarketRequestsCountChanged += count => UploadedMarketRequestsCount = count;
        _playerState.OnUploadedMarketOffersCountChanged += count => UploadedMarketOffersCount = count;
        _playerState.OnUploadedHistoriesCountDicChanged += dic => UploadedHistoriesCountCollection = new ObservableCollection<KeyValuePair<Timescale, int>>(dic);
        _playerState.OnUploadedGoldHistoriesCountChanged += count => UploadedGoldHistoriesCount = count;

        if (NpCapInstallationChecker.IsNpCapInstalled())
        {
            CurrentView = new DashboardView();
        }
        else
        {
            CurrentView = new PCapView();
        }
    }

    private void UpdateState(object? sender, PlayerStateEventArgs e)
    {
        Location = e.Location;
        PlayerName = e.Name;
        AlbionServer = e.AlbionServer;
        IsInGame = e.IsInGame;
        IsNotInGame = !e.IsInGame;
        UploadQueueSize = e.UploadQueueSize;
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
    private void InstallNpCap()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _settingsManager.AppSettings.NPCapDownloadUrl,
                    UseShellExecute = true
                });
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
}
