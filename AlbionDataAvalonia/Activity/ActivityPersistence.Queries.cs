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
    private Task<ActivityLiveTimelineSnapshot> GetLiveTimelineCoreAsync(Guid? sessionId, ActivityScope scope,
        DateTime endUtc, ActivitySession? liveSession, ActivityEncounter? liveEncounter, CancellationToken cancellationToken,
        ActivityCategories categories = ActivityCategories.All)
        => sessionId is null ? Task.FromResult(ActivityLiveTimeline.Empty(null, scope, ActivityLiveTimeline.WindowEnd(endUtc)) with { Categories = categories }) :
            GetLiveTimelineCoreAsync(new ActivityQuery { SessionId = sessionId, Scope = scope, ShowCategories = categories }, endUtc,
                liveSession, liveEncounter, cancellationToken);

    private async Task<ActivityLiveTimelineSnapshot> GetLiveTimelineCoreAsync(ActivityQuery query,
        DateTime endUtc, ActivitySession? liveSession, ActivityEncounter? liveEncounter, CancellationToken cancellationToken,
        double windowSeconds = ActivityLiveTimeline.WindowSeconds)
    {
        if (query.ToUtc is { } selectedTo && selectedTo < endUtc) endUtc = selectedTo;
        endUtc = ActivityLiveTimeline.WindowEnd(endUtc);
        var id = query.SessionId;
        var scope = query.Scope;
        var categories = query.ShowCategories;
        if (!HasQueryContext(query)) return ActivityLiveTimeline.Empty(id, scope, endUtc) with { Categories = categories };
        if (!string.IsNullOrWhiteSpace(query.CharacterId)) query = query with { CharacterName = null };
        var pendingRead = CapturePendingRead();
        var accepted = pendingRead.Accepted;
        var deleted = pendingRead.Deleted;
        if (id is { } deletedId && deleted.Contains(deletedId)) return ActivityLiveTimeline.Empty(id, scope, endUtc) with { Categories = categories };
        windowSeconds = Math.Max(1, windowSeconds);
        var fromUtc = new DateTime(Math.Max(DateTime.MinValue.Ticks,
            endUtc.Ticks - (long)(windowSeconds * TimeSpan.TicksPerSecond)), DateTimeKind.Utc);
        windowSeconds = Math.Max(1, (endUtc - fromUtc).TotalSeconds);
        var sampleSeconds = Math.Max(1, Math.Ceiling(windowSeconds / 600));
        var sampleCount = Math.Min(600, (int)Math.Ceiling(windowSeconds / sampleSeconds));
        var selectedFromUtc = query.FromUtc is { } selectedFrom && selectedFrom > fromUtc ? selectedFrom : fromUtc;
        query = query with { FromUtc = selectedFromUtc, ToUtc = endUtc };
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        // Deferred read transactions keep all aggregates and replacement baselines on the same
        // WAL snapshot without reserving the writer lock.
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var headers = (await FilterSessionOwners(context, query).Where(x => !deleted.Contains(x.Id) &&
                x.StartedAtUtc < endUtc && (x.ClosedAtUtc == null || x.ClosedAtUtc >= selectedFromUtc) &&
                !context.ActivityEvents.Any(e => e.SessionId == x.Id && e.Kind == ActivityEventKind.LegacyGatheringAggregate))
            .ToArrayAsync(cancellationToken)).ToDictionary(x => x.Id);
        foreach (var pendingHeader in accepted.OfType<SessionCommand>().Select(x => x.Value).Where(x => MatchesOwner(x, query) && !deleted.Contains(x.Id) &&
                     x.StartedAtUtc < endUtc && (x.ClosedAtUtc is null || x.ClosedAtUtc >= selectedFromUtc)))
            if (!headers.TryGetValue(pendingHeader.Id, out var previousHeader) || pendingHeader.Revision > previousHeader.Revision) headers[pendingHeader.Id] = pendingHeader;
        if (liveSession is not null && MatchesOwner(liveSession, query) && !deleted.Contains(liveSession.Id) &&
            (!headers.TryGetValue(liveSession.Id, out var savedHeader) || liveSession.Revision >= savedHeader.Revision)) headers[liveSession.Id] = liveSession;
        var headerIds = headers.Keys.ToArray();

        // Read only the identities touched by the bounded pending queue. In particular, health
        // history stays aggregated in SQL rather than materializing every party hit.
        var (before, canonical) = await CanonicalPendingAsync(context, pendingRead, cancellationToken);

        // Category evidence is read before filtered aggregates, then adjusted by accepted mutations.
        // A pending first hit or retraction changes the category of every event in that encounter.
        var recentEncounterIds = await FilterEventOwners(context, query).Where(x =>
                x.EncounterId != null && !x.IsRetracted && x.CapturedAtUtc >= selectedFromUtc && x.CapturedAtUtc < endUtc &&
                (x.SessionId == null || !deleted.Contains(x.SessionId.Value)))
            .Select(x => x.EncounterId!.Value).Distinct().ToArrayAsync(cancellationToken);
        var proofIds = recentEncounterIds.Concat(canonical.Values.Where(InWindow).Where(x => x.EncounterId != null)
            .Select(x => x.EncounterId!.Value)).Distinct().ToArray();
        var pveContribution = ActivityProjection.CountExpression(scope, nameof(ActivitySummary.PveEvents));
        var pvpContribution = ActivityProjection.CountExpression(scope, nameof(ActivitySummary.PvpEvents));
        var combatExpression = ActivityClassification.CombatExpression(scope);
        var isScopedCombat = combatExpression.Compile();
        var proofs = await FilterEventOwners(context, query).Where(x => x.EncounterId != null && proofIds.Contains(x.EncounterId.Value))
            .GroupBy(x => x.EncounterId!.Value).Select(g => new
            {
                Id = g.Key,
                Pve = g.AsQueryable().Select(pveContribution).Sum(),
                Pvp = g.AsQueryable().Select(pvpContribution).Sum(),
                Combat = g.AsQueryable().Where(combatExpression).LongCount()
            }).ToArrayAsync(cancellationToken);
        var proofCounts = proofs.ToDictionary(x => x.Id, x => (x.Pve, x.Pvp, x.Combat));
        foreach (var value in canonical.Values)
        {
            if (before.TryGetValue(value.Id, out var old)) AddProof(old, -1);
            AddProof(value, 1);
        }
        var pveIds = proofCounts.Where(x => x.Value.Pve > 0).Select(x => x.Key).ToArray();
        var pvpIds = proofCounts.Where(x => x.Value.Pvp > 0).Select(x => x.Key).ToArray();
        var visibleEncounterIds = proofCounts.Where(x => x.Value.Combat > 0 &&
            (categories == ActivityCategories.All || (categories & ActivityCategories.Combat) != 0 ||
                (categories & ActivityCategories.Pve) != 0 && x.Value.Pve > 0 ||
                (categories & ActivityCategories.Pvp) != 0 && x.Value.Pvp > 0 ||
                (categories & ActivityCategories.Other) != 0 && x.Value.Pve == 0 && x.Value.Pvp == 0))
            .Select(x => x.Key).ToArray();
        var categoryExpression = ActivityClassification.CategoryExpression(categories, pveIds, pvpIds);
        var matchesCategory = categoryExpression.Compile();
        var filtered = FilterEvents(context, query with { ShowCategories = ActivityCategories.All }).Where(categoryExpression)
            .Where(x => x.SessionId == null || !deleted.Contains(x.SessionId.Value));
        var combat = filtered.Where(x => x.Kind == ActivityEventKind.Damage || x.Kind == ActivityEventKind.Healing ||
            x.Kind == ActivityEventKind.Gathered || x.Kind == ActivityEventKind.FishingReward);
        var dealt = ActivityProjection.ChartExpression(scope, query.ActorKey, ActivityChartMetric.DamageDealt);
        var received = ActivityProjection.ChartExpression(scope, query.ActorKey, ActivityChartMetric.DamageReceived);
        var healed = ActivityProjection.ChartExpression(scope, query.ActorKey, ActivityChartMetric.HealingDone);
        var healingReceived = ActivityProjection.ChartExpression(scope, query.ActorKey, ActivityChartMetric.HealingReceived);
        var gathering = ActivityProjection.ChartExpression(scope, query.ActorKey, ActivityChartMetric.GatheringItems);
        var fishing = ActivityProjection.ChartExpression(scope, query.ActorKey, ActivityChartMetric.FishingItems);
        var pendingContribution = string.IsNullOrWhiteSpace(query.ActorKey) ? null : ActivityProjection.ContributionExpression(scope, query.ActorKey).Compile();
        var firstSecond = fromUtc.Hour * 3600 + fromUtc.Minute * 60 + fromUtc.Second;
        var firstDate = fromUtc.Date;
        // Only whole calendar days use julianday, where midnight differences are exact. Calendar
        // second fields preserve bucket boundaries, including multi-day history and UTC midnight.
        var healthRows = await combat.GroupBy(x =>
                (int)(((ActivityDatabaseFunctions.JulianDay(x.CapturedAtUtc.Date) - ActivityDatabaseFunctions.JulianDay(firstDate)) * 86400 +
                    x.CapturedAtUtc.Hour * 3600 + x.CapturedAtUtc.Minute * 60 + x.CapturedAtUtc.Second - firstSecond) / sampleSeconds))
            .Select(g => new
            {
                Bucket = g.Key,
                DamageDealt = g.AsQueryable().Select(dealt).Sum(),
                DamageReceived = g.AsQueryable().Select(received).Sum(),
                HealingDone = g.AsQueryable().Select(healed).Sum(),
                HealingReceived = g.AsQueryable().Select(healingReceived).Sum(),
                Gathering = g.AsQueryable().Select(gathering).Sum(),
                Fishing = g.AsQueryable().Select(fishing).Sum()
            }).Take(sampleCount).ToArrayAsync(cancellationToken);
        var samples = Enumerable.Range(0, sampleCount).Select(i =>
            new ActivityLiveTimelineSample(fromUtc.AddSeconds(i * sampleSeconds), 0, 0, 0, 0)).ToArray();
        foreach (var row in healthRows)
            if (row.Bucket >= 0 && row.Bucket < sampleCount)
            {
                var seconds = BucketSeconds(row.Bucket);
                samples[row.Bucket] = new(fromUtc.AddSeconds(row.Bucket * sampleSeconds), row.DamageDealt / seconds,
                    row.DamageReceived / seconds, row.HealingDone / seconds, row.HealingReceived / seconds, row.Gathering / seconds, row.Fishing / seconds);
            }

        var markerQuery = filtered.Where(x =>
                x.Kind == ActivityEventKind.MobKilled || x.Kind == ActivityEventKind.PlayerKilled ||
                x.Kind == ActivityEventKind.Death || x.Kind == ActivityEventKind.Knockdown ||
                x.Kind == ActivityEventKind.Loot || x.Kind == ActivityEventKind.Fame || x.Kind == ActivityEventKind.Silver ||
                x.Kind == ActivityEventKind.Gathered || x.Kind == ActivityEventKind.FishingReward)
            .OrderByDescending(x => x.Kind == ActivityEventKind.Death || x.Kind == ActivityEventKind.PlayerKilled || x.Kind == ActivityEventKind.Knockdown)
            .ThenByDescending(x => x.CapturedAtUtc).ThenByDescending(x => x.CaptureOrder).ThenBy(x => x.Id);
        // The result is already bounded. Offset pages would repeat the same sort for each 250 rows.
        var markerRows = await markerQuery.Take(ActivityLiveTimeline.MaximumMarkerRows + 1).ToArrayAsync(cancellationToken);
        var truncated = markerRows.Length > ActivityLiveTimeline.MaximumMarkerRows;
        var markerEvents = markerRows.Take(ActivityLiveTimeline.MaximumMarkerRows).ToDictionary(x => x.Id);
        var gapRows = await FilterEventOwners(context, query).Where(x =>
                x.Kind == ActivityEventKind.CaptureGap && !x.IsRetracted && x.CapturedAtUtc < endUtc)
            .OrderByDescending(x => x.CapturedAtUtc).Take(301).ToArrayAsync(cancellationToken);
        var gapEvents = gapRows.ToDictionary(x => x.Id);

        foreach (var value in canonical.Values)
        {
            if (before.TryGetValue(value.Id, out var old)) AddHealth(old, -1);
            AddHealth(value, 1);
            markerEvents.Remove(value.Id);
            if (InWindow(value) && !value.IsRetracted && ActivityClassification.IsTemporal(value.Kind) &&
                matchesCategory(value) && !ActivityLiveTimeline.IsCombat(value)) markerEvents[value.Id] = value;
            gapEvents.Remove(value.Id);
            if (MatchesOwner(value, query) && value.Kind == ActivityEventKind.CaptureGap && !value.IsRetracted && value.CapturedAtUtc < endUtc)
                gapEvents[value.Id] = value;
        }

        var encounterQuery = context.ActivityEncounters.AsNoTracking().Where(x => headerIds.Contains(x.SessionId) && visibleEncounterIds.Contains(x.Id) &&
                x.StartedAtUtc < endUtc && x.LastCombatAtUtc >= fromUtc)
            .OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Number);
        var encounterRows = await encounterQuery.Take(301).ToArrayAsync(cancellationToken);
        truncated |= encounterRows.Length > 300;
        var encounters = encounterRows.Take(300).ToDictionary(x => x.Id);
        foreach (var pendingHeader in accepted.OfType<EncounterCommand>().Where(x => headerIds.Contains(x.Value.SessionId)))
            if (!encounters.TryGetValue(pendingHeader.Value.Id, out var old) || pendingHeader.Value.Revision > old.Revision)
                encounters[pendingHeader.Value.Id] = pendingHeader.Value;
        if (liveEncounter is not null && headerIds.Contains(liveEncounter.SessionId) && (!encounters.TryGetValue(liveEncounter.Id, out var saved) || liveEncounter.Revision >= saved.Revision))
            encounters[liveEncounter.Id] = liveEncounter;
        var markers = ActivityLiveTimeline.Markers(markerEvents.Values, scope, out var markersTruncated, windowSeconds, query.ActorKey);
        var observed = ActivityPeriodProjection.MergeIntervals(ActivityPeriodProjection.ObservationIntervals(headers.Values, liveSession), selectedFromUtc, endUtc);
        var gaps = new List<ActivityLiveTimelineGap>();
        var cursor = fromUtc;
        foreach (var interval in observed)
        {
            if (interval.StartedAtUtc > cursor) gaps.Add(new(cursor, interval.StartedAtUtc,
                cursor < selectedFromUtc ? "Before selected period" : gapEvents.Values.Any(x => Math.Abs((x.CapturedAtUtc - cursor).TotalSeconds) < 0.001) ? "Capture paused" : "No tracked activity"));
            cursor = interval.EndedAtUtc!.Value;
        }
        if (cursor < endUtc) gaps.Add(new(cursor, endUtc, "No tracked activity"));
        return new ActivityLiveTimelineSnapshot(id, scope, fromUtc, endUtc, samples, markers,
            encounters.Values.Where(x => visibleEncounterIds.Contains(x.Id) && x.StartedAtUtc < endUtc && x.LastCombatAtUtc >= selectedFromUtc)
                .OrderBy(x => x.StartedAtUtc).ThenBy(x => x.Number)
                .Select(x => new ActivityLiveTimelineEncounter(x.Id, x.Number, x.StartedAtUtc, x.LastCombatAtUtc, x.Kind, x.State)).ToArray(),
            gaps, headers.Values.Any(x => x.IsPartial), truncated || markersTruncated, categories)
        {
            HasContext = true,
            ContextKey = $"{id}:{query.EncounterId}:{scope}:{query.ActorKey}:{query.Server}:{query.CharacterId}:{query.CharacterName}:{categories}:{windowSeconds}",
            SampleSeconds = sampleSeconds
        };

        bool InWindow(ActivityEvent value) => MatchesOwner(value, query) && value.CapturedAtUtc >= selectedFromUtc && value.CapturedAtUtc < endUtc &&
            (value.SessionId is null || !deleted.Contains(value.SessionId.Value));
        void AddHealth(ActivityEvent value, int sign)
        {
            if (!InWindow(value) || !matchesCategory(value) || !ActivityClassification.IsTemporal(value.Kind)) return;
            var index = (int)((value.CapturedAtUtc.Ticks - fromUtc.Ticks) / TimeSpan.TicksPerSecond / sampleSeconds);
            if (index < 0 || index >= samples.Length) return;
            var contribution = pendingContribution?.Invoke(value) ?? ActivityProjection.Contribution(value, scope);
            var seconds = BucketSeconds(index);
            var sample = samples[index];
            samples[index] = sample with
            {
                DamageDealt = sample.DamageDealt + sign * contribution.DamageDealt / seconds,
                DamageReceived = sample.DamageReceived + sign * contribution.DamageReceived / seconds,
                HealingDone = sample.HealingDone + sign * contribution.HealingDone / seconds,
                HealingReceived = sample.HealingReceived + sign * contribution.HealingReceived / seconds,
                GatheringQuantity = sample.GatheringQuantity + sign * contribution.GatheringItems / seconds,
                FishingQuantity = sample.FishingQuantity + sign * contribution.FishingItems / seconds
            };
        }
        double BucketSeconds(int index) => Math.Max(1, Math.Min(sampleSeconds, windowSeconds - index * sampleSeconds));

        void AddProof(ActivityEvent value, int sign)
        {
            if (!MatchesOwner(value, query) || value.EncounterId is not { } encounterId || !proofIds.Contains(encounterId)) return;
            var contribution = ActivityProjection.Contribution(value, scope);
            var previous = proofCounts.GetValueOrDefault(encounterId);
            proofCounts[encounterId] = (previous.Pve + sign * contribution.PveEvents, previous.Pvp + sign * contribution.PvpEvents,
                previous.Combat + (isScopedCombat(value) ? sign : 0));
        }
    }

    private async Task<ActivitySession?> GetSessionCoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivitySessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
    private async Task<ActivityEncounter?> GetEncounterCoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivityEncounters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
    private async Task<ActivityEncounter?> GetLatestEncounterCoreAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivityEncounters.AsNoTracking().Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.Number).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<double> GetCombatDurationSecondsCoreAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var combatEvents = FilterEvents(context, filter).Where(x => x.EncounterId != null &&
            (x.Kind == ActivityEventKind.Damage || x.Kind == ActivityEventKind.Healing));
        var participatingIds = combatEvents.Select(x => x.EncounterId!.Value).Distinct();
        var encounters = context.ActivityEncounters.AsNoTracking().AsQueryable();
        if (filter.SessionId is { } sessionId) encounters = encounters.Where(x => x.SessionId == sessionId);
        if (filter.EncounterId is { } encounterId) encounters = encounters.Where(x => x.Id == encounterId);
        // Encounter boundaries are shared, but an encounter contributes time only when this scope participated.
        // First-to-last combat spans omit travel, downtime, and the seven-second closing delay.
        // Build participation once instead of rescanning a session's events for each encounter.
        return await encounters.Where(x => participatingIds.Contains(x.Id))
            .SumAsync(x => Math.Max(1, (ActivityDatabaseFunctions.JulianDay(x.LastCombatAtUtc) -
                ActivityDatabaseFunctions.JulianDay(x.StartedAtUtc)) * 86400), cancellationToken);
    }
    private async Task<ActivityEvent?> GetEventCoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.ActivityEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
    private async Task<ActivityPage<ActivitySession>> GetSessionsCoreAsync(ActivityQuery filter, int page = 0, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var rows = await FilterSessions(context, filter).OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id)
            .Skip(Math.Max(0, page) * 100).Take(101).ToListAsync(cancellationToken);
        return new(rows.Take(100).ToArray(), rows.Count > 100);
    }

    private async Task<IReadOnlyList<ActivitySession>> GetSessionsByIdsCoreAsync(ActivityQuery filter, Guid[] ids, CancellationToken cancellationToken)
    {
        await using var context = contextFactory();
        return await FilterSessions(context, filter).Where(x => ids.Contains(x.Id)).ToArrayAsync(cancellationToken);
    }

    private static IQueryable<ActivitySession> FilterSessions(LocalContext context, ActivityQuery filter)
    {
        var query = context.ActivitySessions.AsNoTracking().AsQueryable();
        if (filter.SessionId is { } id) query = query.Where(x => x.Id == id);
        if (filter.FromUtc is { } from) query = query.Where(x => x.StartedAtUtc >= from);
        if (filter.ToUtc is { } to) query = query.Where(x => x.StartedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(filter.Server)) query = query.Where(x => x.Server == filter.Server);
        if (!string.IsNullOrWhiteSpace(filter.CharacterId)) query = query.Where(x => x.CharacterId == filter.CharacterId);
        if (!string.IsNullOrWhiteSpace(filter.CharacterName)) query = filter.MatchCharacterNameExactly
            ? query.Where(x => x.CharacterName == filter.CharacterName)
            : query.Where(x => x.CharacterName.Contains(filter.CharacterName));
        if (!string.IsNullOrWhiteSpace(filter.Location)) query = query.Where(x => x.LocationName.Contains(filter.Location) || x.RootMapId.Contains(filter.Location));
        if (filter.SessionKind is { } kind) query = query.Where(x => x.Kind == kind);
        if ((filter.ContainsTags & ActivityTags.Gathering) != 0)
        {
            var evidence = context.ActivityEvents.Where(ActivityClassification.GatheringExpression(filter.Scope));
            query = query.Where(s => evidence.Any(e => e.SessionId == s.Id));
        }
        if ((filter.ContainsTags & ActivityTags.Pve) != 0)
        {
            var evidence = context.ActivityEvents.Where(ActivityClassification.PveExpression(filter.Scope));
            query = query.Where(s => evidence.Any(e => e.SessionId == s.Id));
        }
        if ((filter.ContainsTags & ActivityTags.Pvp) != 0)
        {
            var evidence = context.ActivityEvents.Where(ActivityClassification.PvpExpression(filter.Scope));
            query = query.Where(s => evidence.Any(e => e.SessionId == s.Id));
        }
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(x => x.Title.Contains(filter.Search) || x.CharacterName.Contains(filter.Search));
        return query;
    }
    private async Task<ActivityPage<ActivityEncounter>> GetEncountersCoreAsync(Guid sessionId, int page = 0, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var rows = await context.ActivityEncounters.AsNoTracking().Where(x => x.SessionId == sessionId)
            .OrderBy(x => x.Number).Skip(Math.Max(0, page) * 100).Take(101).ToListAsync(cancellationToken);
        return new(rows.Take(100).ToArray(), rows.Count > 100);
    }
    private async Task<ActivityPage<ActivityEvent>> GetEventsCoreAsync(ActivityQuery filter, int page = 0, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var rows = await FilterEvents(context, filter).OrderByDescending(x => x.CapturedAtUtc).ThenByDescending(x => x.CaptureOrder)
            .ThenBy(x => x.Id).Skip(Math.Max(0, page) * 250).Take(251).ToListAsync(cancellationToken);
        return new(rows.Take(250).ToArray(), rows.Count > 250);
    }

    private static IQueryable<ActivityEvent> FilterEvents(LocalContext context, ActivityQuery filter)
    {
        var query = context.ActivityEvents.AsNoTracking().Where(x => !x.IsRetracted && x.Kind != ActivityEventKind.LegacyImportMarker);
        if (filter.SessionId is { } session) query = query.Where(x => x.SessionId == session);
        if (filter.EncounterId is { } encounter) query = query.Where(x => x.EncounterId == encounter);
        if (filter.FromUtc is { } from) query = query.Where(x => x.CapturedAtUtc >= from);
        if (filter.ToUtc is { } to) query = query.Where(x => x.CapturedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(filter.Server)) query = query.Where(x => x.Server == filter.Server);
        if (!string.IsNullOrWhiteSpace(filter.CharacterId)) query = query.Where(x => x.CharacterId == filter.CharacterId);
        if (!string.IsNullOrWhiteSpace(filter.CharacterName)) query = filter.MatchCharacterNameExactly
            ? query.Where(x => x.CharacterName == filter.CharacterName)
            : query.Where(x => x.CharacterName.Contains(filter.CharacterName));
        if (!string.IsNullOrWhiteSpace(filter.ActorKey))
            query = query.Where(x => x.SourceActorId == filter.ActorKey || x.TargetActorId == filter.ActorKey ||
                (x.Kind == ActivityEventKind.MobKilled && x.TargetActorId != null && context.ActivityEvents.Any(d =>
                    !d.IsRetracted && d.Kind == ActivityEventKind.Damage && d.SourceActorId == filter.ActorKey &&
                    d.TargetActorId == x.TargetActorId && d.SessionId == x.SessionId && d.CapturedAtUtc <= x.CapturedAtUtc)));
        if (!string.IsNullOrWhiteSpace(filter.Location)) query = query.Where(x => x.LocationName.Contains(filter.Location) || x.RawMapId.Contains(filter.Location));
        if (filter.Kind is { } kind) query = query.Where(x => x.Kind == kind);
        if (filter.LootOnly) query = query.Where(x => x.Kind == ActivityEventKind.Loot);
        if (filter.ItemsOnly) query = query.Where(x => x.Kind == ActivityEventKind.Loot || x.Kind == ActivityEventKind.Gathered ||
            x.Kind == ActivityEventKind.FishingReward || x.Kind == ActivityEventKind.LegacyGatheringAggregate);
        if (filter.ItemsOnly && filter.ItemSources != ActivityItemSources.All)
        {
            var sources = filter.ItemSources;
            var loot = (sources & ActivityItemSources.Loot) != 0;
            var gathering = (sources & ActivityItemSources.Gathering) != 0;
            var fishing = (sources & ActivityItemSources.Fishing) != 0;
            var mixed = (sources & ActivityItemSources.LegacyMixed) != 0;
            query = query.Where(x => loot && x.Kind == ActivityEventKind.Loot ||
                gathering && (x.Kind == ActivityEventKind.Gathered || x.Kind == ActivityEventKind.LegacyGatheringAggregate &&
                    ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.Method") == (long)ActivityResourceMethod.Gathering) ||
                fishing && (x.Kind == ActivityEventKind.FishingReward || x.Kind == ActivityEventKind.LegacyGatheringAggregate &&
                    ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.Method") == (long)ActivityResourceMethod.Fishing) ||
                mixed && x.Kind == ActivityEventKind.LegacyGatheringAggregate &&
                    (ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.Method") == 0 || ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.Method") == (long)ActivityResourceMethod.Mixed));
        }
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
        return ActivityClassification.FilterCategories(query, context.ActivityEvents.AsNoTracking(), filter);
    }

    private async Task<ActivitySummary> GetSummaryCoreAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await FilterEvents(context, filter).Select(ActivityProjection.ContributionExpression(filter.Scope, filter.ActorKey))
            .GroupBy(_ => 1).Select(g => new ActivitySummary
            {
                TagVersion = ActivityClassification.TagVersion,
                GatheringEvents = g.Sum(x => x.GatheringEvents),
                PveEvents = g.Sum(x => x.PveEvents),
                PvpEvents = g.Sum(x => x.PvpEvents),
                GatheringItems = g.Sum(x => x.GatheringItems),
                FishingItems = g.Sum(x => x.FishingItems),
                UnclassifiedResourceItems = g.Sum(x => x.UnclassifiedResourceItems),
                GatheringValue = g.Sum(x => x.GatheringValue),
                FishingValue = g.Sum(x => x.FishingValue),
                UnclassifiedResourceValue = g.Sum(x => x.UnclassifiedResourceValue),
                MissingResourcePriceItems = g.Sum(x => x.MissingResourcePriceItems),
                LegacyAggregateEvents = g.Sum(x => x.LegacyAggregateEvents),
                DamageDealt = g.Sum(x => x.DamageDealt),
                DamageReceived = g.Sum(x => x.DamageReceived),
                HealingDone = g.Sum(x => x.HealingDone),
                HealingReceived = g.Sum(x => x.HealingReceived),
                Fame = g.Sum(x => x.Fame),
                PveFame = g.Sum(x => x.PveFame),
                GatheringFame = g.Sum(x => x.GatheringFame),
                FishingFame = g.Sum(x => x.FishingFame),
                PvpFame = g.Sum(x => x.PvpFame),
                UnknownFameAmount = g.Sum(x => x.UnknownFameAmount) ?? 0,
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

    private async Task<IReadOnlyList<ActivityChartPoint>> GetChartsCoreAsync(ActivityQuery filter, ActivityChartMetric metric, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var query = FilterEvents(context, filter).Where(x => x.Kind != ActivityEventKind.LegacyGatheringAggregate);
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

    private async Task<IReadOnlyList<ActivityActorTotal>> GetPlayersCoreAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
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

    public Task<ActivityPage<ActivityEvent>> GetItemsAsync(ActivityQuery filter, int page = 0, CancellationToken cancellationToken = default) =>
        GetEventsAsync(filter with { ItemsOnly = true, LootOnly = false }, page, cancellationToken);

    private async Task<IReadOnlyList<ActivityResourceTotal>> GetResourceTotalsCoreAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var query = FilterEvents(context, filter).Where(ActivityClassification.GatheringExpression(filter.Scope, filter.ActorKey));
        var contribution = ActivityProjection.CountExpression(filter.Scope, nameof(ActivitySummary.ResourceValue), filter.ActorKey);
        var grouped = query.GroupBy(x => new
        {
            x.ItemId,
            x.Quality,
            x.ItemUniqueName,
            x.ItemName,
            Source = x.Kind == ActivityEventKind.Gathered ? ActivityResourceMethod.Gathering : x.Kind == ActivityEventKind.FishingReward ? ActivityResourceMethod.Fishing :
                    (ActivityResourceMethod)(ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.Method") ?? 0)
        })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.Quality,
                g.Key.ItemUniqueName,
                g.Key.ItemName,
                g.Key.Source,
                Quantity = g.Sum(x => x.Quantity),
                KnownValue = g.AsQueryable().Select(contribution).Sum(),
                Missing = g.Sum(x => (x.Kind == ActivityEventKind.LegacyGatheringAggregate ? ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.Legacy.RecordedTotalValue") == null : x.EstimatedUnitValue == null) ? x.Quantity : 0)
            })
            .OrderByDescending(x => x.KnownValue).ThenBy(x => x.ItemName).ThenBy(x => x.ItemId).ThenBy(x => x.Quality).ThenBy(x => x.Source);
        // Only one row per resource variant is materialized, not individual pickups.
        var rows = await grouped.ToArrayAsync(cancellationToken);
        return rows.Select(x => new ActivityResourceTotal(x.ItemId, x.Quality, x.ItemUniqueName, x.ItemName,
            x.Source, x.Quantity, x.KnownValue, x.Missing)).ToArray();
    }

    private async Task<ActivityLegacyImportInfo?> GetLegacyImportInfoCoreAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var details = await context.ActivityEvents.AsNoTracking().Where(x => x.SessionId == sessionId && x.Kind == ActivityEventKind.LegacyGatheringAggregate)
            .OrderBy(x => x.CaptureOrder).Select(x => x.DetailsJson).FirstOrDefaultAsync(cancellationToken);
        try { return details is null ? null : System.Text.Json.JsonSerializer.Deserialize<ActivityResourceDetails>(details)?.Legacy; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private async Task<IReadOnlyList<ActivityMobTotal>> GetMobsCoreAsync(ActivityQuery filter, CancellationToken cancellationToken = default)
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
    public static long? JsonLong(string json, string path)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var value = document.RootElement;
            foreach (var part in path.TrimStart('$', '.').Split('.'))
                if (value.ValueKind != System.Text.Json.JsonValueKind.Object || !value.TryGetProperty(part, out value)) return null;
            return value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
