using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
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

    [ObservableProperty]
    private AlbionData.Models.Location location;

    [ObservableProperty]
    private string playerName = "Not set";

    [ObservableProperty]
    private AlbionServer? albionServer;

    [ObservableProperty]
    private object currentView;

    public MainViewModel()
    {
    }

    public MainViewModel(NetworkListenerService networkListener, PlayerState playerState)
    {
        _playerState = playerState;
        networkListener.Run();

        currentView = new DashboardView();

        _playerState.OnPlayerStateChanged += UpdateState;
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
            }
        }
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        CurrentView = new DashboardView();
    }

    [RelayCommand]
    private void ShowSettings()
    {
        CurrentView = new SettingsView();
    }
}
