using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class FarmBuildingInfoEventHandler(FarmingTrackerService tracker)
    : EventPacketHandler<FarmBuildingInfoEvent>((int)EventCodes.FarmBuildingInfo)
{
    protected override Task OnHandleAsync(EventPacket packet) =>
        packet.EventCode == (int)EventCodes.FarmBuildingInfo && !tracker.CanObserveObjects()
            ? NextAsync(packet)
            : base.OnHandleAsync(packet);

    protected override Task OnActionAsync(FarmBuildingInfoEvent value)
    {
        tracker.OnFarmBuilding(value);
        return Task.CompletedTask;
    }
}
