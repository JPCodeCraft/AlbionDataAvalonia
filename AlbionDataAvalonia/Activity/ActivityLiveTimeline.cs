using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public enum ActivityLiveTimelineMarkerKind { MobKill, PlayerKill, Death, Knockdown, Loot, Fame, Silver }

public sealed record ActivityLiveTimelineSample(DateTime CapturedAtUtc, double DamageDealt,
    double DamageReceived, double HealingDone, double HealingReceived);
public sealed record ActivityLiveTimelineMarker(string Id, ActivityLiveTimelineMarkerKind Kind,
    DateTime CapturedAtUtc, string Label, string Detail, IReadOnlyList<Guid> EventIds, Guid? EncounterId);
public sealed record ActivityLiveTimelineEncounter(Guid Id, int Number, DateTime StartedAtUtc,
    DateTime LastCombatAtUtc, ActivityEncounterKind Kind, ActivityState State);
public sealed record ActivityLiveTimelineGap(DateTime StartedAtUtc, DateTime EndedAtUtc, string Reason);
public sealed record ActivityLiveTimelineSnapshot(Guid? SessionId, ActivityScope Scope, DateTime WindowStartUtc,
    DateTime WindowEndUtc, IReadOnlyList<ActivityLiveTimelineSample> Samples,
    IReadOnlyList<ActivityLiveTimelineMarker> Markers, IReadOnlyList<ActivityLiveTimelineEncounter> Encounters,
    IReadOnlyList<ActivityLiveTimelineGap> Gaps, bool IsPartial = false, bool IsTruncated = false);

/// <summary>Presentation-independent, bounded projection of the current five-minute journal window.</summary>
internal static class ActivityLiveTimeline
{
    internal const int WindowSeconds = 300;
    internal const int MaximumMarkerRows = 10000;
    internal const int MaximumMarkers = 600;
    internal static DateTime WindowEnd(DateTime value) => new(value.ToUniversalTime().Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    internal static bool IsCombat(ActivityEvent value) => value.Kind is ActivityEventKind.Damage or ActivityEventKind.Healing;

    internal static ActivityLiveTimelineSnapshot Empty(Guid? sessionId, ActivityScope scope, DateTime endUtc) =>
        new(sessionId, scope, endUtc.AddSeconds(-WindowSeconds), endUtc, [], [], [], []);

    internal static IReadOnlyList<ActivityLiveTimelineMarker> Markers(IEnumerable<ActivityEvent> events,
        ActivityScope scope, out bool truncated)
    {
        var candidates = new List<(ActivityEvent Event, ActivityLiveTimelineMarkerKind Kind)>();
        foreach (var observation in events.OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.CaptureOrder))
        {
            var amount = ActivityProjection.Contribution(observation, scope);
            if (amount.MobKills > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.MobKill));
            if (amount.PlayerKills > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.PlayerKill));
            if (amount.Deaths > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Death));
            if (amount.Knockdowns > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Knockdown));
            if (amount.LootItems > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Loot));
            if (amount.Fame > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Fame));
            if (amount.SilverRaw > 0 || amount.UnknownSilverEvents > 0) candidates.Add((observation, ActivityLiveTimelineMarkerKind.Silver));
        }
        // Nearby awards share a marker only when their recipient and originating encounter agree.
        // A chest burst also requires the same loot source. Critical outcomes remain individually visible.
        var groups = new List<List<(ActivityEvent Event, ActivityLiveTimelineMarkerKind Kind)>>();
        var recent = new Dictionary<(ActivityLiveTimelineMarkerKind Kind, Guid? EncounterId, long CaptureGeneration, long MapEpoch, string Recipient, string Source), int>();
        foreach (var candidate in candidates)
        {
            var value = candidate.Event;
            var critical = candidate.Kind is ActivityLiveTimelineMarkerKind.PlayerKill or ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.Knockdown;
            var key = (candidate.Kind, value.EncounterId, value.CaptureGeneration, value.MapEpoch,
                candidate.Kind == ActivityLiveTimelineMarkerKind.MobKill ? "" : value.SourceActorId ?? value.SourceName,
                candidate.Kind == ActivityLiveTimelineMarkerKind.Loot ? LootSourceKey(value) : "");
            if (!critical && recent.TryGetValue(key, out var index) &&
                value.CapturedAtUtc - groups[index][0].Event.CapturedAtUtc <= TimeSpan.FromSeconds(3))
                groups[index].Add(candidate);
            else
            {
                if (!critical) recent[key] = groups.Count;
                groups.Add([candidate]);
            }
        }
        var result = groups.Select(group => MakeMarker(group, scope)).ToArray();
        truncated = result.Length > MaximumMarkers;
        // If an extreme burst exceeds the display bound, outcomes take priority over earnings.
        return result.OrderByDescending(x => x.Kind is ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.PlayerKill or ActivityLiveTimelineMarkerKind.Knockdown)
            .ThenByDescending(x => x.CapturedAtUtc).Take(MaximumMarkers).OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.Id).ToArray();
    }

    private static ActivityLiveTimelineMarker MakeMarker(List<(ActivityEvent Event, ActivityLiveTimelineMarkerKind Kind)> group, ActivityScope scope)
    {
        var first = group[0].Event;
        var kind = group[0].Kind;
        var events = group.Select(x => x.Event).ToArray();
        var summary = events.Aggregate(new ActivitySummary(), (sum, value) => ActivityProjection.Add(sum, ActivityProjection.Contribution(value, scope)));
        var label = kind switch
        {
            ActivityLiveTimelineMarkerKind.MobKill => summary.MobKills == 1 ? "Mob killed" : $"{summary.MobKills:N0} mobs",
            ActivityLiveTimelineMarkerKind.PlayerKill => "Player kill",
            ActivityLiveTimelineMarkerKind.Death => "Death",
            ActivityLiveTimelineMarkerKind.Knockdown => "Knockdown",
            ActivityLiveTimelineMarkerKind.Loot => $"{summary.LootItems:N0} {(summary.LootItems == 1 ? "item" : "items")}",
            ActivityLiveTimelineMarkerKind.Fame => $"+{summary.Fame:N0} fame",
            _ => summary.UnknownSilverEvents > 0 && summary.SilverRaw == 0 ? "Silver: unknown" : $"+{summary.Silver:N0} silver"
        };
        string detail;
        if (kind == ActivityLiveTimelineMarkerKind.Loot)
        {
            var items = events.GroupBy(x => x.ItemUniqueName ?? x.ItemName).Take(20)
                .Select(g => $"{(string.IsNullOrWhiteSpace(g.First().ItemName) ? g.Key : g.First().ItemName)} × {g.Sum(x => x.Quantity):N0}");
            var estimate = summary.MissingPriceItems == summary.LootItems ? "Estimated value: Unavailable" :
                summary.MissingPriceItems > 0 ? $"Known estimate: {summary.LootValue:N0} silver" : $"Estimated value: {summary.LootValue:N0} silver";
            detail = string.Join("\n", items) + $"\nRecipient: {Actor(first.SourceName)}\nSource: {Actor(first.LootSourceName)}\n{estimate}";
            if (summary.MissingPriceItems > 0) detail += $"\n{summary.MissingPriceItems:N0} items without prices";
            if (events.Select(x => x.ItemUniqueName ?? x.ItemName).Distinct().Count() > 20) detail += "\nMore items available in the session loot list.";
        }
        else if (kind == ActivityLiveTimelineMarkerKind.MobKill)
        {
            detail = string.Join("\n", events.GroupBy(x => x.TargetName).Take(20).Select(g => $"{Actor(g.Key)} × {g.Count():N0}"));
            var inferred = events.Count(x => x.Evidence == ActivityEvidence.Inferred);
            if (inferred > 0) detail += $"\n{inferred:N0} inferred mob deaths";
            detail += "\nParticipated kills; reward credit is not inferred.";
        }
        else if (kind == ActivityLiveTimelineMarkerKind.Fame)
            detail = $"Your observed fame: {summary.Fame:N0}\nPvE / PvP category unavailable.";
        else if (kind == ActivityLiveTimelineMarkerKind.Silver)
        {
            detail = $"Cash silver: {summary.Silver:N2}\nRecipient: {Actor(first.SourceName)}";
            if (summary.UnknownSilverEvents > 0) detail += $"\n{summary.UnknownSilverEvents:N0} unknown net amounts";
        }
        else detail = $"{label}\nSource: {Actor(first.SourceName)}\nTarget: {Actor(first.TargetName)}";
        var time = first.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        if (events.Length > 1 && events[^1].CapturedAtUtc > first.CapturedAtUtc)
            time += " – " + events[^1].CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        detail = time + $"\nLocation: {Actor(first.LocationName)}\n" + detail;
        if (first.EncounterId is not null && kind is ActivityLiveTimelineMarkerKind.Loot or ActivityLiveTimelineMarkerKind.Fame or ActivityLiveTimelineMarkerKind.Silver)
            detail += "\nObserved during the encounter.";
        return new($"{kind}:{first.Id:N}", kind, first.CapturedAtUtc, label, detail,
            events.Select(x => x.Id).Distinct().Take(250).ToArray(), first.EncounterId);
    }

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

    private static string Actor(string name) => string.IsNullOrWhiteSpace(name) ? "Unknown" : name;

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
