using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class ChangeClusterResponseHandler(FarmingTrackerService tracker) : ResponsePacketHandler<ChangeClusterResponse>((int)OperationCodes.ChangeCluster)
{
    protected override Task OnActionAsync(ChangeClusterResponse value)
    {
        if (value.ReturnCode == 0) tracker.OnClusterChanged(value);
        return Task.CompletedTask;
    }
}
