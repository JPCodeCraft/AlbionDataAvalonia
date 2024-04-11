using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AlbionDataAvalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;

    [ObservableProperty]
    private UserSettings userSettings;

    [ObservableProperty]
    private double powSolveTimeAverage;

    public SettingsViewModel()
    {
    }

    public SettingsViewModel(SettingsManager settingsManager, PlayerState playerState)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;

        userSettings = _settingsManager.UserSettings;

        _playerState.OnPlayerStateChanged += (sender, args) => PowSolveTimeAverage = _playerState.PowSolveTimeAverage;
    }

}
