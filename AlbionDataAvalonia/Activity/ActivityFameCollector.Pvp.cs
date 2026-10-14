using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace AlbionDataAvalonia.Activity;

internal sealed partial class ActivityFameCollector
{
    // The captured kill notification follows its award by five reliable messages.
    // Match either order, but require the same actor, stream, exact base amount,
    // and a short capture-time/sequence window. Kill fame is never a second award.
    private const int MaximumKillSequenceGap = 8;
    private readonly List<FameObservation> pendingAwards = new();
    private readonly List<KillObservation> pendingKills = new();

    public ActivityEvent ObserveAward(UpdateFameEvent packet, ActivityEvent value)
    {
        Expire(value.CapturedAtUtc);
        if (value.IsRetracted || value.Evidence != ActivityEvidence.Confirmed ||
            ActivityClassification.GetFameCategory(value) != ActivityFameCategory.Unknown || packet.ActorObjectId is not > 0 ||
            packet.ComponentStates.GetValueOrDefault((byte)2) != PacketFieldState.Valid ||
            packet.RawFixedPointComponents.GetValueOrDefault((byte)2) is not > 0 ||
            packet.MessageIdentity is not { Kind: PhotonMessageKind.Reliable } identity) return value;

        var observation = new FameObservation(packet.ActorObjectId.Value,
            packet.RawFixedPointComponents[2]!.Value, identity, value);
        var candidates = pendingKills.Where(kill => Matches(observation, kill)).ToArray();
        if (candidates.Length > 0)
        {
            pendingKills.RemoveAll(kill => candidates.Contains(kill));
            return candidates.Length == 1 ? ClassifyPvp(value, candidates[0], amendment: false) : value;
        }
        if (pendingAwards.Count == MaximumPendingProgress) pendingAwards.RemoveAt(0);
        pendingAwards.Add(observation);
        return value;
    }

    public ActivityEvent? ObserveKill(KilledPlayerEvent packet, DateTime at)
    {
        Expire(at);
        if (packet.KillerObjectId is not > 0 || packet.VictimObjectId is not > 0 ||
            packet.BaseFameState != PacketFieldState.Valid || packet.RawBaseFame is not > 0 ||
            packet.MessageIdentity is not { Kind: PhotonMessageKind.Reliable } identity) return null;

        var observation = new KillObservation(packet.KillerObjectId.Value, packet.VictimObjectId.Value,
            packet.VictimName, packet.RawBaseFame.Value, identity, at);
        var candidates = pendingAwards.Where(award => Matches(award, observation)).ToArray();
        if (candidates.Length > 0)
        {
            pendingAwards.RemoveAll(award => candidates.Contains(award));
            if (candidates.Length != 1) return null;
            var before = candidates[0].Value;
            var after = ClassifyPvp(before, observation, amendment: true);
            return after == before ? null : after;
        }
        if (pendingKills.Count == MaximumPendingProgress) pendingKills.RemoveAt(0);
        pendingKills.Add(observation);
        return null;
    }

    private static bool Matches(FameObservation award, KillObservation kill)
    {
        if (award.ActorObjectId != kill.KillerObjectId || award.RawBaseFame != kill.RawBaseFame ||
            !SameStream(award.Identity, kill.Identity) ||
            (award.Value.CapturedAtUtc - kill.CapturedAtUtc).Duration() > MatchWindow) return false;
        var forward = SequenceGap(award.Identity, kill.Identity);
        var backward = SequenceGap(kill.Identity, award.Identity);
        return forward is > 0 and <= MaximumKillSequenceGap || backward is > 0 and <= MaximumKillSequenceGap;
    }

    private static ActivityEvent ClassifyPvp(ActivityEvent value, KillObservation kill, bool amendment)
    {
        if (value.IsRetracted || value.Revision == int.MaxValue ||
            ActivityClassification.GetFameCategory(value) != ActivityFameCategory.Unknown) return value;
        if (JsonNode.Parse(value.DetailsJson) is not JsonObject detail) return value;
        detail["FameCategory"] = (int)ActivityFameCategory.Pvp;
        detail["Category"] = ActivityFameCategory.Pvp.ToString();
        detail["ClassificationSource"] = "KilledPlayer";
        detail["PvpKill"] = new JsonObject
        {
            ["OriginKey"] = kill.Identity.OriginKey,
            ["KillerObjectId"] = kill.KillerObjectId,
            ["VictimObjectId"] = kill.VictimObjectId,
            ["VictimName"] = kill.VictimName,
            ["RawBaseFame"] = kill.RawBaseFame
        };
        return value with
        {
            Revision = value.Revision + (amendment ? 1 : 0),
            PayloadVersion = Math.Max(value.PayloadVersion, 5),
            DetailsJson = detail.ToJsonString()
        };
    }

    private sealed record FameObservation(long ActorObjectId, long RawBaseFame,
        PhotonMessageIdentity Identity, ActivityEvent Value);
    private sealed record KillObservation(long KillerObjectId, long VictimObjectId, string VictimName,
        long RawBaseFame, PhotonMessageIdentity Identity, DateTime CapturedAtUtc);
}
