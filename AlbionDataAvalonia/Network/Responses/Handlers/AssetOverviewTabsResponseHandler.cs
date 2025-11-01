using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AssetOverviewTabsResponseHandler : ResponsePacketHandler<AssetOverviewTabsResponse>
{
    private readonly PlayerState playerState;

    public AssetOverviewTabsResponseHandler(PlayerState playerState) : base((int)OperationCodes.AssetOverviewTabs)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AssetOverviewTabsResponse value)
    {
        await Task.CompletedTask;
    }
}
