using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
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

            if (latestVersion == null)
            {
                Log.Error("Failed to get the latest version from the server.");
                return;
            }

            var currentVersionObj = new Version(currentVersion);
            var latestVersionObj = new Version(latestVersion);

            if (currentVersionObj >= latestVersionObj)
            {
                Log.Information($"You are using the latest version! Yours: v.{currentVersion} | Latest: v.{latestVersion}");
                return;
            }

            downloadUrl = downloadUrl.Replace("{fileName}", fileNameFormat.Replace("{version}", latestVersion));

            Log.Information($"A new version is available: v.{latestVersion}. Updating from v.{currentVersion}");


            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
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
                        Arguments = "/VERYSILENT /SP-",
                        UseShellExecute = true
                    }
                };
                process.Start();

                // Stop the current application
                Environment.Exit(0);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Log.Warning("There's a new version available, but updating on Linux is not supported yet. Please update manually.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred while checking for updates: {ex.Message}");
        }
    }
}
