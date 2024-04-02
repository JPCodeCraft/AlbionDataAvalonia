using Serilog;
using System;
using System.IO;
using System.Text.Json;

namespace AlbionDataAvalonia.Settings;

public class SettingsManager
{
    private string userSettingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "UserSettings.json");
    public UserSettings UserSettings { get; private set; } = new UserSettings();

    public SettingsManager()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (File.Exists(userSettingsFilePath))
        {
            string json = File.ReadAllText(userSettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json);

            if (settings != null)
            {
                UserSettings = settings;
            }
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
