using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AttachItemContainerEventHandler : EventPacketHandler<AttachItemContainerEvent>
{
    private readonly LootTrackerService lootTracker;
    private readonly LegendaryItemTrackerService legendaryTracker;

    public AttachItemContainerEventHandler(LootTrackerService lootTracker, LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.AttachItemContainer)
    {
        this.lootTracker = lootTracker;
        this.legendaryTracker = legendaryTracker;
    }

    protected override async Task OnActionAsync(AttachItemContainerEvent value)
    {
        lootTracker.AttachContainer(
            value.ObjectId,
            value.ContainerId,
            value.PrivateContainerId,
            value.SlotItems);
        await legendaryTracker.ObserveContainerAsync(
            value.ObjectId,
            value.ContainerId,
            value.PrivateContainerId,
            value.SlotItems);
    }
}
