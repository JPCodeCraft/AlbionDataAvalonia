using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class LootChestOpenedEventHandler : EventPacketHandler<LootChestOpenedEvent>
{
    private readonly LootTrackerService lootTracker;

    public LootChestOpenedEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.LootChestOpened)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(LootChestOpenedEvent value)
    {
        lootTracker.MarkLootChest(value.ObjectId);
        return Task.CompletedTask;
    }
}
