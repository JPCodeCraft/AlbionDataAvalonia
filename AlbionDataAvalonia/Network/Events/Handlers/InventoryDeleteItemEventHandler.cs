using Albion.Network;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class InventoryDeleteItemEventHandler : EventPacketHandler<InventoryDeleteItemEvent>
{
    private readonly LegendaryItemTrackerService legendaryTracker;

    public InventoryDeleteItemEventHandler(LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.InventoryDeleteItem)
    {
        this.legendaryTracker = legendaryTracker;
    }

    protected override async Task OnActionAsync(InventoryDeleteItemEvent value)
    {
        await legendaryTracker.ObserveInventoryDeleteAsync(value.ItemObjectId);
    }
}
