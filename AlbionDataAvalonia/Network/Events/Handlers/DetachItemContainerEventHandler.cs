using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class DetachItemContainerEventHandler : EventPacketHandler<DetachItemContainerEvent>
{
    private readonly LootTrackerService lootTracker;
    private readonly LegendaryItemTrackerService legendaryTracker;

    public DetachItemContainerEventHandler(LootTrackerService lootTracker, LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.DetachItemContainer)
    {
        this.lootTracker = lootTracker;
        this.legendaryTracker = legendaryTracker;
    }

    protected override Task OnActionAsync(DetachItemContainerEvent value)
    {
        lootTracker.DetachContainer(value.ContainerId);
        return legendaryTracker.DetachContainerAsync(value.ContainerId);
    }
}
