using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class InventoryMoveGivenItemsRequestHandler : RequestPacketHandler<InventoryMoveGivenItemsRequest>
{
    private readonly LootTrackerService lootTracker;

    public InventoryMoveGivenItemsRequestHandler(LootTrackerService lootTracker) : base((int)OperationCodes.InventoryMoveGivenItems)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(InventoryMoveGivenItemsRequest value)
    {
        lootTracker.QueueLocalMoveGivenItems(
            value.SourceContainerId,
            value.DestinationContainerId,
            value.ItemObjectIds);
        return Task.CompletedTask;
    }
}
