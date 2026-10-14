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
    public Task<ActivityPage<ActivityMap>> GetMapsAsync(Guid sessionId, int page = 0,
        CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetMapsCoreAsync(sessionId, page, ct), cancellationToken);

    public Task<ActivityMapSummary> GetMapSummaryAsync(Guid sessionId, string rawMapId, ActivityScope scope,
        CancellationToken cancellationToken = default) =>
        ReadAsync(ct => GetMapSummaryCoreAsync(sessionId, rawMapId, scope, ct), cancellationToken);

    private async Task<ActivityPage<ActivityMap>> GetMapsCoreAsync(Guid sessionId, int page,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var session = await context.ActivitySessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null) return new([], false);

        // Read one small row per map, not its event history. This also retains maps from
        // imported/older recordings which have events but no visit metadata.
        var events = await FilterEvents(context, new ActivityQuery { SessionId = sessionId, Scope = ActivityScope.AllObserved })
            .GroupBy(x => x.RawMapId).Select(g => new
            {
                RawMapId = g.Key,
                Name = g.Where(x => x.LocationName != "" && x.LocationName != x.RawMapId).Min(x => x.LocationName),
                StartedAtUtc = g.Min(x => x.CapturedAtUtc),
                LastObservedAtUtc = g.Max(x => x.CapturedAtUtc)
            }).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var maps = events.ToDictionary(x => x.RawMapId, x => new ActivityMap(x.RawMapId,
            MapName(x.Name), x.StartedAtUtc, x.LastObservedAtUtc, 0, 0, null), StringComparer.Ordinal);
        var end = session.ClosedAtUtc ?? session.LastObservedAtUtc;
        if (session.State == ActivityState.Interrupted && end > session.LastObservedAtUtc) end = session.LastObservedAtUtc;
        var visits = ReadArray<ActivityVisit>(session.VisitsJson).Select(x => x with
        {
            EndedAtUtc = session.State == ActivityState.Interrupted && (x.EndedAtUtc is null || x.EndedAtUtc > end) ? end :
                x.EndedAtUtc ?? (session.State == ActivityState.Open && session.ObservationStartedAtUtc is not null ? null : end)
        }).Where(x => x.EndedAtUtc is null || x.EndedAtUtc >= x.StartedAtUtc);
        foreach (var group in visits.GroupBy(x => x.RawMapId, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(x => x.StartedAtUtc).ToArray();
            var intervals = MergeMapIntervals(ordered);
            var openStart = intervals.FirstOrDefault(x => x.EndedAtUtc is null)?.StartedAtUtc;
            var duration = intervals.Where(x => x.EndedAtUtc is not null)
                .Sum(x => Math.Max(0, (x.EndedAtUtc!.Value - x.StartedAtUtc).TotalSeconds));
            var lastObserved = ordered.Max(x => x.EndedAtUtc ??
                (session.LastObservedAtUtc > x.StartedAtUtc ? session.LastObservedAtUtc : x.StartedAtUtc));
            maps.TryGetValue(group.Key, out var fallback);
            var name = ordered.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.LocationName) && x.LocationName != x.RawMapId)?.LocationName;
            maps[group.Key] = new(group.Key, MapName(name ?? fallback?.Name),
                fallback is not null && fallback.StartedAtUtc < ordered[0].StartedAtUtc ? fallback.StartedAtUtc : ordered[0].StartedAtUtc,
                fallback is not null && fallback.LastObservedAtUtc > lastObserved ? fallback.LastObservedAtUtc : lastObserved,
                ordered.Length, duration, openStart);
        }
        var rows = maps.Values.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.RawMapId, StringComparer.Ordinal)
            .Skip((int)Math.Min(int.MaxValue, Math.Max(0L, page) * 100)).Take(101).ToArray();
        return new(rows.Take(100).ToArray(), rows.Length > 100);
    }

    private async Task<ActivityMapSummary> GetMapSummaryCoreAsync(Guid sessionId, string rawMapId, ActivityScope scope,
        CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var events = FilterEvents(context, new ActivityQuery { SessionId = sessionId, Scope = scope })
            .Where(x => x.RawMapId == rawMapId);
        var summary = await AggregateSummaryAsync(events, scope, cancellationToken);
        var participating = events.Where(ActivityClassification.CombatExpression(scope)).Where(x => x.EncounterId != null)
            .Select(x => x.EncounterId!.Value).Distinct();
        // Scope selects participation; shared encounter boundaries supply the same
        // first-to-last combat span used by the session's other summaries.
        var combatSeconds = await context.ActivityEncounters.AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.RawMapId == rawMapId && participating.Contains(x.Id))
            .SumAsync(x => Math.Max(1, (ActivityDatabaseFunctions.JulianDay(x.LastCombatAtUtc) -
                ActivityDatabaseFunctions.JulianDay(x.StartedAtUtc)) * 86400), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(summary, combatSeconds);
    }

    private static string MapName(string? name) => string.IsNullOrWhiteSpace(name) ? "Unknown map" : name;

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
