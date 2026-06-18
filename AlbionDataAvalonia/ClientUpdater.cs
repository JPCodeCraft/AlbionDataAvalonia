using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia;

public static class ClientUpdater
{
    public const string StableChannel = "stable";
    public const string BetaChannel = "beta";

    private static readonly HttpClient httpClient = new HttpClient();

    public static string? GetVersion()
    {
        return Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString();
    }

    public static async Task<ClientUpdateCheckResult> CheckForUpdatesAsync(
        string? versionUrl,
        string? legacyDownloadUrl,
        string? fileNameFormat,
        bool includeBeta)
    {
        Log.Information("Checking for updates...");

        var currentVersion = GetVersion();

        if (currentVersion == null)
        {
            Log.Error("Failed to get the current version of the application.");
            return ClientUpdateCheckResult.NoUpdate("unknown");
        }

        if (string.IsNullOrWhiteSpace(versionUrl))
        {
            Log.Error("Version URL is not set.");
            return ClientUpdateCheckResult.NoUpdate(currentVersion);
        }

        try
        {
            var currentVersionObj = new Version(currentVersion);
            var response = await httpClient.GetStringAsync(versionUrl);
            using var jsonDocument = JsonDocument.Parse(response);
            var candidates = ReadUpdateCandidates(
                jsonDocument.RootElement,
                legacyDownloadUrl,
                fileNameFormat,
                includeBeta);

            if (candidates.Count == 0)
            {
                Log.Error("Failed to get any valid update versions from the server.");
                return ClientUpdateCheckResult.NoUpdate(currentVersion);
            }

            var latestEligibleUpdate = candidates
                .Where(candidate => candidate.ParsedVersion > currentVersionObj)
                .OrderByDescending(candidate => candidate.ParsedVersion)
                .FirstOrDefault();

            if (latestEligibleUpdate == null)
            {
                var latestEligibleVersion = candidates
                    .OrderByDescending(candidate => candidate.ParsedVersion)
                    .First();

                Log.Information(
                    "You are using the latest eligible version. Yours: v.{CurrentVersion} | Latest eligible: v.{LatestVersion} ({Channel})",
                    currentVersion,
                    latestEligibleVersion.Version,
                    latestEligibleVersion.Channel);
                return ClientUpdateCheckResult.NoUpdate(currentVersion);
            }

            Log.Information(
                "A new {Channel} version is available: v.{LatestVersion}. Updating from v.{CurrentVersion}",
                latestEligibleUpdate.Channel,
                latestEligibleUpdate.Version,
                currentVersion);

            return new ClientUpdateCheckResult(currentVersion, latestEligibleUpdate);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while checking for updates.");
            return ClientUpdateCheckResult.NoUpdate(currentVersion);
        }
    }

    public static async Task InstallUpdateAsync(ClientUpdateInfo update)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Log.Warning(
                "There's a new {Channel} version available, but automatic updates are only supported on Windows. Please update manually.",
                update.Channel);
            return;
        }

        if (string.IsNullOrWhiteSpace(update.WindowsDownloadUrl))
        {
            Log.Error(
                "There's a new {Channel} version available, but no Windows download URL is configured. Version: {Version}",
                update.Channel,
                update.Version);
            return;
        }

        try
        {
            Log.Information("Downloading the new version from {DownloadUrl}", update.WindowsDownloadUrl);
            var data = await httpClient.GetByteArrayAsync(update.WindowsDownloadUrl);
            var filePath = Path.Combine(Path.GetTempPath(), $"AFMDataClientSetup_v_{update.Version}.exe");
            await File.WriteAllBytesAsync(filePath, data);

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
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while installing update v.{Version}.", update.Version);
        }
    }

    private static IReadOnlyList<ClientUpdateInfo> ReadUpdateCandidates(
        JsonElement root,
        string? legacyDownloadUrl,
        string? fileNameFormat,
        bool includeBeta)
    {
        var candidates = new List<ClientUpdateInfo>();

        if (root.TryGetProperty(StableChannel, out var stableElement)
            && stableElement.ValueKind == JsonValueKind.Object)
        {
            TryAddCandidate(
                candidates,
                stableElement,
                StableChannel,
                legacyDownloadUrl,
                fileNameFormat,
                allowLegacyDownloadUrl: true);
        }

        if (!candidates.Any(candidate => string.Equals(candidate.Channel, StableChannel, StringComparison.OrdinalIgnoreCase))
            && TryGetStringProperty(root, "version", out var legacyVersion))
        {
            TryAddCandidate(
                candidates,
                legacyVersion,
                StableChannel,
                BuildLegacyWindowsDownloadUrl(legacyVersion, legacyDownloadUrl, fileNameFormat),
                BuildReleasePageUrl(legacyVersion, StableChannel));
        }

        if (includeBeta
            && root.TryGetProperty(BetaChannel, out var betaElement)
            && betaElement.ValueKind == JsonValueKind.Object)
        {
            TryAddCandidate(
                candidates,
                betaElement,
                BetaChannel,
                legacyDownloadUrl,
                fileNameFormat,
                allowLegacyDownloadUrl: false);
        }

        return candidates;
    }

    private static void TryAddCandidate(
        List<ClientUpdateInfo> candidates,
        JsonElement element,
        string channel,
        string? legacyDownloadUrl,
        string? fileNameFormat,
        bool allowLegacyDownloadUrl)
    {
        if (!TryGetStringProperty(element, "version", out var versionText))
        {
            Log.Warning("Skipping {Channel} update entry because version is not set.", channel);
            return;
        }

        var windowsDownloadUrl = TryGetStringProperty(element, "windowsDownloadUrl", out var configuredWindowsDownloadUrl)
            ? configuredWindowsDownloadUrl
            : null;

        if (string.IsNullOrWhiteSpace(windowsDownloadUrl) && allowLegacyDownloadUrl)
        {
            windowsDownloadUrl = BuildLegacyWindowsDownloadUrl(versionText, legacyDownloadUrl, fileNameFormat);
        }

        var releasePageUrl = TryGetStringProperty(element, "releasePageUrl", out var configuredReleasePageUrl)
            ? configuredReleasePageUrl
            : BuildReleasePageUrl(versionText, channel);

        TryAddCandidate(candidates, versionText, channel, windowsDownloadUrl, releasePageUrl);
    }

    private static void TryAddCandidate(
        List<ClientUpdateInfo> candidates,
        string versionText,
        string channel,
        string? windowsDownloadUrl,
        string? releasePageUrl)
    {
        if (!Version.TryParse(versionText, out var parsedVersion))
        {
            Log.Warning(
                "Skipping {Channel} update entry because version {Version} is not a valid numeric version.",
                channel,
                versionText);
            return;
        }

        candidates.Add(new ClientUpdateInfo(
            versionText,
            parsedVersion,
            channel,
            windowsDownloadUrl,
            releasePageUrl));
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var propertyValue = property.GetString();
        if (string.IsNullOrWhiteSpace(propertyValue))
        {
            return false;
        }

        value = propertyValue;
        return true;
    }

    private static string? BuildLegacyWindowsDownloadUrl(string version, string? legacyDownloadUrl, string? fileNameFormat)
    {
        if (string.IsNullOrWhiteSpace(legacyDownloadUrl) || string.IsNullOrWhiteSpace(fileNameFormat))
        {
            return null;
        }

        return legacyDownloadUrl.Replace("{fileName}", fileNameFormat.Replace("{version}", version));
    }

    private static string BuildReleasePageUrl(string version, string channel)
    {
        var tag = string.Equals(channel, BetaChannel, StringComparison.OrdinalIgnoreCase)
            ? $"v.{version}-beta"
            : $"v.{version}";

        return $"https://github.com/JPCodeCraft/AlbionDataAvalonia/releases/tag/{tag}";
    }
}

public sealed record ClientUpdateCheckResult(string CurrentVersion, ClientUpdateInfo? Update)
{
    public bool UpdateAvailable => Update is not null;

    public static ClientUpdateCheckResult NoUpdate(string currentVersion)
    {
        return new ClientUpdateCheckResult(currentVersion, null);
    }
}

public sealed record ClientUpdateInfo(
    string Version,
    Version ParsedVersion,
    string Channel,
    string? WindowsDownloadUrl,
    string? ReleasePageUrl)
{
    public bool IsBeta => string.Equals(Channel, ClientUpdater.BetaChannel, StringComparison.OrdinalIgnoreCase);
    public string ChannelLabel => IsBeta ? "Beta" : "Stable";
}
