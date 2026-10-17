using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Gathering.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    public ActivityImportResult? LastLegacyImportResult { get; private set; }

    public async Task<ActivityImportResult> ImportLegacyGatheringAsync(CancellationToken cancellationToken = default)
    {
        var command = new ImportLegacyCommand(NewCompletion());
        await ControlAsync(command, cancellationToken).ConfigureAwait(false);
        LastLegacyImportResult = command.Result;
        return command.Result;
    }
    public Task RebuildActivityTagsAsync(CancellationToken cancellationToken = default) =>
        ControlAsync(new RebuildTagsCommand(NewCompletion()), cancellationToken);

    private async Task RebuildTagsAsync(CancellationToken cancellationToken)
    {
        // Recompute only versioned caches; event identity, chronology and ownership stay intact.
        // A bounded transaction keeps a failed/restarted rebuild resumable.
        Guid? afterSession = null;
        while (true)
        {
            await using var context = contextFactory();
            var candidates = afterSession is { } sessionId
                ? context.ActivitySessions.FromSqlInterpolated($"SELECT * FROM \"ActivitySessions\" WHERE \"Id\" > {sessionId}")
                : context.ActivitySessions;
            var rows = await candidates.Where(x =>
                    ActivityDatabaseFunctions.JsonLong(x.PersonalSummaryJson, "$.TagVersion") == null ||
                    ActivityDatabaseFunctions.JsonLong(x.PersonalSummaryJson, "$.TagVersion") != ActivityClassification.TagVersion)
                .OrderBy(x => x.Id).Take(100).ToArrayAsync(cancellationToken);
            if (rows.Length == 0) break;
            foreach (var row in rows)
            {
                var replacement = row with
                {
                    PersonalSummaryJson = await SummaryJson(new() { SessionId = row.Id, Scope = ActivityScope.Personal }),
                    PartySummaryJson = await SummaryJson(new() { SessionId = row.Id, Scope = ActivityScope.Party }),
                    ObservedSummaryJson = await SummaryJson(new() { SessionId = row.Id, Scope = ActivityScope.AllObserved })
                };
                context.Entry(row).CurrentValues.SetValues(replacement);
            }
            var changes = new CommittedChanges();
            changes.Include(context);
            await context.SaveChangesAsync(cancellationToken);
            NotifyCommitted(changes);
            afterSession = rows[^1].Id;
        }
        Guid? afterEncounter = null;
        while (true)
        {
            await using var context = contextFactory();
            var candidates = afterEncounter is { } encounterId
                ? context.ActivityEncounters.FromSqlInterpolated($"SELECT * FROM \"ActivityEncounters\" WHERE \"Id\" > {encounterId}")
                : context.ActivityEncounters;
            var rows = await candidates.Where(x =>
                    ActivityDatabaseFunctions.JsonLong(x.PersonalSummaryJson, "$.TagVersion") == null ||
                    ActivityDatabaseFunctions.JsonLong(x.PersonalSummaryJson, "$.TagVersion") != ActivityClassification.TagVersion)
                .OrderBy(x => x.Id).Take(100).ToArrayAsync(cancellationToken);
            if (rows.Length == 0) break;
            foreach (var row in rows)
                context.Entry(row).CurrentValues.SetValues(row with
                {
                    PersonalSummaryJson = await SummaryJson(new() { EncounterId = row.Id, Scope = ActivityScope.Personal }),
                    PartySummaryJson = await SummaryJson(new() { EncounterId = row.Id, Scope = ActivityScope.Party }),
                    ObservedSummaryJson = await SummaryJson(new() { EncounterId = row.Id, Scope = ActivityScope.AllObserved })
                });
            var changes = new CommittedChanges();
            changes.Include(context);
            await context.SaveChangesAsync(cancellationToken);
            NotifyCommitted(changes);
            afterEncounter = rows[^1].Id;
        }
        async Task<string> SummaryJson(ActivityQuery query) => ActivityProjection.Serialize(
            (await GetSummaryAsync(query, cancellationToken)) with { TagVersion = ActivityClassification.TagVersion });
    }

    private async Task<ActivityImportResult> ImportLegacyRunsAsync(CancellationToken cancellationToken)
    {
        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();
        var page = 0;
        while (true)
        {
            GatheringCompletedSession[] rows;
            await using (var context = contextFactory())
                rows = await context.GatheringCompletedSessions.AsNoTracking().OrderBy(x => x.EndedAtUtc).ThenBy(x => x.Id)
                    .Skip(page++ * 100).Take(100).ToArrayAsync(cancellationToken);
            if (rows.Length == 0) break;
            var markers = await GetImportMarkersAsync(rows.Select(x => x.Id), cancellationToken);
            foreach (var row in rows)
            {
                try
                {
                    if (markers.Contains(ImportOrigin(row.Id))) { skipped++; continue; }
                    var items = new List<LegacyItem>();
                    var itemPage = 0;
                    while (true)
                    {
                        await using var context = contextFactory();
                        var batch = await context.GatheringCompletedSessionItems.AsNoTracking().Where(x => x.SessionId == row.Id)
                            .OrderBy(x => x.Id).Skip(itemPage++ * 250).Take(250).ToArrayAsync(cancellationToken);
                        if (batch.Length == 0) break;
                        items.AddRange(batch.Select(x => new LegacyItem(x.Id, null, x.ItemId, x.Quality, x.ItemUniqueName, x.ItemName,
                            x.Amount, x.EstimatedMarketValue, x.TotalEstimatedMarketValue, Method(x.Source))));
                    }
                    await ImportRunAsync(row.Id, null, Utc(row.StartedAtUtc), Utc(row.EndedAtUtc), Utc(row.LastActivityAtUtc),
                        row.ActiveElapsedSeconds, row.AlbionServerId, row.PlayerName, row.TotalAmount, row.TotalEstimatedMarketValue,
                        row.SilverPerHour, items, cancellationToken);
                    imported++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { AddError(row.Id, error); }
            }
        }
        // Do not call the legacy recovery loader: it deletes stale/corrupt originals.
        // Completed snapshots are authoritative when their logical session ID also has a checkpoint.
        page = 0;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        while (true)
        {
            GatheringUnfinishedSessionCheckpoint[] rows;
            await using (var context = contextFactory())
                rows = await context.GatheringUnfinishedSessionCheckpoints.AsNoTracking().OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id)
                    .Skip(page++ * 100).Take(100).ToArrayAsync(cancellationToken);
            if (rows.Length == 0) break;
            var sourceIds = rows.Select(x => x.SessionId).Distinct().ToArray();
            var markers = await GetImportMarkersAsync(sourceIds, cancellationToken);
            HashSet<Guid> completedIds;
            await using (var context = contextFactory())
                completedIds = (await context.GatheringCompletedSessions.AsNoTracking().Where(x => sourceIds.Contains(x.Id))
                    .Select(x => x.Id).ToArrayAsync(cancellationToken)).ToHashSet();
            foreach (var row in rows)
            {
                try
                {
                    if (completedIds.Contains(row.SessionId) || markers.Contains(ImportOrigin(row.SessionId))) { skipped++; continue; }
                    var payload = JsonSerializer.Deserialize<GatheringSessionCheckpointPayload>(row.PayloadJson, options)
                        ?? throw new InvalidOperationException("Checkpoint payload is missing.");
                    var start = Utc(row.StartedAtUtc);
                    var end = Utc(row.LastActivityAtUtc);
                    var elapsed = ObservedSeconds(start, end, payload.PauseIntervals ?? []);
                    var items = payload.Items.Select((x, i) => new LegacyItem(null, i, x.ItemId, x.Quality, x.ItemUniqueName, x.ItemName,
                        x.Amount, x.EstimatedMarketValue, x.EstimatedMarketValue is { } unit ? checked(unit * x.Amount) : null, Method(x.Source))).ToArray();
                    var amount = items.Aggregate(0L, (sum, item) => checked(sum + item.Amount));
                    var value = items.Aggregate(0L, (sum, item) => checked(sum + (item.TotalValue ?? 0)));
                    await ImportRunAsync(row.SessionId, row.Id, start, end, end, elapsed, payload.AlbionServerId,
                        payload.PlayerName ?? "", amount, value, elapsed > 0 ? checked((long)Math.Round(value * 3600d / elapsed)) : 0, items, cancellationToken);
                    markers.Add(ImportOrigin(row.SessionId));
                    imported++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { AddError(row.SessionId, error); }
            }
        }
        await DeleteImportedLegacyRowsAsync(cancellationToken);
        if (errors.Count > 0) PublishStatus(false, errors[0]);
        return new(imported, skipped, errors);
        void AddError(Guid sourceId, Exception error)
        {
            if (errors.Count < 20) errors.Add($"Legacy gathering session {sourceId}: {error.Message} Originals were preserved; retry on next startup.");
        }
    }

    // Originals already copied into Activity are redundant; rows that failed to import stay for the next retry.
    private async Task DeleteImportedLegacyRowsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = contextFactory();
            var completed = await context.GatheringCompletedSessions.AsNoTracking().Select(x => x.Id).ToArrayAsync(cancellationToken);
            var checkpoints = await context.GatheringUnfinishedSessionCheckpoints.AsNoTracking().Select(x => x.SessionId).ToArrayAsync(cancellationToken);
            if (completed.Length == 0 && checkpoints.Length == 0) return;
            var markers = await GetImportMarkersAsync(completed.Concat(checkpoints), cancellationToken);
            var importedCompleted = completed.Where(id => markers.Contains(ImportOrigin(id))).ToArray();
            var importedCheckpoints = checkpoints.Where(id => markers.Contains(ImportOrigin(id))).ToArray();
            foreach (var batch in importedCompleted.Chunk(200))
            {
                await context.GatheringCompletedSessionItems.Where(x => batch.Contains(x.SessionId)).ExecuteDeleteAsync(cancellationToken);
                await context.GatheringCompletedSessions.Where(x => batch.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
            }
            foreach (var batch in importedCheckpoints.Chunk(200))
                await context.GatheringUnfinishedSessionCheckpoints.Where(x => batch.Contains(x.SessionId)).ExecuteDeleteAsync(cancellationToken);
            if (importedCompleted.Length + importedCheckpoints.Length > 0)
                Log.Information("Removed {Completed} imported legacy gathering sessions and {Checkpoints} checkpoints",
                    importedCompleted.Length, importedCheckpoints.Length);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { Log.Warning(error, "Imported legacy gathering rows could not be removed; they stay until the next start."); }
    }

    private async Task<HashSet<string>> GetImportMarkersAsync(IEnumerable<Guid> sourceIds, CancellationToken cancellationToken)
    {
        var origins = sourceIds.Select(ImportOrigin).Distinct().ToArray();
        if (origins.Length == 0) return [];
        await using var context = contextFactory();
        var markers = await context.ActivityEvents.AsNoTracking().Where(x => x.Kind == ActivityEventKind.LegacyImportMarker &&
            x.OriginKey != null && origins.Contains(x.OriginKey)).Select(x => x.OriginKey!).ToArrayAsync(cancellationToken);
        return markers.ToHashSet(StringComparer.Ordinal);
    }

    private async Task ImportRunAsync(Guid sourceId, Guid? checkpointId, DateTime start, DateTime end, DateTime last,
        double observedSeconds, int? server, string playerName, long originalAmount, long originalValue, long originalRate,
        IReadOnlyCollection<LegacyItem> originals, CancellationToken cancellationToken)
    {
        if (sourceId == Guid.Empty || start > end || last < start || last > end || observedSeconds < 0 || !double.IsFinite(observedSeconds))
            throw new InvalidOperationException("Invalid legacy session timestamps or observed duration.");
        var items = originals.Where(x => x.ItemId > 0 && x.Amount > 0 && x.UnitValue is not < 0 && x.TotalValue is not < 0).ToArray();
        if (items.Length == 0) throw new InvalidOperationException("No valid item totals to import.");
        var amount = items.Aggregate(0L, (sum, item) => checked(sum + item.Amount));
        var value = items.Aggregate(0L, (sum, item) => checked(sum + (item.TotalValue ?? 0)));
        var mismatch = items.Length != originals.Count || amount != originalAmount || value != originalValue ||
            items.Any(x => x.UnitValue is { } unit && (x.Amount > long.MaxValue / Math.Max(1, unit) || x.TotalValue != unit * x.Amount));
        var id = StableId($"legacy-gathering:session:{sourceId:N}");
        var empty = new ActivitySummary { TagVersion = ActivityClassification.TagVersion };
        var summary = empty;
        var info = new ActivityLegacyImportInfo
        {
            SourceSessionId = sourceId,
            CheckpointId = checkpointId,
            IsCheckpoint = checkpointId is not null,
            OriginalTotalAmount = originalAmount,
            OriginalTotalValue = originalValue,
            OriginalSilverPerHour = originalRate,
            StartedAtUtc = start,
            EndedAtUtc = end,
            HasMismatch = mismatch
        };
        var events = new List<ActivityEvent>();
        long order = 0;
        foreach (var item in items)
        {
            var key = item.Id is { } itemId ? $"item:{itemId:N}" : $"checkpoint:{checkpointId:N}:item:{item.Index}";
            var identity = ActivityResourceIdentity.FromUniqueName(item.UniqueName);
            var observation = new ActivityEvent
            {
                Id = StableId($"legacy-gathering:{sourceId:N}:{key}"),
                OriginKey = $"{ImportOrigin(sourceId)}:{key}",
                SessionId = id,
                Kind = ActivityEventKind.LegacyGatheringAggregate,
                Evidence = ActivityEvidence.Confirmed,
                // This is an ordering anchor for a session aggregate, never an individual pickup time.
                CapturedAtUtc = last,
                CaptureOrder = ++order,
                Server = server?.ToString(CultureInfo.InvariantCulture) ?? "",
                CharacterName = playerName,
                SourceName = playerName,
                SourceKind = ActivityActorKind.Player,
                SourceRelationship = ActorRelationship.Self,
                LocationName = "Not recorded",
                ItemId = item.ItemId,
                Quality = item.Quality > 0 ? item.Quality : null,
                ItemUniqueName = item.UniqueName,
                ItemName = item.Name,
                Quantity = item.Amount,
                EstimatedUnitValue = item.UnitValue,
                DetailsJson = JsonSerializer.Serialize(new ActivityResourceDetails
                {
                    Timing = "SessionAggregate",
                    Method = item.Method,
                    ResourceFamily = identity.Family,
                    Tier = identity.Tier,
                    Enchantment = identity.Enchantment,
                    Legacy = info with { SourceItemId = item.Id, CheckpointItemIndex = item.Index, RecordedTotalValue = item.TotalValue }
                })
            };
            // Stored total values are authoritative even if a damaged legacy row has an inconsistent unit price.
            // Keep an inconsistent unit price as provenance rather than feeding overflow into the normal writer.
            if (observation.EstimatedUnitValue is { } price && observation.Quantity > long.MaxValue / Math.Max(1, price))
                observation = observation with { EstimatedUnitValue = null };
            Validate(observation);
            events.Add(observation);
            summary = ActivityProjection.Add(summary, ActivityProjection.Contribution(observation, ActivityScope.Personal));
        }
        var header = new ActivitySession
        {
            Id = id,
            Kind = ActivitySessionKind.Unknown,
            Title = "Imported gathering / fishing session",
            CharacterName = playerName,
            Server = server?.ToString(CultureInfo.InvariantCulture) ?? "",
            LocationName = "Not recorded",
            StartedAtUtc = start,
            LastRelevantAtUtc = last,
            LastObservedAtUtc = end,
            ClosedAtUtc = end,
            ObservedDurationSeconds = observedSeconds,
            State = checkpointId is null ? ActivityState.Closed : ActivityState.Interrupted,
            EndReason = checkpointId is null ? ActivityEndReason.None : ActivityEndReason.Interrupted,
            IsPartial = true,
            PersonalSummaryJson = ActivityProjection.Serialize(summary),
            PartySummaryJson = ActivityProjection.Serialize(summary),
            ObservedSummaryJson = ActivityProjection.Serialize(summary)
        };
        var marker = new ActivityEvent
        {
            Id = StableId(ImportOrigin(sourceId)),
            OriginKey = ImportOrigin(sourceId),
            Kind = ActivityEventKind.LegacyImportMarker,
            Evidence = ActivityEvidence.Supplemental,
            CapturedAtUtc = DateTime.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new { Version = 1, SourceSessionId = sourceId, TargetSessionId = id })
        };
        await using var context = contextFactory();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.ActivityEvents.AnyAsync(x => x.OriginKey == marker.OriginKey, cancellationToken)) return;
        if (await context.ActivitySessions.AnyAsync(x => x.Id == id, cancellationToken))
            throw new InvalidOperationException("An unmarked Activity session already uses this import identity.");
        context.ActivitySessions.Add(header);
        context.ActivityEvents.AddRange(events);
        context.ActivityEvents.Add(marker);
        var changes = new CommittedChanges();
        changes.Include(context);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        NotifyCommitted(changes);
        // Unassigned marker survives assigned-session deletion and is hidden from all public reads.
    }

    private static double ObservedSeconds(DateTime start, DateTime end, IReadOnlyList<GatheringSessionCheckpointPauseInterval> pauses)
    {
        if (end < start) throw new InvalidOperationException("Checkpoint ends before it starts.");
        var intervals = pauses.Select(x => (Start: Utc(x.StartedAtUtc), End: x.EndedAtUtc is { } closed ? Utc(closed) : end))
            .Select(x => (Start: x.Start < start ? start : x.Start, End: x.End > end ? end : x.End)).Where(x => x.End > x.Start).OrderBy(x => x.Start).ToArray();
        var paused = 0d;
        DateTime? boundary = null;
        foreach (var interval in intervals)
        {
            var first = boundary is { } previous && previous > interval.Start ? previous : interval.Start;
            if (interval.End > first) paused += (interval.End - first).TotalSeconds;
            if (boundary is null || interval.End > boundary) boundary = interval.End;
        }
        return Math.Max(0, (end - start).TotalSeconds - paused);
    }
    private static ActivityResourceMethod Method(GatheringSessionSource source) => source switch
    { GatheringSessionSource.Gathering => ActivityResourceMethod.Gathering, GatheringSessionSource.Fishing => ActivityResourceMethod.Fishing, GatheringSessionSource.Mixed => ActivityResourceMethod.Mixed, _ => ActivityResourceMethod.Unknown };
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string ImportOrigin(Guid sourceId) => $"legacy-gathering-import:v1:{sourceId:N}";
    private static Guid StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes.AsSpan(0, 16));
    }
    private sealed record LegacyItem(Guid? Id, int? Index, int ItemId, int Quality, string UniqueName, string Name,
        long Amount, long? UnitValue, long? TotalValue, ActivityResourceMethod Method);
    private sealed record ImportLegacyCommand(TaskCompletionSource Completion) : ControlCommand(Completion)
    { public ActivityImportResult Result { get; set; } = new(0, 0, []); }
    private sealed record RebuildTagsCommand(TaskCompletionSource Completion) : ControlCommand(Completion);
}
