using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class PartyLootItemsRemovedEventHandler : EventPacketHandler<PartyLootItemsRemovedEvent>
{
    private readonly LootTrackerService lootTracker;

    public PartyLootItemsRemovedEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.PartyLootItemsRemoved)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(PartyLootItemsRemovedEvent value)
    {
        lootTracker.RecordPartyLootItemsRemoved(
            value.SourceObjectId,
            value.ItemObjectIds);
        return Task.CompletedTask;
    }
}
