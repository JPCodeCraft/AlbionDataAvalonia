using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class FarmingActionRequestHandler(FarmingTrackerService tracker, OperationCodes operation)
    : RequestPacketHandler<FarmingActionRequest>((int)operation)
{
    protected override Task OnActionAsync(FarmingActionRequest value)
    {
        tracker.OnActionRequest(operation, value);
        return Task.CompletedTask;
    }
}
