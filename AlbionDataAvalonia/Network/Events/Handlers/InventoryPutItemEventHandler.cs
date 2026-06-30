using Albion.Network;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class InventoryPutItemEventHandler : EventPacketHandler<InventoryPutItemEvent>
{
    private readonly LegendaryItemTrackerService legendaryTracker;

    public InventoryPutItemEventHandler(LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.InventoryPutItem)
    {
        this.legendaryTracker = legendaryTracker;
    }

    protected override Task OnActionAsync(InventoryPutItemEvent value)
    {
        return legendaryTracker.ObserveInventoryPutAsync(value.ItemObjectId, value.ContainerId);
    }
}
