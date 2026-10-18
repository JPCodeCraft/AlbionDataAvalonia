using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Locations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    // The owner selects one run (SessionId), all runs of a play session (PlaySessionId) or of several (PlaySessionIds).
    public Task<ActivityPage<ActivityMap>> GetMapsAsync(ActivityQuery owner, int page = 0,
        CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetMapsCoreAsync(owner, page, ct), cancellationToken);

    public Task<ActivityMapSummary> GetMapSummaryAsync(ActivityQuery owner, string rawMapId, ActivityScope scope,
        CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetMapSummaryCoreAsync(owner, rawMapId, scope, ct), cancellationToken);

    private async Task<ActivityPage<ActivityMap>> GetMapsCoreAsync(ActivityQuery owner, int page,
        CancellationToken cancellationToken)
    {
        if (owner.SessionId is null && owner.PlaySessionId is null && owner.PlaySessionIds is null) return new([], false);
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var sessions = await FilterSessionOwners(context, owner).ToArrayAsync(cancellationToken);
        if (sessions.Length == 0) return new([], false);
        var sessionIds = sessions.Select(x => (Guid?)x.Id).ToArray();

        // Read one small row per map, not its event history. This also retains maps from
        // imported/older recordings which have events but no visit metadata.
        var events = await FilterEvents(context, owner with { Scope = ActivityScope.AllObserved })
            .GroupBy(x => x.RawMapId).Select(g => new
            {
                RawMapId = g.Key,
                Name = g.Where(x => x.LocationName != "" && x.LocationName != x.RawMapId).Min(x => x.LocationName),
                StartedAtUtc = g.Min(x => x.CapturedAtUtc),
                LastObservedAtUtc = g.Max(x => x.CapturedAtUtc)
            }).ToArrayAsync(cancellationToken);
        // Older visits only stored their name and identity. Their small location
        // payloads can supply the entrance without loading combat/item history.
        var locations = await context.ActivityEvents.AsNoTracking()
            .Where(x => sessionIds.Contains(x.SessionId) && !x.IsRetracted && x.Kind == ActivityEventKind.Location)
            .OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.CaptureOrder)
            .Select(x => new { x.RawMapId, x.ParentMapId, x.DetailsJson }).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var metadata = new Dictionary<string, ActivityMapMetadata>(StringComparer.Ordinal);
        foreach (var location in locations)
        {
            MapLocationDetails? details;
            try { details = JsonSerializer.Deserialize<MapLocationDetails>(location.DetailsJson); }
            catch (JsonException) { details = null; }
            var value = details?.MapMetadata ?? new ActivityMapMetadata();
            value = value with { OriginMapId = value.OriginMapId ?? details?.SourceClusterId ?? details?.ParentClusterId ?? location.ParentMapId };
            metadata.TryGetValue(location.RawMapId, out var previous);
            metadata[location.RawMapId] = (previous ?? new ActivityMapMetadata()).Merge(value);
        }

        var maps = events.ToDictionary(x => x.RawMapId, x => new ActivityMap(x.RawMapId,
            MapName(x.Name), x.StartedAtUtc, x.LastObservedAtUtc, 0, 0, null), StringComparer.Ordinal);
        // Each run closes its own visits at its recording boundary.
        var visits = sessions.SelectMany(session =>
        {
            var end = session.ClosedAtUtc ?? session.LastObservedAtUtc;
            if (session.State == ActivityState.Interrupted && end > session.LastObservedAtUtc) end = session.LastObservedAtUtc;
            return ReadArray<ActivityVisit>(session.VisitsJson).Select(x => x with
            {
                EndedAtUtc = session.State == ActivityState.Interrupted && (x.EndedAtUtc is null || x.EndedAtUtc > end) ? end :
                    x.EndedAtUtc ?? (session.State == ActivityState.Open && session.ObservationStartedAtUtc is not null ? null : end)
            }).Where(x => x.EndedAtUtc is null || x.EndedAtUtc >= x.StartedAtUtc)
                .Select(x => (Visit: x, session.LastObservedAtUtc));
        }).ToArray();
        foreach (var group in visits.GroupBy(x => x.Visit.RawMapId, StringComparer.Ordinal))
        {
            var ordered = group.Select(x => x.Visit).OrderBy(x => x.StartedAtUtc).ToArray();
            var intervals = MergeMapIntervals(ordered);
            var openStart = intervals.FirstOrDefault(x => x.EndedAtUtc is null)?.StartedAtUtc;
            var duration = intervals.Where(x => x.EndedAtUtc is not null)
                .Sum(x => Math.Max(0, (x.EndedAtUtc!.Value - x.StartedAtUtc).TotalSeconds));
            var lastObserved = group.Max(x => x.Visit.EndedAtUtc ??
                (x.LastObservedAtUtc > x.Visit.StartedAtUtc ? x.LastObservedAtUtc : x.Visit.StartedAtUtc));
            maps.TryGetValue(group.Key, out var fallback);
            var name = ordered.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.LocationName) && x.LocationName != x.RawMapId)?.LocationName;
            metadata.TryGetValue(group.Key, out var visitMetadata);
            foreach (var visit in ordered) visitMetadata = (visitMetadata ?? new ActivityMapMetadata()).Merge(visit.Metadata);
            if (visitMetadata is not null) metadata[group.Key] = visitMetadata;
            maps[group.Key] = new(group.Key, MapName(name ?? fallback?.Name),
                fallback is not null && fallback.StartedAtUtc < ordered[0].StartedAtUtc ? fallback.StartedAtUtc : ordered[0].StartedAtUtc,
                fallback is not null && fallback.LastObservedAtUtc > lastObserved ? fallback.LastObservedAtUtc : lastObserved,
                ordered.Length, duration, openStart);
        }
        var instanceNumbers = new Dictionary<ActivityMapKind, int>();
        var orderedMaps = maps.Values.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.RawMapId, StringComparer.Ordinal).Select(value =>
        {
            var location = AlbionLocations.ResolveActivityLocation(value.RawMapId);
            var name = value.Name;
            // Number the distinct generated areas in visit order, before pagination.
            // A separate identity is not evidence of a dungeon floor number.
            if (location.IsInstance && location.InstanceId is not null && value.RawMapId.Contains('@'))
            {
                instanceNumbers.TryGetValue(location.Kind, out var number);
                instanceNumbers[location.Kind] = ++number;
                name = $"{location.DisplayName} #{number}";
            }
            metadata.TryGetValue(value.RawMapId, out var captured);
            var detail = new ActivityMapMetadata(Tier: location.Tier, ZoneType: location.ZoneType).Merge(captured);
            if (!location.IsInstance || string.Equals(detail.OriginMapId, value.RawMapId, StringComparison.OrdinalIgnoreCase))
                detail = detail with { OriginMapId = null };
            return value with { Name = name, Metadata = detail };
        }).ToArray();
        var rows = orderedMaps.Skip((int)Math.Min(int.MaxValue, Math.Max(0L, page) * 100)).Take(101).ToArray();
        return new(rows.Take(100).ToArray(), rows.Length > 100);
    }

    private async Task<ActivityMapSummary> GetMapSummaryCoreAsync(ActivityQuery owner, string rawMapId, ActivityScope scope,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var events = FilterEvents(context, owner with { Scope = scope })
            .Where(x => x.RawMapId == rawMapId);
        var summary = await AggregateSummaryAsync(events, scope, cancellationToken);
        var participating = events.Where(ActivityClassification.CombatExpression(scope)).Where(x => x.EncounterId != null)
            .Select(x => x.EncounterId!.Value).Distinct();
        // Scope selects participation; shared encounter boundaries supply the same
        // first-to-last combat span used by the session's other summaries.
        var encounters = context.ActivityEncounters.AsNoTracking().Where(x => x.RawMapId == rawMapId && participating.Contains(x.Id));
        if (owner.SessionId is { } sessionId) encounters = encounters.Where(x => x.SessionId == sessionId);
        encounters = InPlaySessions(context, encounters, owner);
        var combatSeconds = await encounters
            .SumAsync(x => Math.Max(1, (ActivityDatabaseFunctions.JulianDay(x.LastCombatAtUtc) -
                ActivityDatabaseFunctions.JulianDay(x.StartedAtUtc)) * 86400), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(summary, combatSeconds);
    }

    private static string MapName(string? name) => string.IsNullOrWhiteSpace(name) ? "Unknown map" : name;

    private static T[] ReadArray<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private sealed record MapLocationDetails(ActivityMapMetadata? MapMetadata = null, string? SourceClusterId = null,
        string? ParentClusterId = null);

    private static IReadOnlyList<ActivityObservationInterval> MergeMapIntervals(IEnumerable<ActivityVisit> visits)
    {
        var result = new List<ActivityObservationInterval>();
        foreach (var visit in visits)
        {
            if (result.Count > 0 && (result[^1].EndedAtUtc is null || result[^1].EndedAtUtc >= visit.StartedAtUtc))
                result[^1] = result[^1] with
                {
                    EndedAtUtc = result[^1].EndedAtUtc is null || visit.EndedAtUtc is null ? null :
                        result[^1].EndedAtUtc > visit.EndedAtUtc ? result[^1].EndedAtUtc : visit.EndedAtUtc
                };
            else result.Add(new(visit.StartedAtUtc, visit.EndedAtUtc));
        }
        return result;
    }
}
