using Albion.Network;
using AlbionDataAvalonia.Shared;
using Serilog;
using Serilog.Core;
using System;
using System.IO;
using System.Linq;

namespace AlbionDataAvalonia.Logging;

/// <summary>
/// Writes every packet the client handles to a separate, short-lived file while diagnostics mode is on.
/// It is never saved, so the app always starts with it off.
/// </summary>
public sealed class PacketDiagnostics : IDisposable
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(2);
    private const long MaxFileBytes = 256L * 1024 * 1024;
    private const string FilePrefix = "diagnostics-";

    private readonly object sync = new();
    private Logger? logger;
    private volatile bool isEnabled;

    public PacketDiagnostics()
    {
        DeleteExpiredFiles();
    }

    public event EventHandler? IsEnabledChanged;

    public static string LogsDirectoryPath => Path.Combine(AppData.LocalPath, "logs");
    public bool IsEnabled => isEnabled;
    public string CurrentFilePath => Path.Combine(LogsDirectoryPath, $"{FilePrefix}{DateTime.Now:yyyyMMdd}.txt");

    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            if (isEnabled == enabled) return;
            if (enabled)
            {
                DeleteExpiredFiles();
                logger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .WriteTo.File(
                        Path.Combine(LogsDirectoryPath, $"{FilePrefix}.txt"),
                        rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: MaxFileBytes,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: null,
                        retainedFileTimeLimit: Retention,
                        outputTemplate: "{Timestamp:HH:mm:ss.fff} {Message:lj}{NewLine}")
                    .CreateLogger();
                isEnabled = true;
            }
            else
            {
                isEnabled = false;
                logger?.Dispose();
                logger = null;
            }
        }

        Log.Information("Diagnostics mode turned {State}", enabled ? "on" : "off");
        IsEnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Write(object packet)
    {
        var current = logger;
        if (!isEnabled || current is null) return;

        switch (packet)
        {
            case EventPacket value:
                current.Information("Event {Code} ({Name}) {Parameters}", value.EventCode,
                    Enum.GetName(typeof(EventCodes), value.EventCode) ?? "Unknown",
                    PacketDiagnosticsFormatter.FormatParameters(value.Parameters));
                break;
            case RequestPacket value:
                current.Information("Request {Code} ({Name}) {Parameters}", value.OperationCode,
                    Enum.GetName(typeof(OperationCodes), value.OperationCode) ?? "Unknown",
                    PacketDiagnosticsFormatter.FormatParameters(value.Parameters));
                break;
            case ResponsePacket value:
                current.Information("Response {Code} ({Name}) return {ReturnCode} {Parameters}", value.OperationCode,
                    Enum.GetName(typeof(OperationCodes), value.OperationCode) ?? "Unknown", value.ReturnCode,
                    PacketDiagnosticsFormatter.FormatParameters(value.Parameters));
                break;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            isEnabled = false;
            logger?.Dispose();
            logger = null;
        }
    }

    // Rolling only prunes while a file is being written, and diagnostics are usually off.
    private static void DeleteExpiredFiles()
    {
        try
        {
            if (!Directory.Exists(LogsDirectoryPath)) return;
            var cutoff = DateTime.UtcNow - Retention;
            foreach (var file in new DirectoryInfo(LogsDirectoryPath).EnumerateFiles($"{FilePrefix}*.txt")
                         .Where(file => file.LastWriteTimeUtc < cutoff))
            {
                file.Delete();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.Debug(error, "Could not remove old diagnostics files");
        }
    }
}
