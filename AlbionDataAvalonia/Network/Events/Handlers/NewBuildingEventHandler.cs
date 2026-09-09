using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class NewBuildingEventHandler(FarmingTrackerService tracker) : EventPacketHandler<NewBuildingEvent>((int)EventCodes.NewBuilding)
{
    protected override Task OnHandleAsync(EventPacket packet) =>
        packet.EventCode == (int)EventCodes.NewBuilding && !tracker.CanObserveObjects()
            ? NextAsync(packet)
            : base.OnHandleAsync(packet);

    protected override Task OnActionAsync(NewBuildingEvent value)
    {
        tracker.OnBuilding(value);
        return Task.CompletedTask;
    }
}
