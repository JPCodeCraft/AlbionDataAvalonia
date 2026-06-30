using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class InventoryDeleteItemEventHandler : EventPacketHandler<InventoryDeleteItemEvent>
{
    private readonly LootTrackerService lootTracker;
    private readonly LegendaryItemTrackerService legendaryTracker;

    public InventoryDeleteItemEventHandler(LootTrackerService lootTracker, LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.InventoryDeleteItem)
    {
        this.lootTracker = lootTracker;
        this.legendaryTracker = legendaryTracker;
    }

    protected override async Task OnActionAsync(InventoryDeleteItemEvent value)
    {
        lootTracker.RecordInventoryDeleteItem(value.ItemObjectId);
        await legendaryTracker.ObserveInventoryDeleteAsync(value.ItemObjectId);
    }
}
