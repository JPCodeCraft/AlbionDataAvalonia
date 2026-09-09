using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class FarmableObjectInfoEventHandler(FarmingTrackerService tracker) : EventPacketHandler<FarmableObjectInfoEvent>((int)EventCodes.FarmableObjectInfo)
{
    protected override Task OnHandleAsync(EventPacket packet) =>
        packet.EventCode == (int)EventCodes.FarmableObjectInfo && !tracker.CanObserveObjects()
            ? NextAsync(packet)
            : base.OnHandleAsync(packet);

    protected override Task OnActionAsync(FarmableObjectInfoEvent value)
    {
        tracker.OnFarmable(value);
        return Task.CompletedTask;
    }
}
