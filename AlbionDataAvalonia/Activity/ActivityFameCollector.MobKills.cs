using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace AlbionDataAvalonia.Activity;

internal sealed partial class ActivityFameCollector
{
    // The game reports kill progress only for some destiny board entries, so kill fame can arrive
    // with no fame achievement progress. Its mob kill lands within a second, usually just after it.
    internal static readonly TimeSpan MobKillWindow = TimeSpan.FromSeconds(1);
    private readonly List<ActivityEvent> killAwards = new();
    private readonly List<DateTime> mobKills = new();

    // Zone fame marks a kill award. Without fame progress it is PvE once a mob kill is near it.
    public ActivityEvent ObserveUnmatchedAward(UpdateFameEvent packet, ActivityFameMatch match, ActivityEvent value)
    {
        Expire(value.CapturedAtUtc);
        if (match.HasFameProgress || packet.ZoneFame <= 0 || value.IsRetracted || value.Evidence != ActivityEvidence.Confirmed ||
            ActivityClassification.GetFameCategory(value) != ActivityFameCategory.Unknown) return value;
        if (mobKills.Any(kill => (value.CapturedAtUtc - kill).Duration() <= MobKillWindow)) return ClassifyPve(value, amendment: false);
        if (killAwards.Count == MaximumPendingProgress) killAwards.RemoveAt(0);
        killAwards.Add(value);
        return value;
    }

    // A mob kill by you or your party classifies the kill awards waiting near it.
    public IReadOnlyList<ActivityEvent> ObserveMobKill(DateTime at)
    {
        Expire(at);
        if (mobKills.Count == MaximumPendingProgress) mobKills.RemoveAt(0);
        mobKills.Add(at);
        var near = killAwards.Where(award => (award.CapturedAtUtc - at).Duration() <= MobKillWindow).ToArray();
        killAwards.RemoveAll(award => near.Contains(award));
        return near.Select(award => ClassifyPve(award, amendment: true)).Where(award => !near.Contains(award)).ToArray();
    }

    internal static ActivityEvent ClassifyPve(ActivityEvent value, bool amendment)
    {
        if (value.IsRetracted || value.Revision == int.MaxValue ||
            ActivityClassification.GetFameCategory(value) != ActivityFameCategory.Unknown) return value;
        if (JsonNode.Parse(value.DetailsJson) is not JsonObject detail) return value;
        detail["FameCategory"] = (int)ActivityFameCategory.Pve;
        detail["Category"] = ActivityFameCategory.Pve.ToString();
        detail["ClassificationSource"] = "MobKilled";
        return value with { Revision = value.Revision + (amendment ? 1 : 0), DetailsJson = detail.ToJsonString() };
    }
}
