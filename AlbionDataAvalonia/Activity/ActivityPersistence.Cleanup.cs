using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    private readonly HashSet<Guid> deletedEvents = [];

    public async Task<ActivityStorageSummary> GetStorageSummaryAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(GetStorageSummaryCoreAsync, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivityCleanupPreview> PreviewCleanupAsync(ActivityCleanupRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCleanupRequest(request);
        request = request with { ObservedThroughUtc = request.ObservedThroughUtc ?? DateTime.UtcNow };
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ReadAsync(async ct =>
        {
            await using var context = contextFactory();
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await connection.OpenAsync(ct);
            await using var transaction = connection.BeginTransaction(deferred: true);
            await context.Database.UseTransactionAsync(transaction, ct);
            return await CleanupCountsAsync(context, request, ct);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActivityCleanupResult> CleanupAsync(ActivityCleanupRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCleanupRequest(request);
        var command = new CleanupCommand(request, NewCompletion());
        await ControlAsync(command, cancellationToken).ConfigureAwait(false);
        return command.Result!;
    }

    private static void ValidateCleanupRequest(ActivityCleanupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OlderThanUtc is null && !request.RemoveOutsidePartyLoot)
            throw new ArgumentException("Select activity to remove.", nameof(request));
        if (request.OlderThanUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("Activity cleanup uses UTC dates.", nameof(request));
        if (request.ObservedThroughUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("Activity cleanup uses UTC dates.", nameof(request));
    }

    private static IQueryable<ActivitySession> CleanupSessions(LocalContext context, ActivityCleanupRequest request)
    {
        var cutoff = request.OlderThanUtc;
        var protectedId = request.ProtectedSessionId;
        var through = request.ObservedThroughUtc;
        return context.ActivitySessions.Where(s => cutoff != null && s.State != ActivityState.Open &&
            (protectedId == null || s.Id != protectedId) && (s.ClosedAtUtc ?? s.LastObservedAtUtc) < cutoff &&
            (through == null || s.LastObservedAtUtc <= through));
    }

    private static IQueryable<ActivityEvent> CleanupEvents(LocalContext context, ActivityCleanupRequest request)
    {
        var sessions = CleanupSessions(context, request).Select(s => s.Id);
        var cutoff = request.OlderThanUtc;
        var outsideParty = request.RemoveOutsidePartyLoot;
        var protectedId = request.ProtectedSessionId;
        var through = request.ObservedThroughUtc;
        // Import markers must survive cleanup, otherwise untouched legacy tables would
        // import their deleted sessions again on the next application start.
        return context.ActivityEvents.Where(e => e.Kind != ActivityEventKind.LegacyImportMarker && (through == null || e.CapturedAtUtc <= through) &&
            (e.SessionId != null && sessions.Contains(e.SessionId.Value) ||
            cutoff != null && e.SessionId == null && e.CapturedAtUtc < cutoff ||
            outsideParty && e.Kind == ActivityEventKind.Loot && e.SourceRelationship != ActorRelationship.Self &&
                e.SourceRelationship != ActorRelationship.Party && (e.SessionId == null ||
                    context.ActivitySessions.Any(s => s.Id == e.SessionId && s.State != ActivityState.Open &&
                        (protectedId == null || s.Id != protectedId)))));
    }

    private static async Task<ActivityCleanupPreview> CleanupCountsAsync(LocalContext context, ActivityCleanupRequest request, CancellationToken cancellationToken)
    {
        var sessions = CleanupSessions(context, request);
        var events = CleanupEvents(context, request);
        return new(request, await sessions.LongCountAsync(cancellationToken),
            await context.ActivityEncounters.Where(e => sessions.Any(s => s.Id == e.SessionId)).LongCountAsync(cancellationToken),
            await events.LongCountAsync(cancellationToken), await events.Where(e => e.Kind == ActivityEventKind.Loot ||
                e.Kind == ActivityEventKind.Gathered || e.Kind == ActivityEventKind.FishingReward ||
                e.Kind == ActivityEventKind.LegacyGatheringAggregate).LongCountAsync(cancellationToken));
    }

    private async Task ExecuteCleanupAsync(CleanupCommand command, CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var preview = await CleanupCountsAsync(context, command.Request, cancellationToken);
        var removedSessions = new List<Guid>();
        var removedEvents = new List<Guid>();
        var changes = new CommittedChanges();
        var touchedPlaySessions = new HashSet<Guid>();
        while (true)
        {
            var ids = await CleanupSessions(context, command.Request).OrderBy(s => s.Id).Select(s => s.Id).Take(100).ToArrayAsync(cancellationToken);
            if (ids.Length == 0) break;
            touchedPlaySessions.UnionWith(await context.ActivitySessions.Where(s => ids.Contains(s.Id) && s.PlaySessionId != null)
                .Select(s => s.PlaySessionId!.Value).ToArrayAsync(cancellationToken));
            await context.ActivitySessions.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);
            removedSessions.AddRange(ids);
            foreach (var id in ids) changes.Sessions.Add(id);
        }
        // Remaining matches are individual events. Apply their signed contributions before
        // removal, so a surviving session and encounter retain accurate cached totals.
        while (true)
        {
            var rows = await CleanupEvents(context, command.Request).OrderBy(e => e.CapturedAtUtc).ThenBy(e => e.Id).Take(250).ToArrayAsync(cancellationToken);
            if (rows.Length == 0) break;
            var sessions = new Dictionary<Guid, ActivitySession>();
            var encounters = new Dictionary<Guid, ActivityEncounter>();
            foreach (var row in rows)
            {
                await ApplyContributionAsync(context, row, -1, sessions, encounters, cancellationToken);
                removedEvents.Add(row.Id);
            }
            context.ActivityEvents.RemoveRange(rows);
            changes.Include(context);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
        changes.PlaySessions.UnionWith(touchedPlaySessions);
        await RefreshPlaySessionsAsync(context, touchedPlaySessions, changes, cancellationToken);
        changes.Items |= preview.ItemPickups > 0;
        await transaction.CommitAsync(cancellationToken);
        lock (acceptedSync)
        {
            foreach (var id in removedSessions) deletedSessions.Add(id);
            foreach (var id in removedEvents) deletedEvents.Add(id);
            Interlocked.Increment(ref readVersion);
        }
        command.Result = new(preview, removedSessions);
        NotifyCommitted(changes);
        foreach (var id in removedSessions)
        {
            try { SessionDeleted?.Invoke(id); } catch { }
        }
    }

    private async Task<ActivityStorageSummary> GetStorageSummaryCoreAsync(CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var sessions = await context.ActivitySessions.LongCountAsync(cancellationToken);
        var encounters = await context.ActivityEncounters.LongCountAsync(cancellationToken);
        var events = await context.ActivityEvents.LongCountAsync(cancellationToken);
        var items = await context.ActivityEvents.Where(e => e.Kind == ActivityEventKind.Loot || e.Kind == ActivityEventKind.Gathered ||
            e.Kind == ActivityEventKind.FishingReward || e.Kind == ActivityEventKind.LegacyGatheringAggregate).LongCountAsync(cancellationToken);
        var estimated = false;
        long activityBytes;
        try
        {
            activityBytes = await ScalarLongAsync(connection, transaction,
                "SELECT COALESCE(SUM(pgsize),0) FROM dbstat WHERE name IN (SELECT name FROM sqlite_schema WHERE tbl_name IN ('ActivitySessions','ActivityEncounters','ActivityEvents'))", cancellationToken);
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 1)
        {
            // Some SQLite builds omit dbstat. This fallback measures stored text payloads
            // and estimates row/index overhead; the UI explicitly labels it as estimated.
            estimated = true;
            activityBytes = await ScalarLongAsync(connection, transaction,
                "SELECT COALESCE((SELECT SUM(280+length(CAST(PersonalSummaryJson||PartySummaryJson||ObservedSummaryJson||VisitsJson||KnownInstancesJson||Title||LocationName||CharacterName AS BLOB))) FROM ActivitySessions),0) + " +
                "COALESCE((SELECT SUM(220+length(CAST(PersonalSummaryJson||PartySummaryJson||ObservedSummaryJson||LocationName AS BLOB))) FROM ActivityEncounters),0) + " +
                "COALESCE((SELECT SUM(480+length(CAST(DetailsJson||SourceName||TargetName||ItemName||LocationName||LootSourceName||COALESCE(OriginKey,'')||COALESCE(ItemUniqueName,'') AS BLOB))) FROM ActivityEvents),0)", cancellationToken);
        }
        var databaseBytes = FileLength(connection.DataSource) + FileLength(connection.DataSource + "-wal") + FileLength(connection.DataSource + "-shm");
        return new(sessions, encounters, events, items, activityBytes, databaseBytes, estimated);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static long FileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private sealed record CleanupCommand(ActivityCleanupRequest Request, TaskCompletionSource Completion) : ControlCommand(Completion)
    {
        public ActivityCleanupResult? Result { get; set; }
    }
}
