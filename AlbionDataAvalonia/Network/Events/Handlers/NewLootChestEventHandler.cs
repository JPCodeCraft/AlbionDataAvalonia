using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class NewLootChestEventHandler : EventPacketHandler<NewLootChestEvent>
{
    private readonly LootTrackerService lootTracker;

    public NewLootChestEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.NewLootChest)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(NewLootChestEvent value)
    {
        lootTracker.IdentifyLootChest(
            value.ObjectId,
            value.UniqueName,
            value.UniqueNameWithLocation);
        return Task.CompletedTask;
    }
}
