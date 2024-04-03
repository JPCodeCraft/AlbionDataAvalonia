using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Settings;

public class SettingsManager
{
    private string userSettingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "UserSettings.json");
    private string appSettingsDownloadUrl = "https://raw.githubusercontent.com/JPCodeCraft/AlbionDataAvalonia/master/AlbionDataAvalonia/AppSettings.json";
    public UserSettings UserSettings { get; private set; } = new UserSettings();
    public AppSettings AppSettings { get; private set; } = new AppSettings();

    public SettingsManager()
    {
        Initialize();
    }

    public async void Initialize()
    {
        LoadUserSettings();
        await LoadAppSettingsAsync();
    }

    private void LoadUserSettings()
    {
        if (File.Exists(userSettingsFilePath))
        {
            string json = File.ReadAllText(userSettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);

            if (settings != null)
            {
                UserSettings = settings;
            }

            UserSettings.PropertyChanged += UserSettings_PropertyChanged;
        }
    }

    private void UserSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SaveSettings();
    }

    public async Task LoadAppSettingsAsync()
    {
        try
        {
            using HttpClient client = new HttpClient();
            string json = await client.GetStringAsync(appSettingsDownloadUrl);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);

            if (settings != null)
            {
                AppSettings = settings;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error in LoadAppSettingsAsync: {ex}");
        }
    }

    public void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(UserSettings);
            File.WriteAllText(userSettingsFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Error in SaveSettings: {ex}");
        }
    }


}
