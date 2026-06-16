using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class NewLootEventHandler : EventPacketHandler<NewLootEvent>
{
    private readonly LootTrackerService lootTracker;

    public NewLootEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.NewLoot)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(NewLootEvent value)
    {
        lootTracker.IdentifyLootSource(value.ObjectId, value.SourceName);
        return Task.CompletedTask;
    }
}
