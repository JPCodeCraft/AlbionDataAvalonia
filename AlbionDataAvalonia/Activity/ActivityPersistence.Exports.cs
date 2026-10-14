using AlbionDataAvalonia.Activity.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    public async Task<IReadOnlyList<ActivityEvent>> GetLootExportSnapshotAsync(ActivityQuery filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        // The caller's list filters (session, period, scope, search, sources) are kept as shown.
        var snapshotFilter = filter with { Kind = ActivityEventKind.Loot, LootOnly = true };
        // Never hold the read gate while waiting for accepted writes to commit.
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(ct => GetLootExportSnapshotCoreAsync(snapshotFilter, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ActivityEvent>> GetLootExportSnapshotCoreAsync(ActivityQuery filter,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        // All batches see one WAL snapshot without reserving the writer lock.
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var query = FilterEvents(context, filter).OrderByDescending(x => x.CapturedAtUtc)
            .ThenByDescending(x => x.CaptureOrder).ThenBy(x => x.Id);
        var rows = new List<ActivityEvent>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await query.Skip(rows.Count).Take(250).ToArrayAsync(cancellationToken);
            rows.AddRange(batch);
            if (batch.Length < 250) break;
        }
        await transaction.CommitAsync(cancellationToken);
        return rows.AsReadOnly();
    }
}
