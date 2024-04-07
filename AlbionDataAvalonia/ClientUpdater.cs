using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia;

public static class ClientUpdater
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static string? GetVersion()
    {
        return Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString();
    }

    public static async Task CheckForUpdatesAsync(string? versionUrl, string? downloadUrl, string? fileNameFormat)
    {
        Log.Information("Checking for updates...");

        if (string.IsNullOrWhiteSpace(versionUrl) || string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrEmpty(fileNameFormat))
        {
            Log.Error("Version URL or Download URL or File Name Format is not set.");
            return;
        }

        try
        {

            // Get the current version of the running application
            var currentVersion = GetVersion();

            if (currentVersion == null)
            {
                Log.Error("Failed to get the current version of the application.");
                return;
            }


            var response = await httpClient.GetStringAsync(versionUrl);
            var jsonDocument = JsonDocument.Parse(response);
            var latestVersion = jsonDocument.RootElement.GetProperty("version").GetString();

            downloadUrl = downloadUrl.Replace("{fileName}", fileNameFormat.Replace("{version}", latestVersion));
            //currentVersion = "0.0.0.0";
            if (string.Compare(currentVersion, latestVersion) < 0)
            {
                Log.Information($"A new version is available: v.{latestVersion}. Updating from v.{currentVersion}");

                // Download the new version
                Log.Information($"Downloading the new version from {downloadUrl}");
                var data = await httpClient.GetByteArrayAsync(downloadUrl);
                var filePath = Path.Combine(Path.GetTempPath(), $"AFMDataClientSetup_v_{latestVersion}.exe");
                await File.WriteAllBytesAsync(filePath, data);

                // Start the new version
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    }
                };
                process.Start();

                // Stop the current application
                Environment.Exit(0);
            }
            else
            {
                Log.Information($"You are using the latest version: v.{latestVersion}.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred while checking for updates: {ex.Message}");
        }
    }
}
