using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityTrackerService
{
    private static readonly TimeSpan PendingActorLifetime = TimeSpan.FromSeconds(10);
    private const int MaximumPendingActors = 4096;
    private const int MaximumUnidentifiedDeaths = 128;
    private readonly Dictionary<PendingActorKey, PendingActor> pendingActors = new();
    private readonly Dictionary<long, PendingPlayerDeath> unidentifiedDeaths = new();
    private DateTime lastPendingActorSweep;
    private DateTime? actorJoinStartedAt;

    private void ObservePendingActorMetadata(BaseEvent packet, DateTime at)
    {
        if (packet is not (NewCharacterEvent or NewMobEvent or LeaveEvent)
            || ActorConnection(packet.ConnectionId, packet.MessageIdentity) is not { } connection) return;
        // Metadata from the current map must not survive a later map epoch just
        // because that transition happened to reuse its transport connection.
        if (actorJoinStartedAt is { } startedAt && at < startedAt) return;
        if (actorJoinStartedAt is null && HasConfirmedContext && SameConnection(packet.ConnectionId, packet.MessageIdentity)) return;
        var objectId = packet switch
        {
            NewCharacterEvent character => character.ObjectId,
            NewMobEvent mob => mob.ObjectId,
            LeaveEvent leave => leave.userObjectId,
            _ => null
        };
        if (objectId is not > 0) return;
        if (at - lastPendingActorSweep >= TimeSpan.FromSeconds(1))
        {
            foreach (var key in pendingActors.Where(x => at - x.Value.At > PendingActorLifetime).Select(x => x.Key).ToArray())
                pendingActors.Remove(key);
            lastPendingActorSweep = at;
        }
        var actorKey = new PendingActorKey(connection, objectId.Value);
        if (pendingActors.TryGetValue(actorKey, out var previous) && previous.At > at) return;
        if (previous is null && pendingActors.Count >= MaximumPendingActors) return;
        pendingActors[actorKey] = new PendingActor(packet, at);
    }

    private void ReplayPendingActorMetadata(JoinResponse packet, DateTime at)
    {
        var connection = ActorConnection(packet.ConnectionId, packet.MessageIdentity);
        var matches = connection is { } incoming
            ? pendingActors.Where(x => x.Key.Connection == incoming && x.Key.ObjectId != packet.userObjectId
                && x.Value.At <= at && at - x.Value.At <= PendingActorLifetime)
                .Select(x => x.Value).OrderBy(x => x.At).ToArray()
            : [];
        ClearPendingActorMetadata();
        foreach (var pending in matches)
        {
            if (pending.Packet is NewCharacterEvent character) actors.Character(character);
            else if (pending.Packet is NewMobEvent { ObjectId: > 0 } mob)
                actors.Mob(mob.ObjectId.Value, mob.MobIndex, mob.CurrentHealth);
        }
    }

    private void ClearPendingActorMetadata()
    {
        pendingActors.Clear();
        unidentifiedDeaths.Clear();
        lastPendingActorSweep = default;
        actorJoinStartedAt = null;
    }

    private void BeginActorJoin(DateTime at)
    {
        ClearPendingActorMetadata();
        actorJoinStartedAt = at;
    }

    private void RememberUnidentifiedDeath(DiedEvent packet, ActivityEvent observation)
    {
        if (observation.Kind != ActivityEventKind.Death || observation.TargetKind != ActivityActorKind.Unknown
            || packet.VictimObjectId is not > 0) return;
        foreach (var id in unidentifiedDeaths.Where(x => observation.CapturedAtUtc - x.Value.Event.CapturedAtUtc > PendingActorLifetime)
            .Select(x => x.Key).ToArray()) unidentifiedDeaths.Remove(id);
        if (unidentifiedDeaths.Count >= MaximumUnidentifiedDeaths) return;
        unidentifiedDeaths[packet.VictimObjectId.Value] = new PendingPlayerDeath(packet.KillerObjectId, observation);
    }

    private void RecordKilledPlayerEvidence(KilledPlayerEvent packet, DateTime at)
    {
        var victim = actors.ConfirmPlayer(packet.VictimObjectId, packet.VictimName);
        if (packet.VictimObjectId is { } id && unidentifiedDeaths.TryGetValue(id, out var pending)
            && pending.Event.SessionId == session?.Id && pending.Event.MapEpoch == mapEpoch
            && pending.Event.CaptureGeneration == captureGeneration && pending.Event.CapturedAtUtc <= at
            && at - pending.Event.CapturedAtUtc <= PendingActorLifetime
            && (pending.KillerObjectId is null || packet.KillerObjectId is null || pending.KillerObjectId == packet.KillerObjectId)
            && (string.IsNullOrWhiteSpace(packet.VictimName) || pending.Event.TargetName is "" or "Unknown"
                || string.Equals(pending.Event.TargetName, packet.VictimName, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(packet.VictimName) || string.IsNullOrWhiteSpace(victim.Name)
                || string.Equals(victim.Name, packet.VictimName, StringComparison.OrdinalIgnoreCase)))
        {
            // Enrich the confirmed Died row. Party membership and grouping stay
            // frozen at that event; KilledPlayer itself never adds a kill count.
            var amendment = WithTarget(pending.Event, victim) with
            {
                Revision = pending.Event.Revision + 1,
                TargetRelationship = pending.Event.TargetRelationship,
                TargetName = string.IsNullOrWhiteSpace(victim.Name) ? pending.Event.TargetName : victim.Name,
                TargetGuild = string.IsNullOrWhiteSpace(victim.Guild) ? pending.Event.TargetGuild : victim.Guild,
                TargetAlliance = string.IsNullOrWhiteSpace(victim.Alliance) ? pending.Event.TargetAlliance : victim.Alliance
            };
            if (persistence.Amend(amendment)) unidentifiedDeaths.Remove(id);
            else storageSuspended = true;
        }
        var killer = actors.Resolve(packet.KillerObjectId);
        if (!Tracked(killer) && !Tracked(victim)) return;
        Persist(WithTarget(WithSource(Stamp(ActivityEventKind.OutcomeEvidence, at, Origin(packet, "KilledPlayer")), killer), victim) with
        {
            Evidence = ActivityEvidence.Supplemental,
            DetailsJson = JsonSerializer.Serialize(new
            {
                Signal = "KilledPlayer",
                packet.KillerObjectId,
                packet.VictimObjectId,
                packet.RawBaseFame,
                packet.BaseFameState
            })
        });
    }

    private static PendingActorConnection? ActorConnection(string connection, PhotonMessageIdentity? identity)
        => string.IsNullOrWhiteSpace(connection) || identity is null || identity.ReceiverGeneration == Guid.Empty
            || string.IsNullOrWhiteSpace(identity.DirectionalConnection)
            ? null
            : new(connection, identity.ReceiverGeneration, identity.DirectionalConnection, identity.PeerId, identity.Challenge);

    private readonly record struct PendingActorConnection(string Connection, Guid Generation, string Stream, short PeerId, int Challenge);
    private readonly record struct PendingActorKey(PendingActorConnection Connection, long ObjectId);
    private sealed record PendingActor(BaseEvent Packet, DateTime At);
    private sealed record PendingPlayerDeath(long? KillerObjectId, ActivityEvent Event);
}
