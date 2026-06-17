using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class UpdateLootChestEventHandler : EventPacketHandler<UpdateLootChestEvent>
{
    private readonly LootTrackerService lootTracker;

    public UpdateLootChestEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.UpdateLootChest)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(UpdateLootChestEvent value)
    {
        lootTracker.UpdateLootChest(value.ObjectId, value.PlayerGuids, value.State);
        return Task.CompletedTask;
    }
}
