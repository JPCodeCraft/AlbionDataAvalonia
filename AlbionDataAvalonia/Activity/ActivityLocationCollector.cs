using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

// Keeps a small, map-scoped entrance cache. A request selects a candidate;
// only a successful Join associates its metadata with an instance.
internal sealed class ActivityLocationCollector
{
    private static readonly TimeSpan EntranceLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TransitionLifetime = TimeSpan.FromMinutes(2);
    private readonly Dictionary<long, Entrance> entrances = new();
    private long? selectedId;
    private Entrance? selectedEntrance;
    private DateTime selectedAt;
    private ChangeClusterResponse? transition;
    private DateTime transitionAt;

    public void Observe(NewRandomDungeonExitEvent packet, string rawMapId, DateTime at)
    {
        if (packet.ObjectId is not > 0 || string.IsNullOrWhiteSpace(packet.UniqueName)) return;
        foreach (var id in entrances.Where(x => at - x.Value.SeenAt > EntranceLifetime).Select(x => x.Key).ToArray())
            entrances.Remove(id);
        if (entrances.Count >= 256 && !entrances.ContainsKey(packet.ObjectId.Value))
            entrances.Remove(entrances.MinBy(x => x.Value.SeenAt).Key);
        var entry = new Entrance(packet, rawMapId, at);
        entrances[packet.ObjectId.Value] = entry;
        if (selectedId == packet.ObjectId && Fresh(at, selectedAt, TransitionLifetime)) selectedEntrance = entry;
    }

    public void Observe(ExitEnterStartRequest packet, DateTime at)
    {
        ClearPending();
        selectedId = packet.TargetObjectId;
        selectedAt = at;
        selectedEntrance = selectedId is { } id && entrances.TryGetValue(id, out var entry) ? entry : null;
    }

    public void Leave(long id, DateTime at)
    {
        if (entrances.TryGetValue(id, out var entry)) entrances[id] = entry with { LeftAt = at };
        // Old-map objects disappear before Join. Preserve the selected snapshot.
    }

    public void Observe(ChangeClusterResponse packet, DateTime at)
    {
        if (packet.ReturnCode != 0) { ClearPending(); return; }
        transition = packet;
        transitionAt = at;
    }

    public ActivityMapMetadata Join(JoinResponse packet, ActivityLocationDescriptor next,
        string? previousMapId, bool sameCharacter, DateTime at)
    {
        var origin = next.ParentClusterId ?? next.SourceClusterId;
        var result = new ActivityMapMetadata(Tier: next.Tier, ZoneType: next.ZoneType,
            OriginMapId: origin == next.RawLocationId ? null : origin);
        if (sameCharacter && previousMapId is not null)
        {
            var sourceMatches = string.Equals(previousMapId, next.SourceClusterId, StringComparison.Ordinal)
                || string.Equals(previousMapId, next.ParentClusterId, StringComparison.Ordinal);
            Entrance? entrance = null;
            if (sourceMatches && selectedId is not null && Fresh(at, selectedAt, TransitionLifetime)
                && selectedEntrance is { } selected && Usable(selected, previousMapId, next.Kind, at)
                && (packet.SourceExitPosition is not { } joinPosition || selected.Packet.SourceExitPosition is not { } selectedPosition
                    || DistanceSquared(selectedPosition, joinPosition) <= 25))
                entrance = selectedEntrance;
            if (entrance is null && sourceMatches && packet.SourceExitPosition is { } position)
            {
                var matches = entrances.Values.Where(x => Usable(x, previousMapId, next.Kind, at)
                    && x.Packet.SourceExitPosition is { } source && DistanceSquared(source, position) <= 25).Take(2).ToArray();
                if (matches.Length == 1) entrance = matches[0];
            }
            if (entrance is not null && Usable(entrance, previousMapId, next.Kind, at))
                result = result.Merge(FromCore(ActivityInstanceMetadata.FromEntrance(entrance.Packet.UniqueName,
                    entrance.Packet.DungeonType, entrance.Packet.Level, entrance.Packet.IsAlreadyEntered)));

            if (transition is { } change && Fresh(at, transitionAt, TransitionLifetime)
                && string.Equals(change.Destination, next.RawLocationId, StringComparison.Ordinal))
                result = result.Merge(FromCore(ActivityInstanceMetadata.FromTransition(next.Kind, change.LayoutId, change.DungeonInformation)));
        }
        Reset();
        return result;
    }

    public void ClearPending()
    {
        selectedId = null;
        selectedEntrance = null;
        transition = null;
    }

    public void Reset() { entrances.Clear(); ClearPending(); }

    public static ActivityMapMetadata FromCore(ActivityInstanceMetadata value) => new(value.Tier,
        value.ZoneType, value.Mode, value.Faction, value.Enchantment, value.Rarity);

    private static bool Usable(Entrance entry, string sourceMapId, ActivityMapKind kind, DateTime at) =>
        entry.MapId == sourceMapId && Fresh(at, entry.SeenAt, EntranceLifetime)
        && (entry.LeftAt is null || Fresh(at, entry.LeftAt.Value, TransitionLifetime))
        && (kind == ActivityMapKind.Mists && entry.Packet.UniqueName?.StartsWith("MISTS_", StringComparison.Ordinal) == true
            || kind == ActivityMapKind.RandomDungeon && entry.Packet.UniqueName is { Length: > 2 } name
                && name[0] == 'T' && name[1] is >= '1' and <= '8' && name[2] == '_');

    private static bool Fresh(DateTime now, DateTime observed, TimeSpan lifetime) => now >= observed && now - observed <= lifetime;
    private static double DistanceSquared(ActivityWorldPosition a, ActivityWorldPosition b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    private sealed record Entrance(NewRandomDungeonExitEvent Packet, string MapId, DateTime SeenAt, DateTime? LeftAt = null);
}
