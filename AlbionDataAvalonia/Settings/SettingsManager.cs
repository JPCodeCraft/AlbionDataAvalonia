using Serilog;
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Settings;

public class SettingsManager
{
    private string deafultUserSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "DefaultUserSettings.json");
    private string defaultAppSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "DefaultAppSettings.json");

    private bool loadedAppSettingsFromRemote = false;

    private string appSettingsDownloadUrl = "https://raw.githubusercontent.com/JPCodeCraft/AlbionDataAvalonia/master/AlbionDataAvalonia/DefaultAppSettings.json";
    public UserSettings UserSettings { get; } = new();
    public AppSettings AppSettings { get; private set; }
    public SettingsManager()
    {
    }

    public async Task InitializeSettings()
    {
        LoadUserSettings();

        //if (!await TryLoadAppSettingsFromRemoteAsync())
        //{
        LoadAppSettingsFromLocal();
        //    _ = KeepTryingToLoadAppSettingsFromRemoteAsync();
        //}
    }


    private void LoadUserSettings()
    {
        // Get the path to the user settings file in the local app data directory
        string localUserSettingsFilePath = Path.Combine(AppData.LocalPath, "UserSettings.json");

        // If the user settings file doesn't exist in the local app data directory, use the default settings file
        if (!File.Exists(localUserSettingsFilePath))
        {
            localUserSettingsFilePath = deafultUserSettingsFilePath;

            // If we are in development mode, use the default settings file
#if DEBUG
            localUserSettingsFilePath = deafultUserSettingsFilePath;
#endif

            string json = File.ReadAllText(localUserSettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);

            if (settings != null)
            {
                UserSettings.UpdateFrom(settings);
                SaveUserSettings(); // Save the default settings to a new user settings file
            }
        }
        else
        {
            string json = File.ReadAllText(localUserSettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);

            if (settings != null)
            {
                UserSettings.UpdateFrom(settings);
            }
        }

        UserSettings.PropertyChanged += UserSettings_PropertyChanged;
    }

    private void UserSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SaveUserSettings();
    }

    private async Task<bool> TryLoadAppSettingsFromRemoteAsync()
    {
        // If we are in development mode, use the default settings file
#if DEBUG
        return false;
#endif

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
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error in LoadAppSettingsAsync: {ex}");
        }

        return false;
    }

    private void LoadAppSettingsFromLocal()
    {
        try
        {
            if (File.Exists(defaultAppSettingsFilePath))
            {
                string json = File.ReadAllText(defaultAppSettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);

                if (settings != null)
                {
                    AppSettings = settings;
                }
                else
                {
                    throw new Exception("Failed to load app settings from local default file.");
                }
                Log.Information("App settings loaded successfully from local default file.");
            }
            else
            {
                throw new Exception("Local default app settings file not found.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error in LoadAppSettingsFromLocal: {ex}");
        }
    }

    private async Task KeepTryingToLoadAppSettingsFromRemoteAsync()
    {
        while (!loadedAppSettingsFromRemote)
        {
            await Task.Delay(TimeSpan.FromMinutes(AppSettings.AppSettingsRetryLoadIntervalMins));
            await TryLoadAppSettingsFromRemoteAsync();
        }
    }

    private void SaveUserSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(UserSettings);

            // Ensure the directory exists
            Directory.CreateDirectory(AppData.LocalPath);

            // Get the path to the user settings file in the local app data directory
            string userSettingsFilePath = Path.Combine(AppData.LocalPath, "UserSettings.json");

            File.WriteAllText(userSettingsFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error($"Error in SaveSettings: {ex}");
        }
    }
}
