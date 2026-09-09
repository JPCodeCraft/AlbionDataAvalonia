using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class FarmingActionResponseHandler(FarmingTrackerService tracker, OperationCodes operation)
    : ResponsePacketHandler<FarmingActionResponse>((int)operation)
{
    protected override Task OnActionAsync(FarmingActionResponse value)
    {
        tracker.OnActionResponse(operation, value);
        return Task.CompletedTask;
    }
}
