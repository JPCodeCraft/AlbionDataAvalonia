using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class PartyLootItemTypesRemovedEventHandler : EventPacketHandler<PartyLootItemTypesRemovedEvent>
{
    private readonly LootTrackerService lootTracker;

    public PartyLootItemTypesRemovedEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.PartyLootItemTypesRemoved)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(PartyLootItemTypesRemovedEvent value)
    {
        lootTracker.RecordPartyLootItemTypesRemoved(
            value.SourceObjectId,
            value.ItemIds,
            value.Amounts,
            value.Qualities);
        return Task.CompletedTask;
    }
}
