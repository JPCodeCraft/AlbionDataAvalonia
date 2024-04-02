using AlbionDataAvalonia.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionDataAvalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;

    [ObservableProperty]
    private UserSettings userSettings;

    public SettingsViewModel()
    {
    }

    public SettingsViewModel(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;

        userSettings = _settingsManager.UserSettings;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        //_settingsManager.UserSettings.StartHidden = setting;
        _settingsManager.SaveSettings();
    }
}
