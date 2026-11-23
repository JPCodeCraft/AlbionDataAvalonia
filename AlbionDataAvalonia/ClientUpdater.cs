using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia;

public static class ClientUpdater
{
    public const string StableChannel = "stable";
    public const string BetaChannel = "beta";

    // The installer's progress window shows when the player clicks Update now or opens the client, so the pause reads as an update.
    private const string VisibleInstallerArguments = "/SILENT /SP-";
    private const string HiddenInstallerArguments = "/VERYSILENT /SP-";

    private static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
    private static readonly SemaphoreSlim prepareGate = new(1, 1);

    private static string UpdatesDirectory => Path.Combine(AppData.LocalPath, "updates");
    private static string ReadyUpdateFilePath => Path.Combine(UpdatesDirectory, "ready-update.json");

    /// <summary>The outcome of the latest update check, for Copy diagnostics.</summary>
    public static ClientUpdateCheckOutcome? LastCheck { get; private set; }

    /// <summary>An update downloaded in the background (Windows), waiting for Update now or the next start.</summary>
    public static DownloadedClientUpdate? ReadyUpdate { get; private set; }

    /// <summary>Raised on any thread when <see cref="ReadyUpdate"/> changes.</summary>
    public static event Action? ReadyUpdateChanged;

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
                LastCheck = new ClientUpdateCheckOutcome("no valid versions in the version file", DateTime.UtcNow);
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
                LastCheck = new ClientUpdateCheckOutcome("up to date", DateTime.UtcNow);
                return new ClientUpdateCheckResult(currentVersion, null, Checked: true);
            }

            Log.Information(
                "A new {Channel} version is available: v.{LatestVersion}. Updating from v.{CurrentVersion}",
                latestEligibleUpdate.Channel,
                latestEligibleUpdate.Version,
                currentVersion);

            LastCheck = new ClientUpdateCheckOutcome($"{latestEligibleUpdate.ChannelLabel.ToLowerInvariant()} {latestEligibleUpdate.Version} available", DateTime.UtcNow);
            return new ClientUpdateCheckResult(currentVersion, latestEligibleUpdate, Checked: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while checking for updates.");
            LastCheck = new ClientUpdateCheckOutcome("failed", DateTime.UtcNow);
            return ClientUpdateCheckResult.NoUpdate(currentVersion);
        }
    }

    /// <summary>
    /// Downloads the installer and runs it at once. Only for a start that cannot continue, when the local database
    /// belongs to a newer version; updates found while the client runs wait for the player (see <see cref="PrepareUpdateAsync"/>).
    /// </summary>
    public static async Task InstallUpdateAsync(ClientUpdateInfo update)
    {
        if (!CanInstallOnThisPlatform(update)) return;

        string installerPath;
        try
        {
            installerPath = await DownloadInstallerAsync(update).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while downloading update v.{Version}.", update.Version);
            return;
        }

        await RunInstallerAndExitAsync(installerPath, update.Version, HiddenInstallerArguments).ConfigureAwait(false);
    }

    /// <summary>
    /// Windows: downloads an available update in the background and keeps it until the player clicks Update now or
    /// the client starts again. Nothing installs while the client runs. A downloaded update the version file no longer
    /// offers is dropped.
    /// </summary>
    public static async Task PrepareUpdateAsync(ClientUpdateCheckResult result)
    {
        if (!OperatingSystem.IsWindows() || !result.Checked) return;
        // A slow download can outlast the hourly check; the next check finds the update again.
        if (!await prepareGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (result.Update is not { } update)
            {
                if (ReadyUpdate is { } withdrawn) DiscardReadyUpdate($"v.{withdrawn.Version} is no longer offered");
                return;
            }

            if (ReadyUpdate is { } ready
                && string.Equals(ready.Version, update.Version, StringComparison.Ordinal)
                && File.Exists(ready.InstallerPath))
            {
                return;
            }

            if (!CanInstallOnThisPlatform(update)) return;

            var installerPath = await DownloadInstallerAsync(update).ConfigureAwait(false);
            var downloaded = new DownloadedClientUpdate(update.Version, update.Channel, installerPath, DateTime.UtcNow);
            SaveReadyUpdate(downloaded);
            DeleteUpdateFiles(keep: installerPath);
            SetReadyUpdate(downloaded);
            Log.Information(
                "Update v.{Version} ({Channel}) downloaded. It installs when the player clicks Update now or the next time the client starts.",
                update.Version,
                update.Channel);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while downloading update v.{Version}.", result.Update?.Version);
        }
        finally
        {
            prepareGate.Release();
        }
    }

    /// <summary>Drops a downloaded beta when the player moves to the stable channel.</summary>
    public static void OnUpdateChannelChanged(bool includeBeta)
    {
        if (!includeBeta && ReadyUpdate is { IsBeta: true } beta)
        {
            DiscardReadyUpdate($"the player left the beta channel before v.{beta.Version} installed");
        }
    }

    /// <summary>
    /// Runs first at start, before the database or capture open: installs an update downloaded in an earlier session.
    /// Returns true when the installer started; the caller then exits, and the installer opens the new version.
    /// </summary>
    public static bool TryStartDownloadedUpdate()
    {
        if (!OperatingSystem.IsWindows()) return false;

        var ready = LoadReadyUpdate();
        if (ready is null)
        {
            // An installer the database recovery downloaded, or one a crash left half saved.
            DeleteUpdateFiles();
            return false;
        }

        var currentVersion = GetVersion();
        if (currentVersion is null
            || !Version.TryParse(currentVersion, out var current)
            || !Version.TryParse(ready.Version, out var readyVersion))
        {
            Log.Warning("Could not compare the downloaded update v.{Version} with this version; removing it.", ready.Version);
            DeleteUpdateFiles();
            return false;
        }

        if (readyVersion <= current)
        {
            Log.Information("Update v.{Version} is installed; removing its installer.", ready.Version);
            DeleteUpdateFiles();
            return false;
        }

        if (ready.InstallStarted)
        {
            // The installer ran or was offered, yet this is still the old version: don't try it again at every start.
            Log.Warning(
                "The installer for v.{Version} ran, but this is still v.{CurrentVersion}. The update downloads again on the next check.",
                ready.Version,
                currentVersion);
            DeleteUpdateFiles();
            return false;
        }

        if (!File.Exists(ready.InstallerPath))
        {
            Log.Warning("The downloaded installer for v.{Version} is missing. The update downloads again on the next check.", ready.Version);
            DeleteUpdateFiles();
            return false;
        }

        try
        {
            SaveReadyUpdate(ready with { InstallStarted = true });
            Log.Information("Installing update v.{Version}, downloaded while v.{CurrentVersion} was running.", ready.Version, currentVersion);
            StartInstaller(ready.InstallerPath, VisibleInstallerArguments);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not start the installer for v.{Version}. Starting v.{CurrentVersion} instead.", ready.Version, currentVersion);
            DeleteUpdateFiles();
            return false;
        }
    }

    /// <summary>Update now: closes the client and runs the downloaded installer, which opens the new version when it finishes.</summary>
    public static async Task InstallReadyUpdateAsync()
    {
        if (ReadyUpdate is not { } ready) return;

        if (!File.Exists(ready.InstallerPath))
        {
            DiscardReadyUpdate($"the installer for v.{ready.Version} is missing");
            return;
        }

        Log.Information("The player chose to install update v.{Version} now.", ready.Version);
        SaveReadyUpdate(ready with { InstallStarted = true });
        await RunInstallerAndExitAsync(ready.InstallerPath, ready.Version, VisibleInstallerArguments).ConfigureAwait(false);
    }

    private static bool CanInstallOnThisPlatform(ClientUpdateInfo update)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Log.Warning(
                "There's a new {Channel} version available, but automatic updates are only supported on Windows. Please update manually.",
                update.Channel);
            return false;
        }

        if (string.IsNullOrWhiteSpace(update.WindowsDownloadUrl))
        {
            Log.Error(
                "There's a new {Channel} version available, but no Windows download URL is configured. Version: {Version}",
                update.Channel,
                update.Version);
            return false;
        }

        return true;
    }

    private static async Task<string> DownloadInstallerAsync(ClientUpdateInfo update)
    {
        Directory.CreateDirectory(UpdatesDirectory);
        var installerPath = Path.Combine(UpdatesDirectory, $"AFMDataClientSetup_v_{update.Version}.exe");
        var partialPath = installerPath + ".part";

        Log.Information("Downloading the new version from {DownloadUrl}", update.WindowsDownloadUrl);
        try
        {
            using (var response = await httpClient.GetAsync(update.WindowsDownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var expectedLength = response.Content.Headers.ContentLength;
                await using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                await using (var target = File.Create(partialPath))
                {
                    await source.CopyToAsync(target).ConfigureAwait(false);
                }

                var length = new FileInfo(partialPath).Length;
                if (expectedLength is { } expected && expected != length)
                    throw new IOException($"The download stopped early: {length} of {expected} bytes.");
                if (!StartsLikeWindowsProgram(partialPath))
                    throw new IOException("The download is not a Windows installer.");
            }

            File.Move(partialPath, installerPath, overwrite: true);
            return installerPath;
        }
        catch
        {
            try
            {
                File.Delete(partialPath);
            }
            catch (Exception deleteError)
            {
                Log.Warning(deleteError, "Could not delete the partial download {File}.", partialPath);
            }

            throw;
        }
    }

    // An error page served in place of the installer would fail only when the player clicks Update now.
    private static bool StartsLikeWindowsProgram(string path)
    {
        using var file = File.OpenRead(path);
        return file.ReadByte() == 'M' && file.ReadByte() == 'Z';
    }

    private static void StartInstaller(string installerPath, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = arguments,
                UseShellExecute = true
            }
        };

        if (!process.Start())
            throw new InvalidOperationException("The update installer did not start.");
    }

    private static async Task RunInstallerAndExitAsync(string installerPath, string version, string arguments)
    {
        var shutdownStarted = false;
        try
        {
            shutdownStarted = true;
            await App.ShutdownAsync().ConfigureAwait(false);
            StartInstaller(installerPath, arguments);
            Log.CloseAndFlush();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while installing update v.{Version}.", version);
            if (shutdownStarted)
            {
                try
                {
                    await App.ShowUpdateInstallationFailureAsync(version).ConfigureAwait(false);
                }
                catch (Exception dialogException)
                {
                    Log.Error(dialogException, "Could not show the update failure message. The stopped application will exit.");
                }
                finally
                {
                    Environment.Exit(1);
                }
            }
        }
    }

    private static void SetReadyUpdate(DownloadedClientUpdate? update)
    {
        ReadyUpdate = update;
        ReadyUpdateChanged?.Invoke();
    }

    private static void DiscardReadyUpdate(string reason)
    {
        Log.Information("Removing the downloaded update: {Reason}.", reason);
        DeleteUpdateFiles();
        SetReadyUpdate(null);
    }

    private static DownloadedClientUpdate? LoadReadyUpdate()
    {
        try
        {
            if (!File.Exists(ReadyUpdateFilePath)) return null;
            return JsonSerializer.Deserialize<DownloadedClientUpdate>(File.ReadAllText(ReadyUpdateFilePath));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the downloaded update; removing it.");
            DeleteUpdateFiles();
            return null;
        }
    }

    private static void SaveReadyUpdate(DownloadedClientUpdate update)
    {
        Directory.CreateDirectory(UpdatesDirectory);
        var temporaryPath = ReadyUpdateFilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(update));
        File.Move(temporaryPath, ReadyUpdateFilePath, overwrite: true);
    }

    private static void DeleteUpdateFiles(string? keep = null)
    {
        if (!Directory.Exists(UpdatesDirectory)) return;

        foreach (var file in Directory.EnumerateFiles(UpdatesDirectory))
        {
            if (string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)
                || (keep is not null && string.Equals(file, ReadyUpdateFilePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not delete the old update file {File}.", file);
            }
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

public sealed record ClientUpdateCheckOutcome(string Summary, DateTime CheckedUtc);

/// <param name="Checked">True when the version file was read and understood, so a missing update means none is offered.</param>
public sealed record ClientUpdateCheckResult(string CurrentVersion, ClientUpdateInfo? Update, bool Checked = false)
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

/// <summary>A downloaded installer, saved in the updates folder so the next start can install it.</summary>
/// <param name="InstallStarted">Set just before the installer runs, so a failed install isn't retried at every start.</param>
public sealed record DownloadedClientUpdate(
    string Version,
    string Channel,
    string InstallerPath,
    DateTime DownloadedUtc,
    bool InstallStarted = false)
{
    [JsonIgnore]
    public bool IsBeta => string.Equals(Channel, ClientUpdater.BetaChannel, StringComparison.OrdinalIgnoreCase);
}
