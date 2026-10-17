using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.DB;

public enum DataCleanupType
{
    Activity,
    Trades,
    Mails,
    AwakenedItems
}

public sealed record DataStorageRow(DataCleanupType Type, long Bytes, long Rows, string CountText);

public sealed record DataStorageSummary(IReadOnlyList<DataStorageRow> Rows, long DatabaseBytes, long OtherBytes, long BackupBytes, bool IsEstimated);

public sealed record DataCleanupRequest(DateTime OlderThanUtc, IReadOnlySet<DataCleanupType> Types, bool RemoveOutsidePartyLoot);

public sealed record DataCleanupPreview(DataCleanupRequest Request, ActivityCleanupPreview? Activity, long Trades, long Mails, long AwakenedItems)
{
    public bool HasData => Activity?.HasData == true || Trades > 0 || Mails > 0 || AwakenedItems > 0;
}

/// <summary>Measures stored data per type and removes old records from all of them in one place.</summary>
public sealed class DataCleanupService(
    ActivityPersistence activity,
    ActivityTrackerService tracker,
    TradeService trades,
    MailService mails,
    LegendaryItemTrackerService awakenedItems)
{
    // Rough row sizes for SQLite builds without dbstat; the UI labels the result as an estimate.
    private const long EstimatedTradeBytes = 220;
    private const long EstimatedMailBytes = 240;
    private const long EstimatedAwakenedItemBytes = 700;

    public async Task<DataStorageSummary> GetStorageAsync(CancellationToken cancellationToken = default)
    {
        var activityStorage = await activity.GetStorageSummaryAsync(cancellationToken).ConfigureAwait(false);

        await using var context = new LocalContext();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var tradeRows = await context.Trades.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var mailRows = await context.AlbionMails.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var awakenedRows = await context.LegendaryItems.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var estimated = activityStorage.IsActivitySizeEstimated;
        long tradeBytes, mailBytes, awakenedBytes;
        try
        {
            tradeBytes = await TableBytesAsync(connection, ["Trades"], cancellationToken).ConfigureAwait(false);
            mailBytes = await TableBytesAsync(connection, ["AlbionMails"], cancellationToken).ConfigureAwait(false);
            awakenedBytes = await TableBytesAsync(connection, ["LegendaryItems", "LegendaryItemTraits"], cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 1)
        {
            estimated = true;
            tradeBytes = tradeRows * EstimatedTradeBytes;
            mailBytes = mailRows * EstimatedMailBytes;
            awakenedBytes = awakenedRows * EstimatedAwakenedItemBytes;
        }

        var rows = new List<DataStorageRow>
        {
            new(DataCleanupType.Activity, activityStorage.ActivityBytes, activityStorage.Events,
                $"{activityStorage.Sessions:N0} sessions · {activityStorage.Events:N0} events"),
            new(DataCleanupType.Trades, tradeBytes, tradeRows, $"{tradeRows:N0} trades"),
            new(DataCleanupType.Mails, mailBytes, mailRows, $"{mailRows:N0} mails"),
            new(DataCleanupType.AwakenedItems, awakenedBytes, awakenedRows, $"{awakenedRows:N0} items")
        };

        var databaseBytes = activityStorage.DatabaseBytes;
        var otherBytes = Math.Max(0, databaseBytes - rows.Sum(row => row.Bytes));
        return new(rows, databaseBytes, otherBytes, FolderBytes(AppData.BackupDirectoryPath), estimated);
    }

    public async Task<DataCleanupPreview> PreviewAsync(DataCleanupRequest request, CancellationToken cancellationToken = default)
    {
        var cutoff = request.OlderThanUtc;
        var activityPreview = request.Types.Contains(DataCleanupType.Activity)
            ? await activity.PreviewCleanupAsync(ActivityRequest(request), cancellationToken).ConfigureAwait(false)
            : null;
        var tradeCount = request.Types.Contains(DataCleanupType.Trades)
            ? await trades.CountTradesOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false)
            : 0;
        var mailCount = request.Types.Contains(DataCleanupType.Mails)
            ? await mails.CountMailsOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false)
            : 0;
        var awakenedCount = request.Types.Contains(DataCleanupType.AwakenedItems)
            ? await awakenedItems.CountItemsNotSeenSinceAsync(cutoff, cancellationToken).ConfigureAwait(false)
            : 0;
        return new(request, activityPreview, tradeCount, mailCount, awakenedCount);
    }

    public async Task<DataCleanupPreview> CleanupAsync(DataCleanupPreview preview, bool compact, CancellationToken cancellationToken = default)
    {
        var request = preview.Request;
        var cutoff = request.OlderThanUtc;
        ActivityCleanupPreview? deletedActivity = null;
        if (preview.Activity?.HasData == true)
        {
            deletedActivity = (await activity.CleanupAsync(preview.Activity.Request, cancellationToken).ConfigureAwait(false)).Deleted;
        }

        var deletedTrades = preview.Trades > 0 ? await trades.DeleteTradesOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false) : 0;
        var deletedMails = preview.Mails > 0 ? await mails.DeleteMailsOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false) : 0;
        var deletedAwakened = preview.AwakenedItems > 0
            ? await awakenedItems.DeleteItemsNotSeenSinceAsync(cutoff, cancellationToken).ConfigureAwait(false)
            : 0;

        Log.Information(
            "Data cleanup removed {Sessions} sessions, {Events} events, {Trades} trades, {Mails} mails and {AwakenedItems} awakened items older than {CutoffUtc:O}",
            deletedActivity?.Sessions ?? 0, deletedActivity?.Events ?? 0, deletedTrades, deletedMails, deletedAwakened, cutoff);

        if (compact)
        {
            await activity.CompactDatabaseAsync(cancellationToken).ConfigureAwait(false);
            Log.Information("Compacted the local database after cleanup");
        }

        return new(request, deletedActivity, deletedTrades, deletedMails, deletedAwakened);
    }

    // The open session is kept so cleanup never cuts into what is being recorded right now.
    private ActivityCleanupRequest ActivityRequest(DataCleanupRequest request) =>
        new(request.OlderThanUtc, request.RemoveOutsidePartyLoot, tracker.CurrentSessionId);

    private static async Task<long> TableBytesAsync(SqliteConnection connection, IReadOnlyList<string> tables, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var names = string.Join(",", tables.Select((_, index) => $"$t{index}"));
        command.CommandText = $"SELECT COALESCE(SUM(pgsize),0) FROM dbstat WHERE name IN (SELECT name FROM sqlite_schema WHERE tbl_name IN ({names}))";
        for (var index = 0; index < tables.Count; index++)
        {
            command.Parameters.AddWithValue($"$t{index}", tables[index]);
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static long FolderBytes(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
                : 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
