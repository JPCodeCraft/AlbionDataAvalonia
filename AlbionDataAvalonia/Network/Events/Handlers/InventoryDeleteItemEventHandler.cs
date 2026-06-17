using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class InventoryDeleteItemEventHandler : EventPacketHandler<InventoryDeleteItemEvent>
{
    private readonly LootTrackerService lootTracker;

    public InventoryDeleteItemEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.InventoryDeleteItem)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(InventoryDeleteItemEvent value)
    {
        lootTracker.RecordInventoryDeleteItem(value.ItemObjectId);
        return Task.CompletedTask;
    }
}
