using Albion.Network;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class OtherGrabbedLootEventHandler : EventPacketHandler<OtherGrabbedLootEvent>
{
    private readonly LootTrackerService lootTracker;

    public OtherGrabbedLootEventHandler(LootTrackerService lootTracker) : base((int)EventCodes.OtherGrabbedLoot)
    {
        this.lootTracker = lootTracker;
    }

    protected override Task OnActionAsync(OtherGrabbedLootEvent value)
    {
        lootTracker.RecordOtherPickup(
            value.SourceName,
            value.PlayerName,
            value.IsSilver,
            value.ItemId,
            value.Amount);
        return Task.CompletedTask;
    }
}
