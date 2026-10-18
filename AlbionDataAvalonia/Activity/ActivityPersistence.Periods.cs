using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
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
    internal Task<ActivityPeriodSnapshot> GetPeriodSnapshotAsync(ActivityQuery query, DateTime nowUtc,
        ActivitySession? liveSession, long contextVersion, CancellationToken cancellationToken) =>
        ReadAsync(ct => GetPeriodSnapshotCoreAsync(query, nowUtc, liveSession, null, contextVersion, ct), cancellationToken);

    internal Task<ActivityPeriodSnapshot> GetPeriodSnapshotWithContextAsync(ActivityQuery query, DateTime nowUtc,
        ActivitySession? liveSession, ActivityEncounter? liveEncounter, long contextVersion, CancellationToken cancellationToken) =>
        ReadAsync(ct => GetPeriodSnapshotCoreAsync(query, nowUtc, liveSession, liveEncounter, contextVersion, ct), cancellationToken);

    internal Task<ActivityLiveTimelineSnapshot> GetLiveTimelineAsync(ActivityQuery query, DateTime endUtc,
        ActivitySession? liveSession, ActivityEncounter? liveEncounter, CancellationToken cancellationToken,
        int windowSeconds = ActivityLiveTimeline.WindowSeconds) =>
        ReadAsync(ct => GetLiveTimelineCoreAsync(query, endUtc, liveSession, liveEncounter, ct,
            Math.Clamp(windowSeconds, 300, 7200)), cancellationToken);

    private static bool HasQueryContext(ActivityQuery query) => query.SessionId is not null || query.PlaySessionId is not null || query.PlaySessionIds is not null ||
        !string.IsNullOrWhiteSpace(query.Server) && (!string.IsNullOrWhiteSpace(query.CharacterId) ||
            query.MatchCharacterNameExactly && !string.IsNullOrWhiteSpace(query.CharacterName));

    // A play session query also needs the set of its runs, as one read sees them.
    private static bool MatchesOwner(ActivityEvent value, ActivityQuery query, IReadOnlySet<Guid>? playRuns) =>
        (query.PlaySessionId is null && query.PlaySessionIds is null || value.SessionId is { } run && playRuns?.Contains(run) == true) &&
        (query.SessionId is null || value.SessionId == query.SessionId) &&
        (query.EncounterId is null || value.EncounterId == query.EncounterId) &&
        (string.IsNullOrWhiteSpace(query.Server) || value.Server == query.Server) &&
        (string.IsNullOrWhiteSpace(query.CharacterId) || value.CharacterId == query.CharacterId) &&
        (string.IsNullOrWhiteSpace(query.CharacterName) || (query.MatchCharacterNameExactly
            ? value.CharacterName == query.CharacterName : value.CharacterName.Contains(query.CharacterName, StringComparison.Ordinal)));

    private static bool MatchesOwner(ActivitySession value, ActivityQuery query) =>
        InPlaySessions(value.PlaySessionId, query) &&
        (query.SessionId is null || value.Id == query.SessionId) &&
        (string.IsNullOrWhiteSpace(query.Server) || value.Server == query.Server) &&
        (string.IsNullOrWhiteSpace(query.CharacterId) || value.CharacterId == query.CharacterId) &&
        (string.IsNullOrWhiteSpace(query.CharacterName) || (query.MatchCharacterNameExactly
            ? value.CharacterName == query.CharacterName : value.CharacterName.Contains(query.CharacterName, StringComparison.Ordinal)));

    private static IQueryable<ActivitySession> FilterSessionOwners(LocalContext context, ActivityQuery query)
    {
        var result = RunsInPlaySessions(context.ActivitySessions.AsNoTracking(), query);
        if (query.SessionId is { } sessionId) result = result.Where(x => x.Id == sessionId);
        if (!string.IsNullOrWhiteSpace(query.Server)) result = result.Where(x => x.Server == query.Server);
        if (!string.IsNullOrWhiteSpace(query.CharacterId)) result = result.Where(x => x.CharacterId == query.CharacterId);
        if (!string.IsNullOrWhiteSpace(query.CharacterName)) result = query.MatchCharacterNameExactly
            ? result.Where(x => x.CharacterName == query.CharacterName)
            : result.Where(x => x.CharacterName.Contains(query.CharacterName));
        return result;
    }

    private static IQueryable<ActivityEvent> FilterEventOwners(LocalContext context, ActivityQuery query)
    {
        var result = InPlaySessions(context, context.ActivityEvents.AsNoTracking(), query);
        if (query.SessionId is { } sessionId) result = result.Where(x => x.SessionId == sessionId);
        if (query.EncounterId is { } encounterId) result = result.Where(x => x.EncounterId == encounterId);
        if (!string.IsNullOrWhiteSpace(query.Server)) result = result.Where(x => x.Server == query.Server);
        if (!string.IsNullOrWhiteSpace(query.CharacterId)) result = result.Where(x => x.CharacterId == query.CharacterId);
        if (!string.IsNullOrWhiteSpace(query.CharacterName)) result = query.MatchCharacterNameExactly
            ? result.Where(x => x.CharacterName == query.CharacterName)
            : result.Where(x => x.CharacterName.Contains(query.CharacterName));
        return result;
    }

    private sealed record PendingRead(WriteCommand[] Accepted, Guid[] Deleted, long Version, Guid[]? DeletedEvents = null);
    private PendingRead CapturePendingRead()
    {
        lock (acceptedSync)
        {
            var accepted = acceptedWrites.ToArray();
            var eventTombstones = accepted.OfType<EventCommand>().Select(command => command.Value.Id)
                .Where(deletedEvents.Contains).Distinct().ToArray();
            return new(accepted, deletedSessions.ToArray(), CurrentReadVersion, eventTombstones);
        }
    }

    // Exactly mirrors the writer's revision, origin deduplication, frozen attribution and price rules.
    // Only the queue's bounded identities are materialized; bulk history remains in SQL.
    private static async Task<(Dictionary<Guid, ActivityEvent> Before, Dictionary<Guid, ActivityEvent> After)> CanonicalPendingAsync(
        LocalContext context, PendingRead pendingRead, CancellationToken cancellationToken)
    {
        var accepted = pendingRead.Accepted;
        var ids = accepted.OfType<EventCommand>().Select(x => x.Value.Id)
            .Concat(accepted.OfType<RetractCommand>().Select(x => x.Id)).Distinct().ToArray();
        var pendingOrigins = accepted.OfType<EventCommand>().Where(x => !x.IsAmendment && x.Value.OriginKey != null)
            .Select(x => x.Value.OriginKey!).Distinct().ToArray();
        var baselines = ids.Length == 0 && pendingOrigins.Length == 0 ? [] :
            await context.ActivityEvents.AsNoTracking().Where(x => ids.Contains(x.Id) ||
                x.OriginKey != null && pendingOrigins.Contains(x.OriginKey)).ToArrayAsync(cancellationToken);
        var canonical = baselines.ToDictionary(x => x.Id);
        var origins = baselines.Where(x => x.OriginKey != null).ToDictionary(x => x.OriginKey!, x => x.Id, StringComparer.Ordinal);
        foreach (var command in accepted)
        {
            if (command is EventCommand mutation)
            {
                var value = mutation.Value;
                if (pendingRead.DeletedEvents?.Contains(value.Id) == true) continue;
                if (value.SessionId is { } sessionId && pendingRead.Deleted.Contains(sessionId)) continue;
                canonical.TryGetValue(value.Id, out var previous);
                if (mutation.IsAmendment && previous is null || !mutation.IsAmendment && previous is not null) continue;
                if (mutation.IsPriceOnly)
                {
                    if (previous is null || previous.IsRetracted || previous.EstimatedUnitValue is not null || value.EstimatedUnitValue is null ||
                        previous.Server != value.Server || previous.ItemId != value.ItemId ||
                        previous.ItemUniqueName != value.ItemUniqueName || previous.Quality != value.Quality) continue;
                    if (previous.Quantity > 0 && value.EstimatedUnitValue > long.MaxValue / previous.Quantity) continue;
                    value = ActivityItemValuationService.WithPrice(previous, value.EstimatedUnitValue.Value,
                        value.PriceObservedAtUtc ?? DateTime.UtcNow, ActivityItemValuationService.BasisQuality(previous),
                        ActivityItemValuationService.IsBlackMarketEmv(value));
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
                    if (frozenPrice)
                        value = ActivityItemValuationService.WithPrice(value, previous.EstimatedUnitValue!.Value,
                            previous.PriceObservedAtUtc ?? DateTime.UtcNow, ActivityItemValuationService.BasisQuality(previous),
                            ActivityItemValuationService.IsBlackMarketEmv(previous)) with
                        { PriceObservedAtUtc = previous.PriceObservedAtUtc };
                }
                else if (value.OriginKey is { } origin && !origins.TryAdd(origin, value.Id)) continue;
                if (!double.IsFinite(value.Amount) || value.Amount < 0 || value.Quantity < 0) continue;
                canonical[value.Id] = value;
            }
            else if (command is RetractCommand retract && canonical.TryGetValue(retract.Id, out var previous) &&
                previous.Revision < retract.Revision && !previous.IsRetracted)
                canonical[retract.Id] = previous with { Revision = retract.Revision, IsRetracted = true };
        }
        return (baselines.ToDictionary(x => x.Id), canonical);
    }

    private async Task<ActivityPeriodSnapshot> GetPeriodSnapshotCoreAsync(ActivityQuery query, DateTime nowUtc,
        ActivitySession? liveSession, ActivityEncounter? liveEncounter, long contextVersion, CancellationToken cancellationToken)
    {
        var pendingRead = CapturePendingRead();
        var version = pendingRead.Version + contextVersion;
        if (!HasQueryContext(query)) return new(version, new(), [], [], [], null, false, false);
        var importCharacterName = query.CharacterName;
        if (!string.IsNullOrWhiteSpace(query.CharacterId)) query = query with { CharacterName = null };
        var fromUtc = query.FromUtc ?? nowUtc;
        var toUtc = query.ToUtc is { } until && until < nowUtc ? until : nowUtc;
        if (fromUtc > toUtc) return new(version, new(), [], [], [], null, false, false);
        query = query with { FromUtc = fromUtc, ToUtc = toUtc };
        await using var context = contextFactory();
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        await context.Database.UseTransactionAsync(transaction, cancellationToken);
        var (before, after) = await CanonicalPendingAsync(context, pendingRead, cancellationToken);
        var deleted = pendingRead.Deleted;
        var playRuns = await PlayRunsAsync(context, query, pendingRead, liveSession, cancellationToken);
        var events = FilterEvents(context, query).Where(x => x.Kind != ActivityEventKind.LegacyGatheringAggregate &&
            (x.SessionId == null || !deleted.Contains(x.SessionId.Value)));
        var summary = await AggregateSummaryAsync(events, query.Scope, cancellationToken);
        var rows = await events.Where(ActivityClassification.GatheringExpression(query.Scope))
            .GroupBy(x => new
            {
                x.ItemId,
                x.Quality,
                x.ItemUniqueName,
                x.Kind,
                FallbackName = x.ItemId == null && (x.ItemUniqueName == null || x.ItemUniqueName == "") ? x.ItemName : ""
            })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.Quality,
                g.Key.ItemUniqueName,
                g.Key.Kind,
                Name = g.Max(x => x.ItemName),
                Quantity = g.Sum(x => x.Quantity),
                KnownValue = g.Sum(x => x.EstimatedUnitValue == null ? 0 : x.Quantity * x.EstimatedUnitValue.Value),
                Missing = g.Sum(x => x.EstimatedUnitValue == null ? x.Quantity : 0),
                FullJournalValue = g.Sum(x => x.EstimatedUnitValue != null && x.ItemUniqueName != null && x.ItemUniqueName.EndsWith("_FULL")
                    ? x.Quantity * (ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.FullJournalUnitValue") ?? 0) : 0),
                EmptyJournalValue = g.Sum(x => x.EstimatedUnitValue != null && x.ItemUniqueName != null && x.ItemUniqueName.EndsWith("_FULL")
                    ? x.Quantity * (ActivityDatabaseFunctions.JsonLong(x.DetailsJson, "$.EmptyJournalUnitValue") ?? 0) : 0)
            }).ToArrayAsync(cancellationToken);
        var resources = rows.Select(x => new ActivityResourceTotal(x.ItemId, x.Quality, x.ItemUniqueName, x.Name ?? "",
            x.Kind == ActivityEventKind.Gathered ? ActivityResourceMethod.Gathering : ActivityResourceMethod.Fishing,
            x.Quantity, x.KnownValue, x.Missing, x.FullJournalValue, x.EmptyJournalValue)).ToDictionary(ResourceKey);
        var contribution = ActivityProjection.ContributionExpression(query.Scope).Compile();
        foreach (var value in after.Values)
        {
            if (before.TryGetValue(value.Id, out var old)) Adjust(old, -1);
            Adjust(value, 1);
        }

        var headers = (await FilterSessionOwners(context, query).Where(x => !deleted.Contains(x.Id) &&
                x.StartedAtUtc < toUtc && (x.ClosedAtUtc == null || x.ClosedAtUtc >= fromUtc) &&
                !context.ActivityEvents.Any(e => e.SessionId == x.Id && e.Kind == ActivityEventKind.LegacyGatheringAggregate))
            .ToArrayAsync(cancellationToken)).ToDictionary(x => x.Id);
        foreach (var header in pendingRead.Accepted.OfType<SessionCommand>().Select(x => x.Value))
            AddHeader(header);
        if (liveSession is not null) AddHeader(liveSession);

        // Scope decides participation, while the encounter's shared boundaries decide duration.
        // A party member joining later must retain the same rate denominator as Current session.
        var touched = after.Keys.ToArray();
        var combatEvents = FilterEvents(context, query with { ShowCategories = ActivityCategories.All })
            .Where(ActivityClassification.CombatExpression(query.Scope)).Where(x => x.EncounterId != null &&
                !touched.Contains(x.Id) && (x.SessionId == null || !deleted.Contains(x.SessionId.Value)));
        var combatIds = (await combatEvents.Select(x => x.EncounterId!.Value).Distinct().ToArrayAsync(cancellationToken)).ToHashSet();
        var isCombat = ActivityClassification.CombatExpression(query.Scope).Compile();
        foreach (var value in after.Values.Where(x => InPeriod(x) && x.EncounterId != null && isCombat(x)))
            combatIds.Add(value.EncounterId!.Value);
        var qualified = combatIds.ToArray();
        var encounterHeaders = (await context.ActivityEncounters.AsNoTracking().Where(x => qualified.Contains(x.Id))
            .ToArrayAsync(cancellationToken)).ToDictionary(x => x.Id);
        foreach (var header in pendingRead.Accepted.OfType<EncounterCommand>().Select(x => x.Value).Where(x => qualified.Contains(x.Id)))
            if (!encounterHeaders.TryGetValue(header.Id, out var previous) || header.Revision >= previous.Revision) encounterHeaders[header.Id] = header;
        if (liveEncounter is not null && qualified.Contains(liveEncounter.Id) && headers.ContainsKey(liveEncounter.SessionId) &&
            (!encounterHeaders.TryGetValue(liveEncounter.Id, out var savedEncounter) || liveEncounter.Revision >= savedEncounter.Revision))
            encounterHeaders[liveEncounter.Id] = liveEncounter;
        var combat = encounterHeaders.Values.Select(x => new ActivityCombatInterval(x.Id, x.StartedAtUtc, x.LastCombatAtUtc)).ToArray();

        var earliest = await events.Where(x => !touched.Contains(x.Id)).Select(x => (DateTime?)x.CapturedAtUtc)
            .MinAsync(cancellationToken);
        foreach (var value in after.Values.Where(InPeriod))
            if (earliest is null || value.CapturedAtUtc < earliest) earliest = value.CapturedAtUtc;
        // This deadline is useful for rolling periods only; the caller decides whether to use it.
        var expiry = earliest?.Add(toUtc - fromUtc).AddTicks(1);
        var imports = context.ActivitySessions.AsNoTracking().Where(x => x.Server == query.Server &&
            x.CharacterName == importCharacterName && x.StartedAtUtc < toUtc &&
            (x.ClosedAtUtc ?? x.LastObservedAtUtc) >= fromUtc && !deleted.Contains(x.Id));
        var hasImports = !string.IsNullOrWhiteSpace(importCharacterName) && await imports.AnyAsync(s =>
            context.ActivityEvents.Any(e => e.SessionId == s.Id && !e.IsRetracted && e.Kind == ActivityEventKind.LegacyGatheringAggregate), cancellationToken);
        return new(version, summary, resources.Values.Where(x => x.Quantity > 0).OrderByDescending(x => x.KnownValue)
                .ThenBy(x => x.Name).ThenBy(x => x.ItemId).ThenBy(x => x.Quality).ToArray(),
            ActivityPeriodProjection.ObservationIntervals(headers.Values, liveSession),
            combat.Where(x => x.StartedAtUtc < toUtc && x.EndedAtUtc >= fromUtc).ToArray(), expiry,
            headers.Values.Any(x => x.IsPartial), hasImports);

        bool InPeriod(ActivityEvent value) => !value.IsRetracted && ActivityClassification.IsTemporal(value.Kind) &&
            MatchesOwner(value, query, playRuns) && value.CapturedAtUtc >= fromUtc && value.CapturedAtUtc < toUtc &&
            (value.SessionId is null || !deleted.Contains(value.SessionId.Value));
        void AddHeader(ActivitySession header)
        {
            if (!MatchesOwner(header, query) || deleted.Contains(header.Id) || header.StartedAtUtc >= toUtc ||
                header.ClosedAtUtc < fromUtc) return;
            if (!headers.TryGetValue(header.Id, out var old) || header.Revision >= old.Revision) headers[header.Id] = header;
        }
        void Adjust(ActivityEvent value, int sign)
        {
            if (!InPeriod(value)) return;
            var amount = contribution(value);
            summary = ActivityProjection.Add(summary, amount, sign);
            if (amount.ResourceItems <= 0) return;
            var journal = ActivityJournals.Values(value);
            var total = new ActivityResourceTotal(value.ItemId, value.Quality, value.ItemUniqueName, value.ItemName,
                ActivityClassification.ResourceMethod(value), amount.ResourceItems, amount.ResourceValue, amount.MissingResourcePriceItems,
                (journal?.Full ?? 0) * amount.ResourceItems, (journal?.Empty ?? 0) * amount.ResourceItems);
            var key = ResourceKey(total);
            var prior = resources.GetValueOrDefault(key) ?? total with { Quantity = 0, KnownValue = 0, MissingPriceItems = 0 };
            resources[key] = prior with
            {
                Name = string.IsNullOrWhiteSpace(total.Name) ? prior.Name : total.Name,
                Quantity = prior.Quantity + sign * total.Quantity,
                KnownValue = prior.KnownValue + sign * total.KnownValue,
                MissingPriceItems = prior.MissingPriceItems + sign * total.MissingPriceItems,
                FullJournalValue = prior.FullJournalValue + sign * total.FullJournalValue,
                EmptyJournalValue = prior.EmptyJournalValue + sign * total.EmptyJournalValue
            };
        }
    }

    private static (int? ItemId, int? Quality, string? UniqueName, ActivityResourceMethod Source, string FallbackName) ResourceKey(ActivityResourceTotal value) =>
        (value.ItemId, value.Quality, value.UniqueName, value.Source,
            value.ItemId is null && string.IsNullOrEmpty(value.UniqueName) ? value.Name : "");

    private static Task<ActivitySummary?> AggregateSummaryResultAsync(IQueryable<ActivityEvent> query, ActivityScope scope,
        CancellationToken cancellationToken) => query.Select(ActivityProjection.ContributionExpression(scope))
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
            Knockdowns = g.Sum(x => x.Knockdowns),
            NonLethalPlayerKills = g.Sum(x => x.NonLethalPlayerKills),
            NonLethalDeaths = g.Sum(x => x.NonLethalDeaths)
        }).SingleOrDefaultAsync(cancellationToken);

    private static async Task<ActivitySummary> AggregateSummaryAsync(IQueryable<ActivityEvent> query, ActivityScope scope,
        CancellationToken cancellationToken) => await AggregateSummaryResultAsync(query, scope, cancellationToken) ?? new();
}
