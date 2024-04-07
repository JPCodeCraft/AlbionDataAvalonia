using Serilog;
using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia;

public static class ClientUpdater
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task CheckForUpdatesAsync(string versionUrl, string downloadUrl)
    {
        try
        {
            // Get the current version of the running application
            var currentVersion = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString();

            if (currentVersion == null)
            {
                Log.Error("Failed to get the current version of the application.");
                return;
            }

            var response = await httpClient.GetStringAsync(versionUrl);
            var jsonDocument = JsonDocument.Parse(response);
            var latestVersion = jsonDocument.RootElement.GetProperty("version").GetString();

            if (string.Compare(currentVersion, latestVersion) < 0)
            {
                Log.Information("A new version is available.");

            }
            else
            {
                Log.Information("You are using the latest version.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred while checking for updates: {ex.Message}");
        }
    }
}
