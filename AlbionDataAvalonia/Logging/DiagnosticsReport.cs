using AFMDataClient.Core;
using AlbionDataAvalonia.Auth.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Maps;
using AlbionDataAvalonia.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Players;
using AlbionDataAvalonia.ReferenceData;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.ViewModels;
using AlbionDataAvalonia.Views;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Logging;

/// <summary>
/// Builds the Copy diagnostics report: the setup checklist with capture, game, upload, data and app details and the
/// latest warnings. Players paste it in public Discord channels, so it leaves out the character, guild, alliance and
/// account, the player's own IP addresses, adapter addresses and ids, and file paths, and scrubs them from warnings.
/// </summary>
public sealed class DiagnosticsReport
{
    // Discord refuses longer messages without Nitro. The code fences count too.
    private const int MaxLength = 2000;
    private const int MaxAdapters = 4;
    private const int MaxAdapterNameLength = 45;
    private const int MaxUnknownServerIps = 4;
    private const int MaxWarningKinds = 8;
    private const int MaxWarningLineLength = 120;
    private const string Fence = "```";
    private const string Indent = "  ";
    private const string AdapterIndent = "    ";
    private static readonly TimeSpan CpuSampleTime = TimeSpan.FromSeconds(1);
    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:lj}");

    private readonly NetworkListenerService listener;
    private readonly PlayerState player;
    private readonly SettingsManager settings;
    private readonly DesktopClientCore clientCore;
    private readonly Uploader uploader;
    private readonly AuthService auth;
    private readonly ListSink logs;
    private readonly MapDataService maps;
    private readonly PlayerIdentityService identities;
    private readonly ActivityMapWindowService mapWindow;
    private readonly ActivityStreamerWindowService streamerWindow;
    private readonly PacketDiagnostics packetDiagnostics;

    public DiagnosticsReport(NetworkListenerService listener, PlayerState player, SettingsManager settings, DesktopClientCore clientCore,
        Uploader uploader, AuthService auth, ListSink logs, MapDataService maps, PlayerIdentityService identities,
        ActivityMapWindowService mapWindow, ActivityStreamerWindowService streamerWindow, PacketDiagnostics packetDiagnostics)
    {
        this.listener = listener;
        this.player = player;
        this.settings = settings;
        this.clientCore = clientCore;
        this.uploader = uploader;
        this.auth = auth;
        this.logs = logs;
        this.maps = maps;
        this.identities = identities;
        this.mapWindow = mapWindow;
        this.streamerWindow = streamerWindow;
        this.packetDiagnostics = packetDiagnostics;
    }

    /// <summary>Takes about a second: CPU use is measured over it while the running processes are checked.</summary>
    public async Task<string> BuildAsync(SetupStatusViewModel setup)
    {
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var sampleStarted = Stopwatch.GetTimestamp();
        var scanTask = Task.Run(ScanProcesses);
        await Task.Delay(CpuSampleTime);
        var scan = await scanTask;
        process.Refresh();
        var cpuPercent = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds
            / (Stopwatch.GetElapsedTime(sampleStarted).TotalMilliseconds * Environment.ProcessorCount) * 100;

        var now = DateTime.UtcNow;
        var capture = listener.GetCaptureDiagnostics();
        var text = new StringBuilder();
        text.AppendLine(Fence);
        AppendHeader(text, setup, process, now);
        text.AppendLine();
        text.AppendLine("Checklist");
        setup.AppendDiagnostics(text, Indent);
        text.AppendLine();
        AppendCapture(text, capture, scan, now);
        text.AppendLine();
        AppendGame(text, capture, now);
        text.AppendLine();
        AppendUploads(text, now);
        text.AppendLine();
        AppendData(text, now);
        text.AppendLine();
        AppendApp(text, cpuPercent);
        text.AppendLine();
        AppendWarnings(text, capture, now);
        text.Append(Fence);
        return text.ToString();
    }

    private static GameProcessScan ScanProcesses()
    {
        try
        {
            return GameProcessDetector.Scan();
        }
        catch (Exception error)
        {
            Log.Debug(error, "Could not list processes for diagnostics.");
            return new GameProcessScan(false, []);
        }
    }

    private void AppendHeader(StringBuilder text, SetupStatusViewModel setup, Process process, DateTime now)
    {
        var channel = settings.UserSettings.JoinBetaProgram ? "beta channel" : "stable channel";
        string uptime;
        try
        {
            uptime = $"up {UiFormat.Duration(DateTime.Now - process.StartTime)}";
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            uptime = "uptime unknown";
        }

        text.AppendLine(Join($"AFM Data Client {ClientUpdater.GetVersion() ?? UiText.Unknown}", channel, uptime,
            $"copied {now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC"));
        text.AppendLine(Join($"{RuntimeInformation.OSDescription.Trim()} {RuntimeInformation.OSArchitecture}",
            $"run as administrator: {YesNo(Environment.IsPrivilegedProcess)}"));
        text.AppendLine($"Status: {setup.StatusTitle}");
    }

    private void AppendCapture(StringBuilder text, CaptureDiagnostics capture, GameProcessScan scan, DateTime now)
    {
        text.AppendLine("Capture");
        var library = $"Library: {NpCapInstallationChecker.DescribeCaptureLibrary()}";
        text.AppendLine(Indent + (NpCapInstallationChecker.IsNpcapAdminOnly() is { } adminOnly
            ? Join(library, $"admin-only: {YesNo(adminOnly)}")
            : library));

        var restarts = $"restarts: {capture.RestartCount}";
        if (capture.LastRestart is { } last)
        {
            restarts += $", last {UiFormat.TimeAgo(last.Utc, now)}: {last.Reason}";
        }

        text.AppendLine(Indent + Join($"Filter: {settings.AppSettings.PacketFilterPortText}", restarts));

        var status = listener.GetCaptureStatus();
        text.AppendLine(Indent + (status.IsStarted
            ? $"Adapters: {status.AvailableDeviceCount} found, {status.ListeningDeviceCount} open"
            : "Adapters: capture not started"));
        var adapters = capture.Adapters
            .Select(adapter => (adapter, router: TrafficRouterDetector.MatchAdapter(adapter.Name)))
            .OrderBy(entry => entry.adapter.State == "selected" ? 0 : entry.router is not null ? 1 : entry.adapter.State == "open" ? 2 : 3)
            .ThenBy(entry => entry.adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var (adapter, router) in adapters.Take(MaxAdapters))
        {
            var line = Join(Shorten(adapter.Name, MaxAdapterNameLength), adapter.State);
            text.AppendLine(AdapterIndent + (router is null ? line : Join(line, router)));
        }

        if (adapters.Length > MaxAdapters)
        {
            text.AppendLine($"{AdapterIndent}… {adapters.Length - MaxAdapters} more");
        }

        text.AppendLine(Indent + $"VPNs and boosters running: {(scan.TrafficRouters.Count > 0 ? string.Join(", ", scan.TrafficRouters) : "none from the list")}");
    }

    private void AppendGame(StringBuilder text, CaptureDiagnostics capture, DateTime now)
    {
        text.AppendLine("Game");
        var lastPacket = player.LastPacketTime;
        text.AppendLine(Indent + (lastPacket == DateTime.MinValue
            ? "Traffic: none yet"
            : $"Traffic: last packet {UiFormat.Duration(now - lastPacket)} ago over {DescribeTransport(capture.Transport)}"));

        if (capture.UnknownServerIps.Count > 0)
        {
            var shown = string.Join(", ", capture.UnknownServerIps.Take(MaxUnknownServerIps));
            var more = capture.UnknownServerIps.Count > MaxUnknownServerIps ? $" and {capture.UnknownServerIps.Count - MaxUnknownServerIps} more" : "";
            text.AppendLine($"{Indent}Unknown server IPs: {shown}{more}");
        }

        // The zone id lets an unrecognized zone be added. Island ids can name their owner, so they stay out.
        var rawLocation = clientCore.Core.Session.RawLocationId;
        if (player.Location == AlbionLocations.Unknown && !string.IsNullOrWhiteSpace(rawLocation)
            && AlbionLocations.GetIslandId(rawLocation) is null)
        {
            text.AppendLine($"{Indent}Unrecognized zone: {rawLocation}");
        }

        text.AppendLine($"{Indent}Market orders: {(player.HasEncryptedData ? "encrypted" : "readable")}");
    }

    private void AppendUploads(StringBuilder text, DateTime now)
    {
        var user = settings.UserSettings;
        var publicStats = player.PublicUploadStats;
        var privateStats = player.PrivateUploadStats;
        text.AppendLine("Uploads");
        text.AppendLine(Indent + Join($"Signed in: {YesNo(auth.CurrentFirebaseUser is not null)}", $"private mode: {OnOff(user.UploadToAfmOnly)}",
            $"contribute to public: {OnOff(user.ContributeToPublic)}", $"share with friends: {OnOff(user.ShareWithFriends)}"));
        text.AppendLine(Indent + Join($"Since start: public {UiFormat.Whole(publicStats.SuccessCount)} ok, {UiFormat.Whole(publicStats.FailedCount)} failed",
            $"private {UiFormat.Whole(privateStats.SuccessCount)} ok, {UiFormat.Whole(privateStats.FailedCount)} failed"));

        var pow = player.PowSolveSampleCount > 0
            ? $"PoW: median {UiFormat.Whole(player.PowSolveTimeMedian)} ms over {UiFormat.Count(player.PowSolveSampleCount, "solve")}"
            : "PoW: none yet";
        text.AppendLine(Indent + Join($"Queue: {UiFormat.Whole(uploader.uploadQueueCount)} waiting, {UiFormat.Whole(uploader.runningTasksCount)} running",
            $"threads: {user.DesiredThreadCount}", pow));

        AppendFailure(text, "AODP", clientCore.LastPublicFailure, now);
        AppendFailure(text, "AFM", clientCore.LastPrivateFailure, now);
    }

    private static void AppendFailure(StringBuilder text, string destination, UploadFailure? failure, DateTime now)
    {
        if (failure is null) return;
        text.AppendLine($"{Indent}Last {destination} failure: {Join(failure.Kind, failure.Reason, UiFormat.TimeAgo(failure.Utc, now))}");
    }

    private void AppendData(StringBuilder text, DateTime now)
    {
        text.AppendLine("Data");
        var (mapVersion, mapFromCdn) = maps.VersionStatus;
        var mapData = mapVersion is null ? "not loaded yet" : $"{Shorten(mapVersion, 8, ellipsis: false)} ({(mapFromCdn == true ? "CDN" : "cached")})";
        text.AppendLine(Indent + Join($"App settings: {(settings.AppSettingsFromCdn ? "CDN" : "built-in defaults")}",
            $"reference data: {DescribeReferenceData()}", $"map data: {mapData}"));
        text.AppendLine(Indent + (ClientUpdater.LastCheck is { } check
            ? $"Update check: {check.Summary}, {UiFormat.TimeAgo(check.CheckedUtc, now)}"
            : "Update check: not run yet"));
    }

    private static string DescribeReferenceData()
    {
        var loads = ReferenceDataLoader.Shared.Loads;
        if (loads.Count == 0) return "not loaded yet";
        var fallbacks = loads
            .Where(load => load.Source != ReferenceDataSource.Download)
            .OrderBy(load => load.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(load => $"{load.FileName} {(load.Source == ReferenceDataSource.DiskCache ? "cached" : "built-in")}")
            .ToArray();
        return fallbacks.Length == 0 ? "CDN" : $"CDN except {string.Join(", ", fallbacks)}";
    }

    private void AppendApp(StringBuilder text, double cpuPercent)
    {
        var user = settings.UserSettings;
        text.AppendLine("App");
        text.AppendLine(Indent + Join($"CPU {cpuPercent.ToString("0.0", CultureInfo.CurrentCulture)}%",
            $"memory {UiFormat.Bytes(Environment.WorkingSet)}", $"database {DescribeDatabaseSize()}"));
        text.AppendLine(Indent + Join($"Map window: {(mapWindow.IsOpen ? "open" : "closed")}", $"streamer: {(streamerWindow.IsOpen ? "open" : "closed")}",
            $"activity recording: {OnOff(user.ActivityRecordingEnabled)}", $"diagnostics mode: {OnOff(packetDiagnostics.IsEnabled)}"));
    }

    private static string DescribeDatabaseSize()
    {
        try
        {
            var database = new FileInfo(AppData.DatabasePath);
            if (!database.Exists) return "none";
            var log = new FileInfo(AppData.DatabasePath + "-wal");
            return UiFormat.Bytes(database.Length + (log.Exists ? log.Length : 0));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return UiText.Unknown;
        }
    }

    /// <summary>The latest warnings and errors, one line per kind, while the report stays under Discord's limit.</summary>
    private void AppendWarnings(StringBuilder text, CaptureDiagnostics capture, DateTime now)
    {
        var kinds = logs.RecentProblems.ToArray()
            .GroupBy(problem => (problem.MessageTemplate.Text, problem.Exception?.GetBaseException().GetType()))
            .Select(group => (Latest: group.MaxBy(problem => problem.Timestamp)!, Count: group.Count()))
            .OrderByDescending(kind => kind.Latest.Timestamp)
            .ToArray();
        if (kinds.Length == 0)
        {
            text.AppendLine("Recent warnings and errors: none");
            return;
        }

        text.AppendLine("Recent warnings and errors");
        var scrubber = CreateScrubber(capture);
        var shown = 0;
        foreach (var (latest, count) in kinds.Take(MaxWarningKinds))
        {
            var prefix = $"{Indent}×{count} {UiFormat.TimeAgo(latest.Timestamp.UtcDateTime, now)}: ";
            var line = Shorten(prefix + scrubber.Scrub(RenderProblem(latest)), MaxWarningLineLength);
            var reserve = Environment.NewLine.Length + Fence.Length + 40;
            if (text.Length + line.Length + reserve > MaxLength) break;
            text.AppendLine(line);
            shown++;
        }

        if (shown < kinds.Length)
        {
            text.AppendLine($"{Indent}… {UiFormat.Count(kinds.Length - shown, "more kind")} on the Logs page");
        }
    }

    private DiagnosticsScrubber CreateScrubber(CaptureDiagnostics capture)
    {
        var names = new List<(string Name, string Replacement)> { (Environment.MachineName, "<pc>") };
        var character = player.PlayerName;
        if (!string.IsNullOrWhiteSpace(character))
        {
            names.Add((character, "<character>"));
            if (identities.TryGetByName(player.AlbionServer?.Id, character, out var identity))
            {
                names.Add((identity.GuildName, "<guild>"));
                names.Add((identity.AllianceName, "<alliance>"));
            }
        }

        return new DiagnosticsScrubber(names, capture.UnknownServerIps);
    }

    private static string RenderProblem(LogEvent problem)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        MessageFormatter.Format(problem, writer);
        var message = writer.ToString();
        if (problem.Exception?.GetBaseException() is { } cause)
        {
            message += $" ({cause.GetType().Name}: {cause.Message})";
        }

        // One line each, and nothing that would close the code block early.
        return string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Replace(Fence, "'''");
    }

    private static string DescribeTransport(GameTrafficTransport transport) => transport switch
    {
        GameTrafficTransport.IPv4 => "IPv4",
        GameTrafficTransport.IPv6Nat64 => "IPv6 (NAT64)",
        GameTrafficTransport.IPv4MappedIPv6 => "IPv4-mapped IPv6",
        GameTrafficTransport.IPv6 => "IPv6",
        _ => UiText.Unknown
    };

    private static string Shorten(string value, int maxLength, bool ellipsis = true) =>
        value.Length <= maxLength ? value : ellipsis ? value[..(maxLength - 1)] + "…" : value[..maxLength];

    private static string Join(params string[] parts) => string.Join(UiText.Separator, parts);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string OnOff(bool value) => value ? "on" : "off";
}
