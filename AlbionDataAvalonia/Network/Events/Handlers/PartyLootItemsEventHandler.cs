using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class PartyLootItemsEventHandler : EventPacketHandler<PartyLootItemsEvent>
{
    private readonly LootTrackerService lootTracker;

    public PartyLootItemsEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.PartyLootItems)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(PartyLootItemsEvent value)
    {
        lootTracker.TrackPartyLootItems(
            value.SourceObjectId,
            value.ItemObjectIds,
            value.ItemIds,
            value.Amounts,
            value.PlayerNames);
        return Task.CompletedTask;
    }
}
