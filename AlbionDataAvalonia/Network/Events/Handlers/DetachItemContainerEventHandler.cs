using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class DetachItemContainerEventHandler : EventPacketHandler<DetachItemContainerEvent>
{
    private readonly LootTrackerService lootTracker;

    public DetachItemContainerEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.DetachItemContainer)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(DetachItemContainerEvent value)
    {
        lootTracker.DetachContainer(value.ContainerId);
        return Task.CompletedTask;
    }
}
