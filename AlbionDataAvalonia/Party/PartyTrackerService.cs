using AlbionDataAvalonia.Party.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Party;

public sealed class PartyTrackerService
{
    private const int MaximumKnownNonPartyPlayers = 2048;
    private readonly object sync = new();
    private readonly Dictionary<Guid, PartyMemberState> partyMembers = new();
    private readonly Dictionary<Guid, PartyMemberState> knownPlayers = new();
    private readonly LinkedList<Guid> nonPartyRecency = new();
    private readonly Dictionary<Guid, LinkedListNode<Guid>> nonPartyNodes = new();

    private long? localObjectId;
    private Guid? localUserGuid;
    private string localPlayerName = string.Empty;

    public event Action<PartyTrackerSnapshot>? SnapshotChanged;

    public PartyTrackerSnapshot CurrentSnapshot
    {
        get
        {
            lock (sync)
            {
                return BuildSnapshot();
            }
        }
    }

    public void SetLocalPlayer(long objectId, Guid? userGuid, string name)
    {
        PartyTrackerSnapshot snapshot;
        lock (sync)
        {
            if (localUserGuid is not null && localUserGuid != userGuid)
            {
                partyMembers.Clear();
                ClearKnownPlayers();
            }
            localObjectId = objectId > 0 ? objectId : null;
            localUserGuid = userGuid is { } guid && guid != Guid.Empty ? guid : null;
            localPlayerName = name?.Trim() ?? string.Empty;
            if (localUserGuid is { } localGuid)
            {
                RememberKnownPlayer(localGuid, new PartyMemberState(localObjectId, localPlayerName));
            }

            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void ResetObjectBindings(bool clearRoster)
    {
        PartyTrackerSnapshot snapshot;
        lock (sync)
        {
            localObjectId = null;
            if (clearRoster)
            {
                partyMembers.Clear();
                ClearKnownPlayers();
            }
            else
            {
                foreach (var id in knownPlayers.Keys.ToArray())
                    if (id != localUserGuid && !partyMembers.ContainsKey(id)) ForgetKnownPlayer(id);
                    else knownPlayers[id] = knownPlayers[id] with { ObjectId = null };
                foreach (var id in partyMembers.Keys.ToArray())
                    partyMembers[id] = partyMembers[id] with { ObjectId = null };
            }
            snapshot = BuildSnapshot();
        }
        SnapshotChanged?.Invoke(snapshot);
    }

    public void SetPartySnapshot(IReadOnlyDictionary<Guid, string> members)
    {
        PartyTrackerSnapshot snapshot;
        lock (sync)
        {
            partyMembers.Clear();
            foreach (var (guid, name) in members)
            {
                if (guid == Guid.Empty || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var state = GetKnownPlayerState(guid) with { Name = name.Trim() };
                partyMembers[guid] = state;
                RememberKnownPlayer(guid, state);
            }
            ReconcileKnownPlayers();

            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void AddPartyMember(Guid userGuid, string name)
    {
        if (userGuid == Guid.Empty || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        PartyTrackerSnapshot snapshot;
        lock (sync)
        {
            var state = GetKnownPlayerState(userGuid) with { Name = name.Trim() };
            partyMembers[userGuid] = state;
            RememberKnownPlayer(userGuid, state);
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void EnsurePartyMember(Guid userGuid)
    {
        if (userGuid == Guid.Empty)
        {
            return;
        }

        PartyTrackerSnapshot snapshot;
        lock (sync)
        {
            if (localUserGuid == userGuid || partyMembers.ContainsKey(userGuid))
            {
                return;
            }

            partyMembers[userGuid] = GetKnownPlayerState(userGuid);
            RememberKnownPlayer(userGuid, partyMembers[userGuid]);
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void EnsurePartyMembers(IEnumerable<Guid> userGuids)
    {
        PartyTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            foreach (var userGuid in userGuids)
            {
                if (userGuid == Guid.Empty
                    || localUserGuid == userGuid
                    || partyMembers.ContainsKey(userGuid))
                {
                    continue;
                }

                partyMembers[userGuid] = GetKnownPlayerState(userGuid);
                RememberKnownPlayer(userGuid, partyMembers[userGuid]);
                snapshot = BuildSnapshot();
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void UpdatePartyMemberName(Guid? userGuid, string? name, long? objectId = null)
    {
        if (userGuid is not { } guid
            || guid == Guid.Empty)
        {
            return;
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedObjectId = objectId is > 0 ? objectId : null;
        if (string.IsNullOrWhiteSpace(normalizedName) && normalizedObjectId is null)
        {
            return;
        }

        PartyTrackerSnapshot? snapshot = null;
        lock (sync)
        {
            var knownState = GetKnownPlayerState(guid);
            var updatedKnownState = knownState with
            {
                ObjectId = normalizedObjectId ?? knownState.ObjectId,
                Name = string.IsNullOrWhiteSpace(normalizedName)
                    ? knownState.Name
                    : normalizedName
            };
            RememberKnownPlayer(guid, updatedKnownState);

            if (localUserGuid == guid)
            {
                var changed = false;
                if (normalizedObjectId is not null && localObjectId != normalizedObjectId)
                {
                    localObjectId = normalizedObjectId;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(normalizedName)
                    && !string.Equals(localPlayerName, normalizedName, StringComparison.Ordinal))
                {
                    localPlayerName = normalizedName;
                    changed = true;
                }

                if (changed)
                {
                    snapshot = BuildSnapshot();
                }
            }
            else if (partyMembers.TryGetValue(guid, out var currentState)
                && currentState != updatedKnownState)
            {
                partyMembers[guid] = updatedKnownState;
                snapshot = BuildSnapshot();
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(snapshot);
        }
    }

    public void RemovePartyMember(Guid? userGuid)
    {
        if (userGuid is not { } guid || guid == Guid.Empty)
        {
            return;
        }

        PartyTrackerSnapshot snapshot;
        lock (sync)
        {
            if (localUserGuid == guid)
            {
                partyMembers.Clear();
            }
            else
            {
                partyMembers.Remove(guid);
            }
            ReconcileKnownPlayers();

            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public void DisbandParty()
    {
        PartyTrackerSnapshot snapshot;
        lock (sync)
        {
            partyMembers.Clear();
            ReconcileKnownPlayers();
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public bool IsPartyMember(Guid? userGuid)
    {
        lock (sync)
        {
            if (userGuid is not { } guid || guid == Guid.Empty)
            {
                return false;
            }

            return localUserGuid == guid || partyMembers.ContainsKey(guid);
        }
    }

    public bool IsPartyMember(string? playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return false;
        }

        lock (sync)
        {
            if (string.Equals(localPlayerName, playerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return partyMembers.Values.Any(name =>
                string.Equals(name.Name, playerName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private PartyTrackerSnapshot BuildSnapshot()
    {
        var members = new List<PartyMemberSnapshot>(partyMembers.Count + 1);
        if (localObjectId is not null
            || localUserGuid is not null
            || !string.IsNullOrWhiteSpace(localPlayerName))
        {
            members.Add(new PartyMemberSnapshot(
                localObjectId,
                localUserGuid,
                localPlayerName,
                true));
        }

        foreach (var (guid, state) in partyMembers)
        {
            if (localUserGuid == guid
                || (!string.IsNullOrWhiteSpace(localPlayerName)
                    && string.Equals(localPlayerName, state.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            members.Add(new PartyMemberSnapshot(
                state.ObjectId,
                guid,
                string.IsNullOrWhiteSpace(state.Name) ? "Unknown" : state.Name,
                false));
        }

        return new PartyTrackerSnapshot(
            members
                .OrderByDescending(member => member.IsLocalPlayer)
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private PartyMemberState GetKnownPlayerState(Guid userGuid)
    {
        return knownPlayers.TryGetValue(userGuid, out var state)
            ? state
            : new PartyMemberState(null, string.Empty);
    }

    // The roster and local identity are retained; nearby non-party names are a
    // bounded lookup cache, not an application-long history of players seen.
    private void RememberKnownPlayer(Guid guid, PartyMemberState state)
    {
        knownPlayers[guid] = state;
        if (nonPartyNodes.Remove(guid, out var existing)) nonPartyRecency.Remove(existing);
        if (guid == localUserGuid || partyMembers.ContainsKey(guid)) return;
        nonPartyNodes[guid] = nonPartyRecency.AddLast(guid);
        while (nonPartyRecency.Count > MaximumKnownNonPartyPlayers)
            ForgetKnownPlayer(nonPartyRecency.First!.Value);
    }

    private void ReconcileKnownPlayers()
    {
        foreach (var guid in knownPlayers.Keys.ToArray())
        {
            if (guid == localUserGuid || partyMembers.ContainsKey(guid))
            {
                if (nonPartyNodes.Remove(guid, out var node)) nonPartyRecency.Remove(node);
            }
            else if (!nonPartyNodes.ContainsKey(guid))
                nonPartyNodes[guid] = nonPartyRecency.AddLast(guid);
        }
        while (nonPartyRecency.Count > MaximumKnownNonPartyPlayers)
            ForgetKnownPlayer(nonPartyRecency.First!.Value);
    }

    private void ForgetKnownPlayer(Guid guid)
    {
        knownPlayers.Remove(guid);
        if (nonPartyNodes.Remove(guid, out var node)) nonPartyRecency.Remove(node);
    }

    private void ClearKnownPlayers()
    {
        knownPlayers.Clear();
        nonPartyRecency.Clear();
        nonPartyNodes.Clear();
    }

    private sealed record PartyMemberState(long? ObjectId, string Name);
}
