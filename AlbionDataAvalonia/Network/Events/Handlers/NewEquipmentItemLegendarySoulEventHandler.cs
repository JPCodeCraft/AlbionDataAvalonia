using Albion.Network;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class NewEquipmentItemLegendarySoulEventHandler : EventPacketHandler<NewEquipmentItemLegendarySoulEvent>
{
    private readonly LegendaryItemTrackerService legendaryTracker;

    public NewEquipmentItemLegendarySoulEventHandler(LegendaryItemTrackerService legendaryTracker) : base((int)EventCodes.NewEquipmentItemLegendarySoul)
    {
        this.legendaryTracker = legendaryTracker;
    }

    protected override Task OnActionAsync(NewEquipmentItemLegendarySoulEvent value)
    {
        return value.LegendarySoul is null
            ? Task.CompletedTask
            : legendaryTracker.ObserveSoulAsync(value.LegendarySoul);
    }
}
