using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class InventoryMoveItemRequestHandler : RequestPacketHandler<InventoryMoveItemRequest>
{
    private readonly LootTrackerService lootTracker;

    public InventoryMoveItemRequestHandler(LootTrackerService lootTracker) : base((int)OperationCodes.InventoryMoveItem)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(InventoryMoveItemRequest value)
    {
        lootTracker.QueueLocalMoveBySlot(
            value.SourceSlot,
            value.SourceContainerId,
            value.DestinationContainerId);
        return Task.CompletedTask;
    }
}
