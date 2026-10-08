using Albion.Network;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class DetachItemContainerEventHandler : EventPacketHandler<DetachItemContainerEvent>
{
    private readonly LegendaryItemTrackerService legendaryTracker;

    public DetachItemContainerEventHandler(LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.DetachItemContainer)
    {
        this.legendaryTracker = legendaryTracker;
    }

    protected override Task OnActionAsync(DetachItemContainerEvent value)
    {
        return legendaryTracker.DetachContainerAsync(value.ContainerId);
    }
}
