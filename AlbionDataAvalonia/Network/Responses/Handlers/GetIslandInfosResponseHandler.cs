using Albion.Network;
using AlbionDataAvalonia.Farming;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public sealed class GetIslandInfosResponseHandler(FarmingTrackerService tracker) : ResponsePacketHandler<GetIslandInfosResponse>((int)OperationCodes.GetIslandInfos)
{
    protected override Task OnActionAsync(GetIslandInfosResponse value)
    {
        if (value.ReturnCode == 0 && value.IsValid) tracker.OnIslandList(value);
        return Task.CompletedTask;
    }
}
