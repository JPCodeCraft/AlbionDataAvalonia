using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Party;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityTrackerService
{
    private readonly PartyTrackerService partyRoster;
    // Members already journaled for the open session. Roster differences become join/leave events.
    private readonly Dictionary<Guid, string> recordedParty = new();
    private Guid? recordedPartySessionId;
    private bool partyHasUnnamedMembers;

    // Party packets update the roster before activity observers run. A session's first sync
    // journals the members already present, so each session records who it started with.
    private void SyncParty(DateTime at, string? origin)
    {
        if (session is null || !CanRecord) return;
        var roster = recordedPartySessionId != session.Id;
        if (roster)
        {
            recordedParty.Clear();
            recordedPartySessionId = session.Id;
        }
        var snapshot = partyRoster.CurrentSnapshot;
        var current = new Dictionary<Guid, string>();
        partyHasUnnamedMembers = false;
        foreach (var member in snapshot.Members)
        {
            if (member.IsLocalPlayer || member.UserGuid is not { } guid) continue;
            if (string.IsNullOrWhiteSpace(member.Name) || member.Name == "Unknown") partyHasUnnamedMembers = true;
            else current[guid] = member.Name;
        }
        var size = snapshot.Members.Count;
        var entry = 0;
        foreach (var (guid, name) in recordedParty.Where(x => !current.ContainsKey(x.Key)).ToArray())
        {
            recordedParty.Remove(guid);
            Persist(PartyEvent(ActivityEventKind.PartyLeft, at, origin, entry++, guid, name, false, size));
        }
        foreach (var (guid, name) in current.Where(x => !recordedParty.ContainsKey(x.Key)).ToArray())
        {
            recordedParty[guid] = name;
            Persist(PartyEvent(ActivityEventKind.PartyJoined, at, origin, entry++, guid, name, roster, size));
        }
    }

    private ActivityEvent PartyEvent(ActivityEventKind kind, DateTime at, string? origin, int entry,
        Guid guid, string name, bool roster, int size)
    {
        // A departed member is no longer in the roster; the event still describes a party member.
        var resolved = actors.Resolve(null, name);
        var actor = resolved with
        {
            Id = $"{actors.ServerId}:{guid}",
            CharacterId = guid,
            Name = name,
            Kind = ActivityActorKind.Player,
            Relationship = ActorRelationship.Party
        };
        return WithSource(Stamp(kind, at, origin is null ? null : $"{origin}:{entry}"), actor) with
        {
            EncounterId = null,
            DetailsJson = JsonSerializer.Serialize(new ActivityPartyDetails(guid, roster, size))
        };
    }
}
