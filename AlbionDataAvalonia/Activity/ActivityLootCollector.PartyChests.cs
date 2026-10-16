using Albion.Network;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity;

internal sealed partial class ActivityLootCollector
{
    private readonly ActivityPartyChestLedger partyChests = new();

    // Reserved stacks can only be taken by their owners. One possible owner is the recipient;
    // several leave the pickup inferred without a recipient, as for any unattributed removal.
    private void RecordReservedPickups(IReadOnlyList<ActivityReservedPickup> taken, DateTime at, BaseEvent packet, bool record)
    {
        if (!record) return;
        for (var index = 0; index < taken.Count; index++)
        {
            var pickup = taken[index];
            var origin = ActivityTrackerService.Origin(packet, "ReservedPickup", index);
            var stack = new Item(0, pickup.ItemId, pickup.Quality, pickup.Amount, null, null, at, at, GameValue: pickup.UnitValue);
            var recipient = pickup.Owners.Count == 1 ? tracker.ResolvePartyRecipient(pickup.Owners) : null;
            if (recipient is not null) RecordAttributed(recipient, pickup.SourceId, pickup.ItemId, pickup.Amount, stack, null, at, origin, pickup.Proof);
            else AmbiguousRemoval(pickup.SourceId, pickup.ItemId, pickup.Amount, at, origin, null, stack, proof: pickup.Proof);
        }
    }
}
