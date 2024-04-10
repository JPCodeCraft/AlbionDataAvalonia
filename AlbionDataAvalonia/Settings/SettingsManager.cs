using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Settings;

public class SettingsManager
{
    string userSettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFMDataClient");

    private string deafultUserSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "DefaultUserSettings.json");
    private string deafultAppSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "DefaultAppSettings.json");

    private bool loadedAppSettingsFromRemote = false;

    private string appSettingsDownloadUrl = "https://raw.githubusercontent.com/JPCodeCraft/AlbionDataAvalonia/master/AlbionDataAvalonia/DefaultAppSettings.json";
    public UserSettings UserSettings { get; private set; }
    public AppSettings AppSettings { get; private set; }
    public SettingsManager()
    {
        LoadUserSettings();
    }

    public async Task Initialize()
    {
        await LoadAppSettingsAsync();
    }

    private void LoadUserSettings()
    {
        // Get the path to the user settings file in the local app data directory
        string localUserSettingsFilePath = Path.Combine(userSettingsDirectory, "UserSettings.json");

        // If the user settings file doesn't exist in the local app data directory, use the default settings file
        if (!File.Exists(localUserSettingsFilePath))
        {
            localUserSettingsFilePath = deafultUserSettingsFilePath;
            string json = File.ReadAllText(localUserSettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);

            if (settings != null)
            {
                UserSettings = settings;
                SaveUserSettings(); // Save the default settings to a new user settings file
            }
        }
        else
        {
            string json = File.ReadAllText(localUserSettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);

            if (settings != null)
            {
                UserSettings = settings;
            }
        }

        UserSettings.PropertyChanged += UserSettings_PropertyChanged;
    }

    private void UserSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SaveUserSettings();
    }

    private async Task LoadAppSettingsAsync()
    {
        try
        {
            using HttpClient client = new HttpClient();
            string json = await client.GetStringAsync(appSettingsDownloadUrl);

            if (string.IsNullOrEmpty(json))
            {
                throw new Exception("Downloaded app settings is null or empty.");
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json);

            if (settings != null)
            {
                AppSettings = settings;
                Log.Information("App settings loaded successfully from remote repository.");
                loadedAppSettingsFromRemote = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error in LoadAppSettingsAsync: {ex}");

            // If the download fails or the downloaded JSON is null or empty, load the default settings
            if (File.Exists(deafultAppSettingsFilePath))
            {
                string json = File.ReadAllText(deafultAppSettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);

                if (settings != null)
                {
                    AppSettings = settings;
                }
                Log.Information("App settings loaded successfully from local default file.");
            }
        }
    }

    private void SaveUserSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(UserSettings);

            // Ensure the directory exists
            Directory.CreateDirectory(userSettingsDirectory);

            // Get the path to the user settings file in the local app data directory
            string userSettingsFilePath = Path.Combine(userSettingsDirectory, "UserSettings.json");

            File.WriteAllText(userSettingsFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Error in SaveSettings: {ex}");
        }
    }
}
