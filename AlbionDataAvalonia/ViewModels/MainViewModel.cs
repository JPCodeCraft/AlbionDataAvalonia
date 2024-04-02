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
    private object currentView;

    [ObservableProperty]
    private bool pcapIsInstalled = false;

    public MainViewModel()
    {
    }

    public MainViewModel(NetworkListenerService networkListener, PlayerState playerState, SettingsManager settingsManager, SettingsViewModel settingsViewModel)
    {
        _playerState = playerState;
        _networkListener = networkListener;
        _settingsManager = settingsManager;
        _settingsViewModel = settingsViewModel;

        _playerState.OnPlayerStateChanged += UpdateState;

        pcapIsInstalled = WinPCapInstallationChecker.IsWinPCapInstalled();

        if (pcapIsInstalled)
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
                desktop.MainWindow = new MainWindow(_settingsManager);
                desktop.MainWindow.DataContext = this;
                desktop.MainWindow.Show();
                desktop.MainWindow.Activate();
            }
        }
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        if (PcapIsInstalled)
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
    private void InstallWinPCap()
    {
        if (WinPCapInstallationChecker.InstallWinPCap())
        {
            if (WinPCapInstallationChecker.IsWinPCapInstalled())
            {
                PcapIsInstalled = true;
                CurrentView = new DashboardView();
                _networkListener.Run();
            }
        }
    }
}
