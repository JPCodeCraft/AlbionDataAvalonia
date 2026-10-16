using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public enum ActivityLiveTimelineMarkerKind { MobKill, PlayerKill, Death, Knockdown, Loot, Fame, Silver, Gathering, Fishing, Party, Overflow }
public enum ActivityTimelineChartMode { Combat, Gathering, Fame }

/// <summary>Per-second rates over the snapshot's sample interval, including each fame category once.</summary>
public sealed record ActivityLiveTimelineSample(DateTime CapturedAtUtc, double DamageDealt,
    double DamageReceived, double HealingDone, double HealingReceived, double GatheringQuantity = 0, double FishingQuantity = 0,
    double PveFame = 0, double PvpFame = 0, double GatheringFame = 0, double FishingFame = 0, double UnknownFame = 0);
public sealed record ActivityLiveTimelineMarker(string Id, ActivityLiveTimelineMarkerKind Kind,
    DateTime CapturedAtUtc, string Label, string Detail, IReadOnlyList<Guid> EventIds, Guid? EncounterId,
    double Amount = 0, bool HasUnknownAmount = false, string ValueText = "", ActivityLiveTimelineTooltip? Tooltip = null,
    DateTime? EndedAtUtc = null, double SecondaryAmount = 0);
public sealed record ActivityLiveTimelineTooltip(string Title, string TimeText, string ContextText, IReadOnlyList<string> Lines);
// Summary holds the encounter's saved totals for the snapshot scope; null when the chart follows one player.
public sealed record ActivityLiveTimelineEncounter(Guid Id, int Number, DateTime StartedAtUtc,
    DateTime LastCombatAtUtc, ActivityEncounterKind Kind, ActivityState State, string LocationName = "", ActivitySummary? Summary = null);
public sealed record ActivityLiveTimelineGap(DateTime StartedAtUtc, DateTime EndedAtUtc, string Reason);
public sealed record ActivityLiveTimelineSnapshot(Guid? SessionId, ActivityScope Scope, DateTime WindowStartUtc,
    DateTime WindowEndUtc, IReadOnlyList<ActivityLiveTimelineSample> Samples,
    IReadOnlyList<ActivityLiveTimelineMarker> Markers, IReadOnlyList<ActivityLiveTimelineEncounter> Encounters,
    IReadOnlyList<ActivityLiveTimelineGap> Gaps, bool IsPartial = false, bool IsTruncated = false,
    ActivityCategories Categories = ActivityCategories.All, string ContextKey = "", bool HasContext = true,
    double SampleSeconds = 1);

/// <summary>Presentation-independent, bounded projection of the current five-minute journal window.</summary>
internal static class ActivityLiveTimeline
{
    internal const int WindowSeconds = 300;
    internal const int MaximumMarkerRows = 10000;
    internal const int MaximumMarkers = 600;
    internal static DateTime WindowEnd(DateTime value) => new(value.ToUniversalTime().Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    internal static bool IsCombat(ActivityEvent value) => value.Kind is ActivityEventKind.Damage or ActivityEventKind.Healing;

    internal static ActivityLiveTimelineSnapshot Empty(Guid? sessionId, ActivityScope scope, DateTime endUtc) =>
        new(sessionId, scope, endUtc.AddSeconds(-WindowSeconds), endUtc, [], [], [], [], HasContext: sessionId.HasValue);

    internal static IReadOnlyList<ActivityLiveTimelineMarker> Markers(IEnumerable<ActivityEvent> events,
        ActivityScope scope, out bool truncated, double windowSeconds = WindowSeconds, string? actorKey = null)
    {
        var groupingSeconds = Math.Clamp(windowSeconds / 30, 3, 60);
        Func<ActivityEvent, ActivitySummary> contribution = string.IsNullOrWhiteSpace(actorKey)
            ? value => ActivityProjection.Contribution(value, scope) : ActivityProjection.ContributionExpression(scope, actorKey).Compile();
        var candidates = new List<(ActivityEvent Event, ActivityLiveTimelineMarkerKind Kind)>();
        foreach (var observation in events.OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.CaptureOrder))
        {
            if (!ActivityClassification.IsTemporal(observation.Kind)) continue;
            if (ActivityClassification.IsParty(observation.Kind))
            {
                // The roster journaled when a session opens is context, not a change.
                if (ActivityPartyDetails.Parse(observation.DetailsJson) is not { Roster: true })
                    candidates.Add((observation, ActivityLiveTimelineMarkerKind.Party));
                continue;
            }
            var amount = contribution(observation);
            if (amount.MobKills > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.MobKill));
            if (amount.PlayerKills > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.PlayerKill));
            if (amount.Deaths > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Death));
            if (amount.Knockdowns > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Knockdown));
            if (amount.LootItems > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Loot));
            if (amount.Fame > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Fame));
            if (amount.SilverRaw > 0 || amount.UnknownSilverEvents > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Silver));
            if (amount.GatheringItems > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Gathering));
            if (amount.FishingItems > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Fishing));
        }
        // Nearby awards share a marker only when their recipient, fame category and originating encounter agree.
        // A chest burst also requires the same loot source. Critical outcomes remain individually visible.
        var groups = new List<List<(ActivityEvent Event, ActivityLiveTimelineMarkerKind Kind)>>();
        var recent = new Dictionary<(ActivityLiveTimelineMarkerKind Kind, Guid? SessionId, Guid? EncounterId, long CaptureGeneration, long MapEpoch, string Recipient, string Source, ActivityFameCategory FameCategory), int>();
        foreach (var candidate in candidates)
        {
            var value = candidate.Event;
            var critical = candidate.Kind is ActivityLiveTimelineMarkerKind.PlayerKill or ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.Knockdown;
            // A large party joining at once shares one marker, like a burst of mob kills.
            var key = (candidate.Kind, value.SessionId, value.EncounterId, value.CaptureGeneration, value.MapEpoch,
                candidate.Kind is ActivityLiveTimelineMarkerKind.MobKill or ActivityLiveTimelineMarkerKind.Party ? "" : value.SourceActorId ?? value.SourceName,
                candidate.Kind == ActivityLiveTimelineMarkerKind.Loot ? LootSourceKey(value) : "",
                candidate.Kind == ActivityLiveTimelineMarkerKind.Fame ? ActivityClassification.GetFameCategory(value) : ActivityFameCategory.Unknown);
            if (!critical && recent.TryGetValue(key, out var index) &&
                value.CapturedAtUtc - groups[index][0].Event.CapturedAtUtc <= TimeSpan.FromSeconds(groupingSeconds))
                groups[index].Add(candidate);
            else
            {
                if (!critical) recent[key] = groups.Count;
                groups.Add([candidate]);
            }
        }
        var result = groups.Select(group => MakeMarker(group, contribution)).ToArray();
        truncated = result.Length > MaximumMarkers;
        // If an extreme burst exceeds the display bound, outcomes take priority over earnings.
        return result.OrderByDescending(x => x.Kind is ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.PlayerKill or ActivityLiveTimelineMarkerKind.Knockdown)
            .ThenByDescending(x => x.CapturedAtUtc).Take(MaximumMarkers).OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.Id).ToArray();
    }

    private static ActivityLiveTimelineMarker MakeMarker(List<(ActivityEvent Event, ActivityLiveTimelineMarkerKind Kind)> group, Func<ActivityEvent, ActivitySummary> contribution)
    {
        var first = group[0].Event;
        var kind = group[0].Kind;
        var events = group.Select(x => x.Event).ToArray();
        if (kind == ActivityLiveTimelineMarkerKind.Party) return PartyMarker(events);
        var summary = events.Aggregate(new ActivitySummary(), (sum, value) => ActivityProjection.Add(sum, contribution(value)));
        // Kills and deaths stay individual markers, so one event decides the label.
        var nonLethal = kind is ActivityLiveTimelineMarkerKind.PlayerKill or ActivityLiveTimelineMarkerKind.Death
            && ActivityDatabaseFunctions.JsonLong(first.DetailsJson, "$.Lethal") == 0;
        var label = kind switch
        {
            ActivityLiveTimelineMarkerKind.MobKill => "Mobs killed",
            ActivityLiveTimelineMarkerKind.PlayerKill => nonLethal ? "PvP kill (non-lethal)" : "PvP kill",
            ActivityLiveTimelineMarkerKind.Death => nonLethal ? "Death (non-lethal)" : "Death",
            ActivityLiveTimelineMarkerKind.Knockdown => "Knockdown",
            ActivityLiveTimelineMarkerKind.Loot => "Loot picked up",
            ActivityLiveTimelineMarkerKind.Fame => FameLabel(ActivityClassification.GetFameCategory(first)),
            ActivityLiveTimelineMarkerKind.Gathering => "Harvested",
            ActivityLiveTimelineMarkerKind.Fishing => "Caught",
            _ => "Silver picked up"
        };
        var amount = kind switch
        {
            ActivityLiveTimelineMarkerKind.MobKill => summary.MobKills,
            ActivityLiveTimelineMarkerKind.PlayerKill => summary.PlayerKills,
            ActivityLiveTimelineMarkerKind.Death => summary.Deaths,
            ActivityLiveTimelineMarkerKind.Knockdown => summary.Knockdowns,
            ActivityLiveTimelineMarkerKind.Loot => summary.LootItems,
            ActivityLiveTimelineMarkerKind.Fame => summary.Fame,
            ActivityLiveTimelineMarkerKind.Gathering => summary.GatheringItems,
            ActivityLiveTimelineMarkerKind.Fishing => summary.FishingItems,
            _ => summary.Silver
        };
        var unknown = kind == ActivityLiveTimelineMarkerKind.Silver && summary.UnknownSilverEvents > 0;
        var valueText = MarkerValue(amount, unknown);
        var lines = new List<string>();
        var context = first.LocationName;
        if (kind is ActivityLiveTimelineMarkerKind.Loot or ActivityLiveTimelineMarkerKind.Gathering or ActivityLiveTimelineMarkerKind.Fishing)
        {
            var items = events.GroupBy(x => (x.ItemUniqueName ?? x.ItemName, x.Quality)).ToArray();
            lines.AddRange(items.Take(4).Select(g => $"{DisplayName(g.First().ItemName, g.First().ItemUniqueName, "Unknown item")} × {g.Sum(x => x.Quantity):N0}"));
            if (items.Length > 4) lines.Add($"+{items.Length - 4:N0} more item types");
            var itemCount = kind == ActivityLiveTimelineMarkerKind.Loot ? summary.LootItems :
                kind == ActivityLiveTimelineMarkerKind.Gathering ? summary.GatheringItems : summary.FishingItems;
            var missing = kind == ActivityLiveTimelineMarkerKind.Loot ? summary.MissingPriceItems : summary.MissingResourcePriceItems;
            var value = kind == ActivityLiveTimelineMarkerKind.Loot ? summary.LootValue :
                kind == ActivityLiveTimelineMarkerKind.Gathering ? summary.GatheringValue : summary.FishingValue;
            var estimate = missing == itemCount ? "Estimated value unavailable" : $"Estimated value: {value:N0} silver";
            context = WithPlayer(context, first.SourceName);
            lines.Add(estimate);
            if (missing > 0 && missing != itemCount) lines.Add($"{missing:N0} items have no price");
        }
        else if (kind == ActivityLiveTimelineMarkerKind.MobKill)
        {
            var mobs = events.GroupBy(x => string.IsNullOrWhiteSpace(x.TargetName) ? x.MobUniqueName ?? "" : x.TargetName).ToArray();
            lines.AddRange(mobs.Take(4).Select(g => $"{DisplayName(g.First().TargetName, g.First().MobUniqueName, "Unknown mob")} × {g.Count():N0}"));
            if (mobs.Length > 4) lines.Add($"+{mobs.Length - 4:N0} more mob types");
        }
        else if (kind == ActivityLiveTimelineMarkerKind.Fame)
        {
            lines.Add($"{summary.Fame:N0} fame");
            context = WithPlayer(context, first.SourceName);
        }
        else if (kind == ActivityLiveTimelineMarkerKind.Silver)
        {
            if (summary.SilverRaw > 0 || !unknown) lines.Add($"{summary.Silver:N0} silver");
            if (unknown) lines.Add($"Amount unavailable for {summary.UnknownSilverEvents:N0} pickups");
            context = WithPlayer(context, first.SourceName);
        }
        else lines.Add($"{Actor(first.SourceName)} → {Actor(first.TargetName)}");
        var time = first.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        if (events.Length > 1 && events[^1].CapturedAtUtc > first.CapturedAtUtc)
            time += " – " + events[^1].CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        var tooltipTitle = kind is ActivityLiveTimelineMarkerKind.Fame or ActivityLiveTimelineMarkerKind.Silver or ActivityLiveTimelineMarkerKind.PlayerKill ? label : $"{label} · {amount:N0}";
        var tooltip = new ActivityLiveTimelineTooltip(tooltipTitle, time, context, lines);
        var detail = time + "\n" + context + "\n" + string.Join("\n", lines);
        return new($"{kind}:{first.Id:N}", kind, first.CapturedAtUtc, label, detail,
            events.Select(x => x.Id).Distinct().Take(250).ToArray(), first.EncounterId, amount, unknown, valueText, tooltip, events[^1].CapturedAtUtc);
    }

    private static ActivityLiveTimelineMarker PartyMarker(ActivityEvent[] events)
    {
        var first = events[0];
        var joined = events.Where(x => x.Kind == ActivityEventKind.PartyJoined).Select(x => Actor(x.SourceName)).ToArray();
        var left = events.Where(x => x.Kind == ActivityEventKind.PartyLeft).Select(x => Actor(x.SourceName)).ToArray();
        var lines = new List<string>();
        if (joined.Length > 0) lines.Add("Joined: " + Names(joined));
        if (left.Length > 0) lines.Add("Left: " + Names(left));
        if (ActivityPartyDetails.Parse(events[^1].DetailsJson) is { PartySize: > 0 } details)
            lines.Add(details.PartySize == 1 ? "Now solo" : $"Party size: {details.PartySize:N0}");
        var title = joined.Length > 0 && left.Length > 0 ? "Party changed"
            : joined.Length > 0 ? joined.Length == 1 ? "Player joined the party" : $"{joined.Length:N0} players joined the party"
            : left.Length == 1 ? "Player left the party" : $"{left.Length:N0} players left the party";
        var time = first.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        if (events[^1].CapturedAtUtc > first.CapturedAtUtc)
            time += " – " + events[^1].CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        var tooltip = new ActivityLiveTimelineTooltip(title, time, first.LocationName, lines);
        return new($"{ActivityLiveTimelineMarkerKind.Party}:{first.Id:N}", ActivityLiveTimelineMarkerKind.Party, first.CapturedAtUtc,
            "Party changed", time + "\n" + first.LocationName + "\n" + string.Join("\n", lines),
            events.Select(x => x.Id).Distinct().Take(250).ToArray(), null, joined.Length, false, PartyValue(joined.Length, left.Length),
            tooltip, events[^1].CapturedAtUtc, left.Length);

        static string Names(string[] names) => names.Length <= 5 ? string.Join(", ", names)
            : string.Join(", ", names.Take(5)) + $" +{names.Length - 5:N0} more";
    }

    private static string PartyValue(double joined, double left) => string.Join(" ", new[]
    {
        joined > 0 ? "+" + joined.ToString("N0", CultureInfo.CurrentCulture) : null,
        left > 0 ? "−" + left.ToString("N0", CultureInfo.CurrentCulture) : null
    }.Where(x => x is not null));

    // Display grouping adapts to the chart's width, independently of event/encounter accounting.
    // Anchor each group at its first marker so a continuous stream cannot chain into one large badge.
    internal static IReadOnlyList<ActivityLiveTimelineMarker> GroupMarkers(IReadOnlyList<ActivityLiveTimelineMarker> markers,
        IReadOnlyList<ActivityLiveTimelineGap> gaps, double groupingSeconds)
    {
        var groups = new List<List<ActivityLiveTimelineMarker>>();
        var recent = new Dictionary<(ActivityLiveTimelineMarkerKind Kind, string Label, string Context), int>();
        foreach (var marker in markers.OrderBy(value => value.CapturedAtUtc).ThenBy(value => value.Id))
        {
            var critical = marker.Kind is ActivityLiveTimelineMarkerKind.PlayerKill or ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.Knockdown;
            var key = (marker.Kind, marker.Label, marker.Tooltip?.ContextText ?? "");
            if (!critical && recent.TryGetValue(key, out var index) &&
                (marker.CapturedAtUtc - groups[index][0].CapturedAtUtc).TotalSeconds <= groupingSeconds &&
                !gaps.Any(gap => gap.StartedAtUtc < (marker.EndedAtUtc ?? marker.CapturedAtUtc) &&
                    gap.EndedAtUtc > groups[index][0].CapturedAtUtc))
                groups[index].Add(marker);
            else
            {
                if (!critical) recent[key] = groups.Count;
                groups.Add([marker]);
            }
        }
        return groups.Select(group => group.Count == 1 ? group[0] : CombineMarkers(group)).ToArray();
    }

    private static ActivityLiveTimelineMarker CombineMarkers(IReadOnlyList<ActivityLiveTimelineMarker> group)
    {
        var first = group[0];
        var party = first.Kind == ActivityLiveTimelineMarkerKind.Party;
        var end = group.Max(marker => marker.EndedAtUtc ?? marker.CapturedAtUtc);
        var amount = group.Sum(marker => marker.Amount);
        var secondary = group.Sum(marker => marker.SecondaryAmount);
        var unknown = group.Any(marker => marker.HasUnknownAmount);
        var time = first.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        if (end > first.CapturedAtUtc) time += " – " + end.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        var title = first.Label + " · " + (party ? PartyValue(amount, secondary) : amount.ToString("N0", CultureInfo.CurrentCulture) + (unknown ? " + ?" : ""));
        // Keep constituent ranges and totals: their existing item/mob breakdowns may already be truncated.
        var lines = group.Take(4).Select(marker =>
            (marker.Tooltip?.TimeText ?? marker.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture)) +
            " · " + (party ? marker.ValueText : marker.Amount.ToString("N0", CultureInfo.CurrentCulture) + (marker.HasUnknownAmount ? " + ?" : "")) +
            (marker.Kind is ActivityLiveTimelineMarkerKind.Loot or ActivityLiveTimelineMarkerKind.Gathering or ActivityLiveTimelineMarkerKind.Fishing
                ? " items" : marker.Kind == ActivityLiveTimelineMarkerKind.MobKill ? " kills" : "")).ToList();
        if (group.Count > 4) lines.Add($"+{group.Count - 4:N0} more groups");
        var tooltip = new ActivityLiveTimelineTooltip(title, time, first.Tooltip?.ContextText ?? "", lines);
        return first with
        {
            Amount = amount,
            SecondaryAmount = secondary,
            HasUnknownAmount = unknown,
            ValueText = party ? PartyValue(amount, secondary) : MarkerValue(amount, unknown),
            EndedAtUtc = end,
            EventIds = group.SelectMany(marker => marker.EventIds).Distinct().ToArray(),
            EncounterId = group.All(marker => marker.EncounterId == first.EncounterId) ? first.EncounterId : null,
            Detail = time + "\n" + tooltip.ContextText + "\n" + string.Join("\n", lines),
            Tooltip = tooltip
        };
    }

    private static string MarkerValue(double amount, bool unknown) =>
        unknown && amount == 0 ? "?" : CompactAmount(amount) + (unknown ? " ?" : "");

    private static string CompactAmount(double value) => value >= 1000000 ? (value / 1000000).ToString("0.#", CultureInfo.CurrentCulture) + "m" :
        value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.CurrentCulture) + "k" : value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FameLabel(ActivityFameCategory category) => category switch
    {
        ActivityFameCategory.Pve => "Combat fame",
        ActivityFameCategory.Pvp => "PvP fame",
        ActivityFameCategory.Gathering => "Gathering fame",
        ActivityFameCategory.Fishing => "Fishing fame",
        _ => "Fame gained"
    };

    internal static IReadOnlyList<ActivityLiveTimelineGap> Gaps(ActivitySession session, DateTime fromUtc, DateTime toUtc,
        IReadOnlyCollection<ActivityEvent> captureGaps)
    {
        ActivityVisit[] visits;
        try { visits = JsonSerializer.Deserialize<ActivityVisit[]>(session.VisitsJson) ?? []; }
        catch (JsonException) { return []; }
        var ordered = visits.OrderBy(x => x.StartedAtUtc).ToArray();
        var gaps = new List<ActivityLiveTimelineGap>();
        if (session.StartedAtUtc > fromUtc)
            gaps.Add(new(fromUtc, session.StartedAtUtc < toUtc ? session.StartedAtUtc : toUtc, "Before session"));
        for (var i = 0; i < ordered.Length; i++)
        {
            if (ordered[i].EndedAtUtc is not { } ended) continue;
            var resumed = i + 1 < ordered.Length ? ordered[i + 1].StartedAtUtc :
                session.State == ActivityState.Open && session.ObservationStartedAtUtc is null ? toUtc : ended;
            if (resumed > ended && resumed > fromUtc && ended < toUtc)
            {
                var capturePaused = captureGaps.Any(x => !x.IsRetracted && Math.Abs((x.CapturedAtUtc - ended).TotalSeconds) < 0.001);
                gaps.Add(new(ended < fromUtc ? fromUtc : ended, resumed > toUtc ? toUtc : resumed,
                    capturePaused ? "Capture paused" : "Outside the session"));
            }
        }
        return gaps;
    }

    private static string Actor(string name) => string.IsNullOrWhiteSpace(name) || IsTechnicalName(name) ? "Unknown" : name;

    private static string DisplayName(string name, string? uniqueName, string fallback) =>
        string.IsNullOrWhiteSpace(name) || IsTechnicalName(name) || string.Equals(name, uniqueName, StringComparison.Ordinal) ? fallback : name;

    private static bool IsTechnicalName(string name) => Guid.TryParse(name, out _) || name.StartsWith('@') ||
        name.StartsWith("MOB_", StringComparison.OrdinalIgnoreCase) ||
        (name.Length > 2 && name[0] == 'T' && char.IsDigit(name[1]) && name[2] == '_') ||
        (name.Contains('_') && name.All(character => !char.IsLetter(character) || char.IsUpper(character)));

    private static string WithPlayer(string location, string player) => string.IsNullOrWhiteSpace(player) || IsTechnicalName(player) ? location :
        string.IsNullOrWhiteSpace(location) ? player : location + " · " + player;

    private static string LootSourceKey(ActivityEvent value)
    {
        try
        {
            using var detail = JsonDocument.Parse(value.DetailsJson);
            if ((detail.RootElement.TryGetProperty("SourceObjectId", out var source) ||
                detail.RootElement.TryGetProperty("ObjectId", out source)) && source.TryGetInt64(out var objectId) && objectId > 0)
                return $"{value.CaptureGeneration}:{value.MapEpoch}:{objectId}";
        }
        catch (JsonException) { }
        // Without a source identity, preserve individual observations instead of grouping separate chests.
        return value.Id.ToString("N");
    }
}
