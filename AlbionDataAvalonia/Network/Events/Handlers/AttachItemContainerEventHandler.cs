using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AttachItemContainerEventHandler : EventPacketHandler<AttachItemContainerEvent>
{
    private readonly LootTrackerService lootTracker;

    public AttachItemContainerEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.AttachItemContainer)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(AttachItemContainerEvent value)
    {
        lootTracker.AttachContainer(
            value.ObjectId,
            value.ContainerId,
            value.PrivateContainerId,
            value.SlotItems);
        return Task.CompletedTask;
    }
}
