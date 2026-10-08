using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    internal async Task<ActivityLiveTimelineSnapshot> GetLiveTimelineAsync(Guid? sessionId, ActivityScope scope,
        DateTime endUtc, ActivitySession? liveSession, ActivityEncounter? liveEncounter, CancellationToken cancellationToken)
    {
        endUtc = ActivityLiveTimeline.WindowEnd(endUtc);
        if (sessionId is not { } id) return ActivityLiveTimeline.Empty(null, scope, endUtc);
        WriteCommand[] accepted;
        lock (acceptedSync)
        {
            if (deletedSessions.Contains(id)) return ActivityLiveTimeline.Empty(id, scope, endUtc);
            accepted = acceptedWrites.ToArray();
        }
        var fromUtc = endUtc.AddSeconds(-ActivityLiveTimeline.WindowSeconds);
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        // Deferred read transactions keep all aggregates and replacement baselines on the same
        // WAL snapshot without reserving the writer lock.
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var header = await context.ActivitySessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        foreach (var pendingHeader in accepted.OfType<SessionCommand>().Where(x => x.Value.Id == id))
            if (header is null || pendingHeader.Value.Revision > header.Revision) header = pendingHeader.Value;
        if (liveSession?.Id == id && (header is null || liveSession.Revision >= header.Revision)) header = liveSession;
        if (header is null) return ActivityLiveTimeline.Empty(id, scope, endUtc);

        var filter = new ActivityQuery { SessionId = id, Scope = scope, FromUtc = fromUtc, ToUtc = endUtc };
        var combat = FilterEvents(context, filter).Where(x => x.Kind == ActivityEventKind.Damage || x.Kind == ActivityEventKind.Healing);
        var dealt = ActivityProjection.ChartExpression(scope, null, ActivityChartMetric.DamageDealt);
        var received = ActivityProjection.ChartExpression(scope, null, ActivityChartMetric.DamageReceived);
        var healed = ActivityProjection.ChartExpression(scope, null, ActivityChartMetric.HealingDone);
        var healingReceived = ActivityProjection.ChartExpression(scope, null, ActivityChartMetric.HealingReceived);
        var firstSecond = fromUtc.Hour * 3600 + fromUtc.Minute * 60 + fromUtc.Second;
        // Calendar second fields translate to SQLite strftime, avoiding julianday floating point
        // boundary drift. Modulo handles a five-minute window crossing UTC midnight.
        var healthRows = await combat.GroupBy(x =>
                (x.CapturedAtUtc.Hour * 3600 + x.CapturedAtUtc.Minute * 60 + x.CapturedAtUtc.Second - firstSecond + 86400) % 86400)
            .Select(g => new
            {
                Bucket = g.Key,
                DamageDealt = g.AsQueryable().Select(dealt).Sum(),
                DamageReceived = g.AsQueryable().Select(received).Sum(),
                HealingDone = g.AsQueryable().Select(healed).Sum(),
                HealingReceived = g.AsQueryable().Select(healingReceived).Sum()
            }).Take(ActivityLiveTimeline.WindowSeconds).ToArrayAsync(cancellationToken);
        var samples = Enumerable.Range(0, ActivityLiveTimeline.WindowSeconds).Select(i =>
            new ActivityLiveTimelineSample(fromUtc.AddSeconds(i), 0, 0, 0, 0)).ToArray();
        foreach (var row in healthRows)
            if (row.Bucket is >= 0 and < ActivityLiveTimeline.WindowSeconds)
                samples[row.Bucket] = new(fromUtc.AddSeconds(row.Bucket), row.DamageDealt, row.DamageReceived, row.HealingDone, row.HealingReceived);

        var markerQuery = FilterEvents(context, filter).Where(x =>
                x.Kind == ActivityEventKind.MobKilled || x.Kind == ActivityEventKind.PlayerKilled ||
                x.Kind == ActivityEventKind.Death || x.Kind == ActivityEventKind.Knockdown ||
                x.Kind == ActivityEventKind.Loot || x.Kind == ActivityEventKind.Fame || x.Kind == ActivityEventKind.Silver)
            .OrderByDescending(x => x.Kind == ActivityEventKind.Death || x.Kind == ActivityEventKind.PlayerKilled || x.Kind == ActivityEventKind.Knockdown)
            .ThenByDescending(x => x.CapturedAtUtc).ThenByDescending(x => x.CaptureOrder).ThenBy(x => x.Id);
        var markerRows = new List<ActivityEvent>();
        while (markerRows.Count <= ActivityLiveTimeline.MaximumMarkerRows)
        {
            var size = Math.Min(250, ActivityLiveTimeline.MaximumMarkerRows + 1 - markerRows.Count);
            var page = await markerQuery.Skip(markerRows.Count).Take(size).ToArrayAsync(cancellationToken);
            markerRows.AddRange(page);
            if (page.Length < size) break;
        }
        var truncated = markerRows.Count > ActivityLiveTimeline.MaximumMarkerRows;
        var markerEvents = markerRows.Take(ActivityLiveTimeline.MaximumMarkerRows).ToDictionary(x => x.Id);
        var gapRows = await context.ActivityEvents.AsNoTracking().Where(x => x.SessionId == id &&
                x.Kind == ActivityEventKind.CaptureGap && !x.IsRetracted && x.CapturedAtUtc < endUtc)
            .OrderByDescending(x => x.CapturedAtUtc).Take(301).ToArrayAsync(cancellationToken);
        var gapEvents = gapRows.ToDictionary(x => x.Id);

        // Read only the identities touched by the bounded pending queue. In particular, health
        // history stays aggregated in SQL rather than materializing every party hit.
        var pendingIds = accepted.OfType<EventCommand>().Select(x => x.Value.Id)
            .Concat(accepted.OfType<RetractCommand>().Select(x => x.Id)).Distinct().ToArray();
        var pendingOrigins = accepted.OfType<EventCommand>().Where(x => !x.IsAmendment && x.Value.OriginKey != null)
            .Select(x => x.Value.OriginKey!).Distinct().ToArray();
        var baselines = pendingIds.Length == 0 && pendingOrigins.Length == 0 ? [] :
            await context.ActivityEvents.AsNoTracking().Where(x => pendingIds.Contains(x.Id) ||
                x.OriginKey != null && pendingOrigins.Contains(x.OriginKey)).ToArrayAsync(cancellationToken);
        var canonical = baselines.ToDictionary(x => x.Id);
        var origins = baselines.Where(x => x.OriginKey != null).ToDictionary(x => x.OriginKey!, x => x.Id, StringComparer.Ordinal);
        foreach (var command in accepted)
        {
            if (command is EventCommand mutation)
            {
                var value = mutation.Value;
                canonical.TryGetValue(value.Id, out var previous);
                if (mutation.IsAmendment && previous is null || !mutation.IsAmendment && previous is not null) continue;
                if (mutation.IsPriceOnly)
                {
                    if (previous is null || previous.EstimatedUnitValue is not null || value.EstimatedUnitValue is null) continue;
                    if (previous.Quantity > 0 && value.EstimatedUnitValue > long.MaxValue / previous.Quantity) continue;
                    value = previous with { EstimatedUnitValue = value.EstimatedUnitValue, PriceObservedAtUtc = value.PriceObservedAtUtc };
                }
                else if (previous is not null)
                {
                    if (previous.Revision >= value.Revision) continue;
                    var frozenPrice = previous.EstimatedUnitValue is not null && previous.ItemId == value.ItemId &&
                        previous.ItemUniqueName == value.ItemUniqueName && previous.Quality == value.Quality;
                    value = value with
                    {
                        SessionId = previous.SessionId,
                        EncounterId = previous.EncounterId,
                        CapturedAtUtc = previous.CapturedAtUtc,
                        CaptureOrder = previous.CaptureOrder,
                        OriginKey = previous.OriginKey,
                        MapEpoch = previous.MapEpoch,
                        CaptureGeneration = previous.CaptureGeneration,
                        EstimatedUnitValue = frozenPrice ? previous.EstimatedUnitValue : value.EstimatedUnitValue,
                        PriceObservedAtUtc = frozenPrice ? previous.PriceObservedAtUtc : value.PriceObservedAtUtc
                    };
                }
                else if (value.OriginKey is { } origin && !origins.TryAdd(origin, value.Id)) continue;
                if (!double.IsFinite(value.Amount) || value.Amount < 0 || value.Quantity < 0) continue;
                canonical[value.Id] = value;
            }
            else if (command is RetractCommand retract && canonical.TryGetValue(retract.Id, out var previous) &&
                previous.Revision < retract.Revision && !previous.IsRetracted)
                canonical[retract.Id] = previous with { Revision = retract.Revision, IsRetracted = true };
        }
        var before = baselines.ToDictionary(x => x.Id);
        foreach (var value in canonical.Values)
        {
            if (before.TryGetValue(value.Id, out var old)) AddHealth(old, -1);
            AddHealth(value, 1);
            markerEvents.Remove(value.Id);
            if (InWindow(value) && !value.IsRetracted && !ActivityLiveTimeline.IsCombat(value)) markerEvents[value.Id] = value;
            gapEvents.Remove(value.Id);
            if (value.SessionId == id && value.Kind == ActivityEventKind.CaptureGap && !value.IsRetracted && value.CapturedAtUtc < endUtc)
                gapEvents[value.Id] = value;
        }

        var encounterQuery = context.ActivityEncounters.AsNoTracking().Where(x => x.SessionId == id &&
                x.StartedAtUtc < endUtc && x.LastCombatAtUtc >= fromUtc)
            .OrderBy(x => x.Number);
        var encounterRows = new List<ActivityEncounter>();
        while (encounterRows.Count <= 300)
        {
            var size = Math.Min(100, 301 - encounterRows.Count);
            var page = await encounterQuery.Skip(encounterRows.Count).Take(size).ToArrayAsync(cancellationToken);
            encounterRows.AddRange(page);
            if (page.Length < size) break;
        }
        truncated |= encounterRows.Count > 300;
        var encounters = encounterRows.Take(300).ToDictionary(x => x.Id);
        foreach (var pendingHeader in accepted.OfType<EncounterCommand>().Where(x => x.Value.SessionId == id))
            if (!encounters.TryGetValue(pendingHeader.Value.Id, out var old) || pendingHeader.Value.Revision > old.Revision)
                encounters[pendingHeader.Value.Id] = pendingHeader.Value;
        if (liveEncounter?.SessionId == id && (!encounters.TryGetValue(liveEncounter.Id, out var saved) || liveEncounter.Revision >= saved.Revision))
            encounters[liveEncounter.Id] = liveEncounter;
        var markers = ActivityLiveTimeline.Markers(markerEvents.Values, scope, out var markersTruncated);
        lock (acceptedSync)
            if (deletedSessions.Contains(id)) return ActivityLiveTimeline.Empty(id, scope, endUtc);
        return new(id, scope, fromUtc, endUtc, samples, markers,
            encounters.Values.Where(x => x.StartedAtUtc < endUtc && x.LastCombatAtUtc >= fromUtc).OrderBy(x => x.Number)
                .Select(x => new ActivityLiveTimelineEncounter(x.Id, x.Number, x.StartedAtUtc, x.LastCombatAtUtc, x.Kind, x.State)).ToArray(),
            ActivityLiveTimeline.Gaps(header, fromUtc, endUtc, gapEvents.Values.ToArray()), header.IsPartial, truncated || markersTruncated);

        bool InWindow(ActivityEvent value) => value.SessionId == id && value.CapturedAtUtc >= fromUtc && value.CapturedAtUtc < endUtc;
        void AddHealth(ActivityEvent value, int sign)
        {
            if (!InWindow(value) || !ActivityLiveTimeline.IsCombat(value)) return;
            var index = (int)((value.CapturedAtUtc.Ticks - fromUtc.Ticks) / TimeSpan.TicksPerSecond);
            var contribution = ActivityProjection.Contribution(value, scope);
            var sample = samples[index];
            samples[index] = sample with
            {
                DamageDealt = sample.DamageDealt + sign * contribution.DamageDealt,
                DamageReceived = sample.DamageReceived + sign * contribution.DamageReceived,
                HealingDone = sample.HealingDone + sign * contribution.HealingDone,
                HealingReceived = sample.HealingReceived + sign * contribution.HealingReceived
            };
        }
    }

    public async Task<ActivitySession?> GetSessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivitySessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
    public async Task<ActivityEncounter?> GetEncounterAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivityEncounters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
    public async Task<ActivityEncounter?> GetLatestEncounterAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivityEncounters.AsNoTracking().Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.Number).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<double> GetCombatDurationSecondsAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var combatEvents = FilterEvents(context, filter).Where(x => x.EncounterId != null &&
            (x.Kind == ActivityEventKind.Damage || x.Kind == ActivityEventKind.Healing));
        var encounters = context.ActivityEncounters.AsNoTracking().AsQueryable();
        if (filter.SessionId is { } sessionId) encounters = encounters.Where(x => x.SessionId == sessionId);
        if (filter.EncounterId is { } encounterId) encounters = encounters.Where(x => x.Id == encounterId);
        // Encounter boundaries are shared, but an encounter contributes time only when this scope participated.
        // First-to-last combat spans omit travel, downtime, and the seven-second closing delay.
        return await encounters.Where(x => combatEvents.Any(e => e.EncounterId == x.Id))
            .SumAsync(x => Math.Max(1, (ActivityDatabaseFunctions.JulianDay(x.LastCombatAtUtc) -
                ActivityDatabaseFunctions.JulianDay(x.StartedAtUtc)) * 86400), cancellationToken);
    }
    public async Task<ActivityEvent?> GetEventAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivityEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
    public async Task<ActivityPage<ActivitySession>> GetSessionsAsync(ActivityQuery filter, int page = 0, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var query = context.ActivitySessions.AsNoTracking().AsQueryable();
        if (filter.SessionId is { } id) query = query.Where(x => x.Id == id);
        if (filter.FromUtc is { } from) query = query.Where(x => x.StartedAtUtc >= from);
        if (filter.ToUtc is { } to) query = query.Where(x => x.StartedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(filter.Server)) query = query.Where(x => x.Server == filter.Server);
        if (!string.IsNullOrWhiteSpace(filter.CharacterId)) query = query.Where(x => x.CharacterId == filter.CharacterId);
        if (!string.IsNullOrWhiteSpace(filter.CharacterName)) query = query.Where(x => x.CharacterName.Contains(filter.CharacterName));
        if (!string.IsNullOrWhiteSpace(filter.Location)) query = query.Where(x => x.LocationName.Contains(filter.Location) || x.RootMapId.Contains(filter.Location));
        if (filter.SessionKind is { } kind) query = query.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(x => x.Title.Contains(filter.Search) || x.CharacterName.Contains(filter.Search));
        var rows = await query.OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip(Math.Max(0, page) * 100).Take(101).ToListAsync(cancellationToken);
        return new(rows.Take(100).ToArray(), rows.Count > 100);
    }
    public async Task<ActivityPage<ActivityEncounter>> GetEncountersAsync(Guid sessionId, int page = 0, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var rows = await context.ActivityEncounters.AsNoTracking().Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Number).Skip(Math.Max(0, page) * 100).Take(101).ToListAsync(cancellationToken);
        return new(rows.Take(100).ToArray(), rows.Count > 100);
    }
    public async Task<ActivityPage<ActivityEvent>> GetEventsAsync(ActivityQuery filter, int page = 0, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var rows = await FilterEvents(context, filter).OrderByDescending(x => x.CapturedAtUtc).ThenByDescending(x => x.CaptureOrder)
            .ThenBy(x => x.Id).Skip(Math.Max(0, page) * 250).Take(251).ToListAsync(cancellationToken);
        return new(rows.Take(250).ToArray(), rows.Count > 250);
    }

    private static IQueryable<ActivityEvent> FilterEvents(LocalContext context, ActivityQuery filter)
    {
        var query = context.ActivityEvents.AsNoTracking().Where(x => !x.IsRetracted);
        if (filter.SessionId is { } session) query = query.Where(x => x.SessionId == session);
        if (filter.EncounterId is { } encounter) query = query.Where(x => x.EncounterId == encounter);
        if (filter.FromUtc is { } from) query = query.Where(x => x.CapturedAtUtc >= from);
        if (filter.ToUtc is { } to) query = query.Where(x => x.CapturedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(filter.Server)) query = query.Where(x => x.Server == filter.Server);
        if (!string.IsNullOrWhiteSpace(filter.CharacterId)) query = query.Where(x => x.CharacterId == filter.CharacterId);
        if (!string.IsNullOrWhiteSpace(filter.CharacterName)) query = query.Where(x => x.CharacterName.Contains(filter.CharacterName));
        if (!string.IsNullOrWhiteSpace(filter.ActorKey))
            query = query.Where(x => x.SourceActorId == filter.ActorKey || x.TargetActorId == filter.ActorKey ||
                (x.Kind == ActivityEventKind.MobKilled && x.TargetActorId != null && context.ActivityEvents.Any(d =>
                    !d.IsRetracted && d.Kind == ActivityEventKind.Damage && d.SourceActorId == filter.ActorKey &&
                    d.TargetActorId == x.TargetActorId && d.SessionId == x.SessionId && d.CapturedAtUtc <= x.CapturedAtUtc)));
        if (!string.IsNullOrWhiteSpace(filter.Location)) query = query.Where(x => x.LocationName.Contains(filter.Location) || x.RawMapId.Contains(filter.Location));
        if (filter.Kind is { } kind) query = query.Where(x => x.Kind == kind);
        if (filter.LootOnly) query = query.Where(x => x.Kind == ActivityEventKind.Loot);
        if (filter.SessionKind is { } sessionKind)
            query = query.Where(x => x.SessionId != null && context.ActivitySessions.Any(s => s.Id == x.SessionId && s.Kind == sessionKind));
        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(x => x.SourceName.Contains(filter.Search) || x.TargetName.Contains(filter.Search) ||
                x.ItemName.Contains(filter.Search) || (x.ItemUniqueName != null && x.ItemUniqueName.Contains(filter.Search)) ||
                x.LootSourceName.Contains(filter.Search) || x.SourceGuild.Contains(filter.Search) || x.SourceAlliance.Contains(filter.Search));
        if (filter.Scope == ActivityScope.Personal)
            query = query.Where(x => x.SourceRelationship == ActorRelationship.Self || x.TargetRelationship == ActorRelationship.Self ||
                (x.Kind == ActivityEventKind.MobKilled && x.ParticipatedSelf) || x.Kind == ActivityEventKind.Location || x.Kind == ActivityEventKind.CaptureGap);
        else if (filter.Scope == ActivityScope.Party)
            query = query.Where(x => x.SourceRelationship == ActorRelationship.Self || x.SourceRelationship == ActorRelationship.Party ||
                x.TargetRelationship == ActorRelationship.Self || x.TargetRelationship == ActorRelationship.Party ||
                (x.Kind == ActivityEventKind.MobKilled && (x.ParticipatedSelf || x.ParticipatedParty)) ||
                x.Kind == ActivityEventKind.Location || x.Kind == ActivityEventKind.CaptureGap);
        return query;
    }

    public async Task<ActivitySummary> GetSummaryAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await FilterEvents(context, filter).Select(ActivityProjection.ContributionExpression(filter.Scope, filter.ActorKey))
            .GroupBy(_ => 1).Select(g => new ActivitySummary
            {
                DamageDealt = g.Sum(x => x.DamageDealt),
                DamageReceived = g.Sum(x => x.DamageReceived),
                HealingDone = g.Sum(x => x.HealingDone),
                HealingReceived = g.Sum(x => x.HealingReceived),
                Fame = g.Sum(x => x.Fame),
                SilverRaw = g.Sum(x => x.SilverRaw),
                UnknownSilverEvents = g.Sum(x => x.UnknownSilverEvents),
                LootValue = g.Sum(x => x.LootValue),
                LootItems = g.Sum(x => x.LootItems),
                MissingPriceItems = g.Sum(x => x.MissingPriceItems),
                MobKills = g.Sum(x => x.MobKills),
                PlayerKills = g.Sum(x => x.PlayerKills),
                Deaths = g.Sum(x => x.Deaths),
                Knockdowns = g.Sum(x => x.Knockdowns)
            }).SingleOrDefaultAsync(cancellationToken) ?? new();
    }

    public async Task<IReadOnlyList<ActivityChartPoint>> GetChartsAsync(ActivityQuery filter, ActivityChartMetric metric, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var query = FilterEvents(context, filter);
        var range = await query.GroupBy(_ => 1).Select(g => new { Start = g.Min(x => x.CapturedAtUtc), End = g.Max(x => x.CapturedAtUtc) })
            .SingleOrDefaultAsync(cancellationToken);
        if (range is null) return [];
        var seconds = Math.Max(1, (int)Math.Ceiling(((range.End - range.Start).TotalSeconds + 1) / 600));
        var expression = ActivityProjection.ChartExpression(filter.Scope, filter.ActorKey, metric);
        var rows = await query.GroupBy(x => (long)Math.Max(0, (ActivityDatabaseFunctions.JulianDay(x.CapturedAtUtc) -
                ActivityDatabaseFunctions.JulianDay(range.Start)) * 86400 / seconds))
            .Select(g => new { Bucket = g.Key, Value = g.AsQueryable().Select(expression).Sum() }).OrderBy(x => x.Bucket)
            .Take(600).ToListAsync(cancellationToken);
        var values = rows.ToDictionary(x => x.Bucket, x => x.Value);
        var count = Math.Min(600, (int)Math.Floor((range.End - range.Start).TotalSeconds / seconds) + 1);
        return Enumerable.Range(0, count).Select(i => new ActivityChartPoint(range.Start.AddSeconds((long)i * seconds), values.GetValueOrDefault(i))).ToArray();
    }

    public async Task<IReadOnlyList<ActivityActorTotal>> GetPlayersAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var query = FilterEvents(context, filter).Where(x => x.Kind == ActivityEventKind.Damage || x.Kind == ActivityEventKind.Healing);
        var party = filter.Scope != ActivityScope.Personal;
        var all = filter.Scope == ActivityScope.AllObserved;
        var source = await query.Where(x => x.SourceActorId != null && x.SourceKind == ActivityActorKind.Player &&
                (all || x.SourceRelationship == ActorRelationship.Self || (party && x.SourceRelationship == ActorRelationship.Party)))
            .GroupBy(x => new { x.SourceActorId, x.SourceName, x.SourceRelationship })
            .Select(g => new
            {
                ActorId = g.Key.SourceActorId!,
                Name = g.Key.SourceName,
                Relationship = g.Key.SourceRelationship,
                DamageDealt = g.Sum(x => x.Kind == ActivityEventKind.Damage ? x.Amount : 0),
                DamageReceived = 0d,
                HealingDone = g.Sum(x => x.Kind == ActivityEventKind.Healing ? x.Amount : 0),
                HealingReceived = 0d
            })
            .OrderByDescending(x => x.DamageDealt + x.HealingDone).Take(250).ToListAsync(cancellationToken);
        var targets = await query.Where(x => x.TargetActorId != null && x.TargetKind == ActivityActorKind.Player &&
                (all || x.TargetRelationship == ActorRelationship.Self || (party && x.TargetRelationship == ActorRelationship.Party)))
            .GroupBy(x => new { x.TargetActorId, x.TargetName, x.TargetRelationship })
            .Select(g => new
            {
                ActorId = g.Key.TargetActorId!,
                Name = g.Key.TargetName,
                Relationship = g.Key.TargetRelationship,
                DamageDealt = 0d,
                DamageReceived = g.Sum(x => x.Kind == ActivityEventKind.Damage ? x.Amount : 0),
                HealingDone = 0d,
                HealingReceived = g.Sum(x => x.Kind == ActivityEventKind.Healing ? x.Amount : 0)
            })
            .OrderByDescending(x => x.DamageReceived + x.HealingReceived).Take(250).ToListAsync(cancellationToken);
        return source.Concat(targets).GroupBy(x => new { x.ActorId, x.Name, x.Relationship })
            .Select(g => new ActivityActorTotal(g.Key.ActorId, g.Key.Name, g.Key.Relationship,
                g.Sum(x => x.DamageDealt), g.Sum(x => x.DamageReceived), g.Sum(x => x.HealingDone), g.Sum(x => x.HealingReceived)))
            .OrderByDescending(x => x.DamageDealt + x.HealingDone).Take(250).ToArray();
    }

    public async Task<IReadOnlyList<ActivityMobTotal>> GetMobsAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var rows = await FilterEvents(context, filter).Where(x => x.Kind == ActivityEventKind.MobKilled)
            .GroupBy(x => new { Id = x.MobUniqueName ?? "Unknown", Name = x.TargetName })
            .Select(g => new { g.Key.Id, g.Key.Name, Kills = g.LongCount(), InferredKills = g.LongCount(x => x.Evidence == ActivityEvidence.Inferred) })
            .OrderByDescending(x => x.Kills).Take(250).ToListAsync(cancellationToken);
        return rows.Select(x => new ActivityMobTotal(x.Id, x.Name, x.Kills, x.InferredKills)).ToArray();
    }
}

public static class ActivityDatabaseFunctions
{
    public static double JulianDay(DateTime value) => throw new InvalidOperationException("This function is translated to SQLite julianday().");
}
