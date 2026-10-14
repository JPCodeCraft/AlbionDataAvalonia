using System;
using System.Collections.Generic;
using System.Linq;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Party;

namespace AlbionDataAvalonia.Activity;

internal sealed record ActivityActor(
    string Id, long? ObjectId, Guid? CharacterId, string Name, string Guild, string Alliance,
    ActivityActorKind Kind, ActorRelationship Relationship, Guid LifetimeId,
    int? MobIndex = null, string? MobUniqueName = null);

// Object IDs are temporary map bindings. Saved events carry copies, never references here.
internal sealed class ActivityActorContext(PartyTrackerService party, MobsService mobs)
{
    internal static readonly TimeSpan BindingGrace = TimeSpan.FromSeconds(10);
    private const int MaximumTerminalBindings = 8192;
    private readonly Dictionary<long, Entity> entities = new();
    private readonly Dictionary<long, TerminalBinding> terminals = new();
    private readonly Queue<(long ObjectId, Guid Lifetime)> terminalOrder = new();
    private int? serverId;
    private Guid? characterId;
    private long? localObjectId;
    private string localName = "";
    private string localGuild = "";
    private string localAlliance = "";
    private long epoch;

    public int? ServerId => serverId;
    public string CharacterId => characterId?.ToString() ?? "";
    public string CharacterName => localName;
    public bool HasIdentity => serverId is > 0 && localObjectId is > 0 && localName.Length > 0;

    public void Join(JoinResponse packet, int? server, long mapEpoch)
    {
        var switched = serverId != server || characterId != packet.userGuid
            || characterId is null && localName.Length > 0 && localName != packet.playerName;
        party.ResetObjectBindings(switched);
        entities.Clear();
        terminals.Clear();
        terminalOrder.Clear();
        serverId = server;
        characterId = packet.userGuid;
        localObjectId = packet.userObjectId;
        localName = packet.playerName ?? "";
        localGuild = packet.guildName ?? "";
        localAlliance = packet.allianceName ?? "";
        epoch = mapEpoch;
        party.SetLocalPlayer(packet.userObjectId, packet.userGuid, localName);
        entities[packet.userObjectId] = new Entity(Guid.NewGuid(), packet.userGuid, localName,
            localGuild, localAlliance, ActivityActorKind.Player);
    }

    public void Suspend()
    {
        entities.Clear();
        terminals.Clear();
        terminalOrder.Clear();
        party.ResetObjectBindings(false);
        localObjectId = null;
    }

    public void Character(NewCharacterEvent packet)
    {
        if (packet.ObjectId is not > 0) return;
        var id = packet.ObjectId.Value;
        var lifetime = entities.TryGetValue(id, out var previous) && previous.Kind == ActivityActorKind.Player
            && !previous.Terminal && previous.CharacterId == packet.Guid ? previous.Lifetime : Guid.NewGuid();
        terminals.Remove(id);
        entities[id] = new Entity(lifetime, packet.Guid, packet.Name,
            packet.GuildName ?? "", packet.AllianceName ?? "", ActivityActorKind.Player);
        party.UpdatePartyMemberName(packet.Guid, packet.Name, id);
    }

    public void Mob(long objectId, int? index, double? health)
    {
        if (objectId <= 0) return;
        var descriptor = mobs.GetMobDescriptor(index);
        var terminal = terminals.GetValueOrDefault(objectId);
        var fresh = !entities.TryGetValue(objectId, out var previous)
            || previous.Kind != ActivityActorKind.Mob || (previous.Terminal && health is > 0);
        // Metadata without positive health cannot reopen a retired dead lifetime.
        if (terminal is { Kind: ActivityActorKind.Mob } && health is not > 0)
        {
            entities[objectId] = new Entity(terminal.Lifetime,
                null, descriptor?.DisplayName ?? $"Mob {index?.ToString() ?? "?"}", "", "",
                ActivityActorKind.Mob, index, descriptor?.UniqueName, true, TerminalAt: terminal.At);
            return;
        }
        terminals.Remove(objectId);
        entities[objectId] = new Entity(fresh ? Guid.NewGuid() : previous!.Lifetime,
            null, descriptor?.DisplayName ?? $"Mob {index?.ToString() ?? "?"}", "", "",
            ActivityActorKind.Mob, index, descriptor?.UniqueName,
            !fresh && previous!.Terminal, TerminalAt: fresh ? null : previous!.TerminalAt);
    }

    public void MarkTerminal(long objectId, DateTime at)
    {
        if (objectId <= 0 || IsTerminal(objectId)) return;
        if (!entities.TryGetValue(objectId, out var value))
            value = new Entity(Guid.NewGuid(), null, "", "", "", ActivityActorKind.Unknown);
        entities[objectId] = value with { Terminal = true, TerminalAt = at };
        terminals[objectId] = new TerminalBinding(value.Lifetime, value.Kind, at);
        terminalOrder.Enqueue((objectId, value.Lifetime));
        while (terminalOrder.Count > MaximumTerminalBindings)
        {
            var oldest = terminalOrder.Dequeue();
            if (terminals.TryGetValue(oldest.ObjectId, out var terminal) && terminal.Lifetime == oldest.Lifetime)
                terminals.Remove(oldest.ObjectId);
        }
    }

    public ActivityActor ConfirmPlayer(long? objectId, string? observedName)
    {
        if (objectId is not > 0) return Resolve(objectId, observedName) with { Kind = ActivityActorKind.Player };
        var id = objectId.Value;
        if (entities.TryGetValue(id, out var previous) && previous.Kind == ActivityActorKind.Player)
        {
            if (string.IsNullOrWhiteSpace(previous.Name) && !string.IsNullOrWhiteSpace(observedName))
                entities[id] = previous with { Name = observedName };
            return Resolve(id, observedName);
        }

        // KilledPlayer proves the victim's type, but does not prove a new lifetime
        // or another lethal outcome. Preserve an earlier Died tombstone.
        var terminal = terminals.GetValueOrDefault(id);
        var sameLifetime = previous?.Kind == ActivityActorKind.Unknown;
        var keepTerminal = terminal?.Kind is ActivityActorKind.Unknown or ActivityActorKind.Player;
        var lifetime = sameLifetime ? previous!.Lifetime : keepTerminal ? terminal!.Lifetime : Guid.NewGuid();
        var member = party.CurrentSnapshot.Members.SingleOrDefault(x =>
            !string.IsNullOrWhiteSpace(observedName) && string.Equals(x.Name, observedName, StringComparison.OrdinalIgnoreCase));
        entities[id] = new Entity(lifetime, member?.UserGuid, observedName ?? previous?.Name ?? "", "", "",
            ActivityActorKind.Player, Terminal: keepTerminal, TerminalAt: keepTerminal ? terminal!.At : null);
        if (keepTerminal) terminals[id] = terminal! with { Kind = ActivityActorKind.Player };
        else terminals.Remove(id);
        return Resolve(id, observedName);
    }

    public bool IsTerminal(long? objectId) => objectId is { } id &&
        (terminals.ContainsKey(id) || entities.TryGetValue(id, out var entity) && entity.Terminal);

    public bool HasLifetime(long objectId, Guid lifetime) => entities.TryGetValue(objectId, out var entity)
        && (lifetime == Guid.Empty || entity.Lifetime == lifetime);

    public void Leave(long objectId, DateTime at)
    {
        if (entities.TryGetValue(objectId, out var value)) entities[objectId] = value with { LeftAt = at };
    }

    public void Expire(DateTime at)
    {
        foreach (var id in entities.Where(x => x.Key != localObjectId &&
            (x.Value.LeftAt is { } left && at - left > BindingGrace ||
             x.Value.TerminalAt is { } terminal && at - terminal > BindingGrace))
            .Select(x => x.Key).ToArray()) entities.Remove(id);
    }

    public ActivityActor Local => Resolve(localObjectId);

    public ActivityActor Resolve(long? objectId, string? observedName = null, string? observedGuild = null)
    {
        if (objectId is { } id && entities.TryGetValue(id, out var entity))
        {
            var relation = id == localObjectId ? ActorRelationship.Self
                : entity.Kind == ActivityActorKind.Player
                    ? party.IsPartyMember(entity.CharacterId) ? ActorRelationship.Party : ActorRelationship.Other
                    : ActorRelationship.Other;
            var actorId = entity.CharacterId is { } guid ? $"{serverId}:{guid}" : $"{serverId}:{epoch}:{entity.Lifetime}";
            return new ActivityActor(actorId, id, entity.CharacterId,
                string.IsNullOrWhiteSpace(entity.Name) ? observedName ?? "Unknown" : entity.Name,
                entity.Guild, entity.Alliance, entity.Kind, relation, entity.Lifetime,
                entity.MobIndex, entity.UniqueName);
        }

        if (!string.IsNullOrWhiteSpace(observedName))
        {
            var matching = entities.Where(x => x.Value.Kind == ActivityActorKind.Player
                && string.Equals(x.Value.Name, observedName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length == 1) return Resolve(matching[0].Key);
            var member = party.CurrentSnapshot.Members.SingleOrDefault(x =>
                string.Equals(x.Name, observedName, StringComparison.OrdinalIgnoreCase));
            if (member is not null)
                return new ActivityActor(member.UserGuid is { } guid ? $"{serverId}:{guid}" : $"name:{serverId}:{observedName}",
                    null, member.UserGuid, observedName, observedGuild ?? "", "", ActivityActorKind.Player,
                    member.IsLocalPlayer ? ActorRelationship.Self : ActorRelationship.Party, Guid.Empty);
            return new ActivityActor($"name:{serverId}:{observedName}", objectId, null, observedName,
                observedGuild ?? "", "", ActivityActorKind.Player, ActorRelationship.Other, Guid.Empty);
        }

        return new ActivityActor($"unknown:{serverId}:{epoch}:{objectId}", objectId, null, "Unknown",
            observedGuild ?? "", "", ActivityActorKind.Unknown, ActorRelationship.Unknown, Guid.Empty);
    }

    private sealed record Entity(Guid Lifetime, Guid? CharacterId, string Name, string Guild,
        string Alliance, ActivityActorKind Kind, int? MobIndex = null, string? UniqueName = null,
        bool Terminal = false, DateTime? LeftAt = null, DateTime? TerminalAt = null);
    private sealed record TerminalBinding(Guid Lifetime, ActivityActorKind Kind, DateTime At);
}
