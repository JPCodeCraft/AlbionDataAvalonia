using AlbionDataAvalonia.Party.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Party;

public sealed class PartyTrackerService
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, string> partyMembers = new();

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
            localObjectId = objectId > 0 ? objectId : null;
            localUserGuid = userGuid is { } guid && guid != Guid.Empty ? guid : null;
            localPlayerName = name?.Trim() ?? string.Empty;
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
                if (guid != Guid.Empty && !string.IsNullOrWhiteSpace(name))
                {
                    partyMembers[guid] = name.Trim();
                }
            }

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
            partyMembers[userGuid] = name.Trim();
            snapshot = BuildSnapshot();
        }

        SnapshotChanged?.Invoke(snapshot);
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
            partyMembers.Remove(guid);
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
                string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase));
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

        foreach (var (guid, name) in partyMembers)
        {
            if (localUserGuid == guid
                || (!string.IsNullOrWhiteSpace(localPlayerName)
                    && string.Equals(localPlayerName, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            members.Add(new PartyMemberSnapshot(null, guid, name, false));
        }

        return new PartyTrackerSnapshot(
            members
                .OrderByDescending(member => member.IsLocalPlayer)
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}
