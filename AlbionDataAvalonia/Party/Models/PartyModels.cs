using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Party.Models;

public sealed record PartyMemberSnapshot(
    long? ObjectId,
    Guid? UserGuid,
    string Name,
    bool IsLocalPlayer);

public sealed record PartyTrackerSnapshot(
    IReadOnlyList<PartyMemberSnapshot> Members)
{
    public static PartyTrackerSnapshot Empty() => new(Array.Empty<PartyMemberSnapshot>());
}
