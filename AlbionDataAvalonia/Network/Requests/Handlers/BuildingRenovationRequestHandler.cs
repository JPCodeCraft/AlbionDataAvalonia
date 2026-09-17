using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class BuildingRenovationRequestHandler(FarmingTrackerService tracker)
    : RequestPacketHandler<BuildingRenovationRequest>((int)OperationCodes.BuildingChangeRenovationState)
{
    protected override Task OnActionAsync(BuildingRenovationRequest value)
    {
        tracker.OnRenovationRequest(value);
        return Task.CompletedTask;
    }
}
