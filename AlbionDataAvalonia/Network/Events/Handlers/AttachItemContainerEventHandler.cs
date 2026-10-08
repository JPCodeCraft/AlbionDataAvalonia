using Albion.Network;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AttachItemContainerEventHandler : EventPacketHandler<AttachItemContainerEvent>
{
    private readonly LegendaryItemTrackerService legendaryTracker;

    public AttachItemContainerEventHandler(LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.AttachItemContainer)
    {
        this.legendaryTracker = legendaryTracker;
    }

    protected override async Task OnActionAsync(AttachItemContainerEvent value)
    {
        await legendaryTracker.ObserveContainerAsync(
            value.ObjectId,
            value.ContainerId,
            value.PrivateContainerId,
            value.SlotItems);
    }
}
