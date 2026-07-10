using Microsoft.Data.Sqlite;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.DB;

public sealed class DatabaseBackupService : IDisposable
{
    private const string BackupFilePrefix = "afmdataclient-backup-";
    private const string BackupTimestampFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const string DatabaseArchiveEntryName = "afmdataclient.db";
    private static readonly TimeSpan BackupInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan BackupRetryInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan[] RetentionTargetAges =
    [
        TimeSpan.FromDays(5),
        TimeSpan.FromDays(10),
        TimeSpan.FromDays(15)
    ];
    private static readonly Regex BackupFileNameRegex = new(
        $"^{Regex.Escape(BackupFilePrefix)}(?<timestamp>\\d{{8}}T\\d{{6}}Z)\\.zip$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SemaphoreSlim backupLock = new(1, 1);
    private readonly object lifecycleLock = new();
    private CancellationTokenSource? cancellationTokenSource;
    private Task? backgroundTask;
    private bool disposed;

    public string BackupDirectoryPath => AppData.BackupDirectoryPath;

    public void Start()
    {
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (backgroundTask != null)
            {
                return;
            }

            cancellationTokenSource = new CancellationTokenSource();
            backgroundTask = RunAsync(cancellationTokenSource.Token);
            Log.Debug(
                "Started automatic SQLite backup service. DatabasePath={DatabasePath}, BackupDirectoryPath={BackupDirectoryPath}, BackupIntervalHours={BackupIntervalHours}, FailureRetryIntervalHours={FailureRetryIntervalHours}",
                AppData.DatabasePath,
                BackupDirectoryPath,
                BackupInterval.TotalHours,
                BackupRetryInterval.TotalHours);
        }
    }

    public async Task<bool> CreateBackupIfDueAsync(CancellationToken cancellationToken = default)
    {
        if (!await backupLock.WaitAsync(0, cancellationToken))
        {
            Log.Debug("Skipped automatic SQLite backup check because another backup operation is running.");
            return false;
        }

        try
        {
            Directory.CreateDirectory(BackupDirectoryPath);
            CleanupStaleTemporaryFiles();

            var nowUtc = DateTimeOffset.UtcNow;
            var existingBackups = GetCompletedBackups();
            var latestBackup = existingBackups.MaxBy(backup => backup.TimestampUtc);
            Log.Debug(
                "Checking whether an automatic SQLite backup is due. CompletedBackupCount={CompletedBackupCount}, LatestBackupUtc={LatestBackupUtc}",
                existingBackups.Count,
                latestBackup?.TimestampUtc);
            if (latestBackup != null && nowUtc - latestBackup.TimestampUtc < BackupInterval)
            {
                Log.Debug(
                    "Automatic SQLite backup is not due. NextBackupDueUtc={NextBackupDueUtc}",
                    latestBackup.TimestampUtc + BackupInterval);
                return false;
            }

            Log.Debug("Automatic SQLite backup is due; starting snapshot creation.");
            await Task.Run(() => CreateBackup(nowUtc, cancellationToken), cancellationToken);
            ApplyRetention(nowUtc);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create automatic SQLite backup.");
            return false;
        }
        finally
        {
            backupLock.Release();
        }
    }

    public void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(BackupDirectoryPath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = BackupDirectoryPath,
                    UseShellExecute = true
                });
                Log.Debug("Opened SQLite backup folder. Path={BackupDirectoryPath}", BackupDirectoryPath);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                StartFolderProcess("xdg-open");
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                StartFolderProcess("open");
                return;
            }

            Log.Warning("Opening the backup folder is not supported on this operating system. Path={BackupDirectoryPath}", BackupDirectoryPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open SQLite backup folder. Path={BackupDirectoryPath}", BackupDirectoryPath);
        }
    }

    public void Dispose()
    {
        lock (lifecycleLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellationTokenSource?.Cancel();
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            Log.Debug("Stopped automatic SQLite backup service.");
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await CreateBackupIfDueAsync(cancellationToken);

                var nowUtc = DateTimeOffset.UtcNow;
                DateTimeOffset? nextBackupDueUtc = null;
                var delay = BackupRetryInterval;
                try
                {
                    var latestBackup = GetCompletedBackups().MaxBy(backup => backup.TimestampUtc);
                    nextBackupDueUtc = latestBackup?.TimestampUtc + BackupInterval;
                    if (nextBackupDueUtc.HasValue && nextBackupDueUtc.Value > nowUtc)
                    {
                        delay = nextBackupDueUtc.Value - nowUtc;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to determine the next automatic SQLite backup due time; using the retry interval.");
                }

                Log.Debug(
                    "Scheduled next automatic SQLite backup attempt. BackupDueUtc={BackupDueUtc}, NextAttemptUtc={NextAttemptUtc}, DelayHours={DelayHours}",
                    nextBackupDueUtc,
                    nowUtc + delay,
                    Math.Round(delay.TotalHours, 2));
                await Task.Delay(delay, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void CreateBackup(DateTimeOffset timestampUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        var timestampText = timestampUtc.ToString(BackupTimestampFormat, CultureInfo.InvariantCulture);
        var finalBackupPath = Path.Combine(AppData.BackupDirectoryPath, $"{BackupFilePrefix}{timestampText}.zip");
        var temporaryId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var temporaryDatabasePath = Path.Combine(AppData.BackupDirectoryPath, $"{BackupFilePrefix}{timestampText}-{temporaryId}.db.tmp");
        var temporaryArchivePath = Path.Combine(AppData.BackupDirectoryPath, $"{BackupFilePrefix}{timestampText}-{temporaryId}.zip.tmp");

        try
        {
            Log.Debug("Creating consistent SQLite snapshot. TemporaryDatabasePath={TemporaryDatabasePath}", temporaryDatabasePath);
            BackupAndSanitizeDatabase(temporaryDatabasePath);
            cancellationToken.ThrowIfCancellationRequested();
            var uncompressedSize = new FileInfo(temporaryDatabasePath).Length;

            using (var archive = ZipFile.Open(temporaryArchivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(temporaryDatabasePath, DatabaseArchiveEntryName, CompressionLevel.Optimal);
            }
            Log.Debug(
                "Compressed sanitized SQLite snapshot. UncompressedSizeBytes={UncompressedSizeBytes}, TemporaryArchivePath={TemporaryArchivePath}",
                uncompressedSize,
                temporaryArchivePath);

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryArchivePath, finalBackupPath);

            var finalSize = new FileInfo(finalBackupPath).Length;
            stopwatch.Stop();
            Log.Information(
                "Created automatic SQLite backup successfully. Path={BackupPath}, UncompressedSizeBytes={UncompressedSizeBytes}, CompressedSizeBytes={CompressedSizeBytes}, ElapsedMilliseconds={ElapsedMilliseconds}",
                finalBackupPath,
                uncompressedSize,
                finalSize,
                stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            DeleteTemporaryDatabaseArtifacts(temporaryDatabasePath);
            DeleteTemporaryFile(temporaryArchivePath);
        }
    }

    private static void BackupAndSanitizeDatabase(string destinationPath)
    {
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = AppData.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var sourceConnection = new SqliteConnection(sourceConnectionString);
        using var destinationConnection = new SqliteConnection(destinationConnectionString);
        sourceConnection.Open();
        destinationConnection.Open();
        sourceConnection.BackupDatabase(destinationConnection);
        Log.Debug("Copied live SQLite database into the temporary snapshot.");

        ExecuteNonQuery(destinationConnection, "PRAGMA journal_mode = DELETE;");
        ExecuteNonQuery(destinationConnection, "DELETE FROM UserAuth;");
        ExecuteNonQuery(destinationConnection, "VACUUM;");
        Log.Debug("Removed authentication data and compacted the temporary SQLite snapshot.");

        using (var authCheckCommand = destinationConnection.CreateCommand())
        {
            authCheckCommand.CommandText = "SELECT COUNT(*) FROM UserAuth;";
            var authRowCount = Convert.ToInt64(authCheckCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (authRowCount != 0)
            {
                throw new InvalidDataException("The sanitized database backup still contains authentication rows.");
            }
        }

        using var integrityCommand = destinationConnection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA quick_check;";
        var integrityResult = Convert.ToString(integrityCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite backup integrity check failed: {integrityResult ?? "no result"}");
        }

        Log.Debug("Validated sanitized SQLite snapshot successfully.");
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private void ApplyRetention(DateTimeOffset nowUtc)
    {
        var backups = GetCompletedBackups()
            .OrderByDescending(backup => backup.TimestampUtc)
            .ToList();
        var retainedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remainingBackups = backups.Skip(3).ToList();
        var deletedBackupCount = 0;

        foreach (var backup in backups.Take(3))
        {
            retainedPaths.Add(backup.Path);
        }

        foreach (var targetAge in RetentionTargetAges)
        {
            var selectedBackup = remainingBackups
                .OrderBy(backup => Math.Abs(((nowUtc - backup.TimestampUtc) - targetAge).TotalSeconds))
                .ThenBy(backup => backup.TimestampUtc)
                .FirstOrDefault();
            if (selectedBackup == null)
            {
                break;
            }

            retainedPaths.Add(selectedBackup.Path);
            remainingBackups.Remove(selectedBackup);
        }

        foreach (var backup in backups.Where(backup => !retainedPaths.Contains(backup.Path)))
        {
            try
            {
                File.Delete(backup.Path);
                deletedBackupCount++;
                Log.Debug("Deleted expired automatic SQLite backup. Path={BackupPath}", backup.Path);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete expired automatic SQLite backup. Path={BackupPath}", backup.Path);
            }
        }

        Log.Debug(
            "Applied automatic SQLite backup retention. AvailableBackupCount={AvailableBackupCount}, RetainedBackupCount={RetainedBackupCount}, DeletedBackupCount={DeletedBackupCount}",
            backups.Count,
            retainedPaths.Count,
            deletedBackupCount);
    }

    private List<DatabaseBackupFile> GetCompletedBackups()
    {
        if (!Directory.Exists(BackupDirectoryPath))
        {
            return [];
        }

        var backups = new List<DatabaseBackupFile>();
        foreach (var path in Directory.EnumerateFiles(BackupDirectoryPath, $"{BackupFilePrefix}*.zip", SearchOption.TopDirectoryOnly))
        {
            var match = BackupFileNameRegex.Match(Path.GetFileName(path));
            if (!match.Success)
            {
                continue;
            }

            if (DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                BackupTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestampUtc))
            {
                backups.Add(new DatabaseBackupFile(path, timestampUtc));
            }
        }

        return backups;
    }

    private void CleanupStaleTemporaryFiles()
    {
        foreach (var path in Directory.EnumerateFiles(BackupDirectoryPath, $"{BackupFilePrefix}*.tmp*", SearchOption.TopDirectoryOnly))
        {
            DeleteTemporaryFile(path);
        }
    }

    private static void DeleteTemporaryDatabaseArtifacts(string databasePath)
    {
        DeleteTemporaryFile(databasePath);
        DeleteTemporaryFile($"{databasePath}-journal");
        DeleteTemporaryFile($"{databasePath}-shm");
        DeleteTemporaryFile($"{databasePath}-wal");
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Log.Debug("Deleted temporary SQLite backup file. Path={BackupTemporaryPath}", path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete temporary SQLite backup file. Path={BackupTemporaryPath}", path);
        }
    }

    private void StartFolderProcess(string executable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(BackupDirectoryPath);
        Process.Start(startInfo);
        Log.Debug("Opened SQLite backup folder. Path={BackupDirectoryPath}", BackupDirectoryPath);
    }

    private sealed record DatabaseBackupFile(string Path, DateTimeOffset TimestampUtc);
}
