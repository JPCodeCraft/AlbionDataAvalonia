using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class InventoryPutItemEventHandler : EventPacketHandler<InventoryPutItemEvent>
{
    private readonly LootTrackerService lootTracker;

    public InventoryPutItemEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.InventoryPutItem)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(InventoryPutItemEvent value)
    {
        lootTracker.RecordInventoryPutItem(value.ItemObjectId);
        return Task.CompletedTask;
    }
}
