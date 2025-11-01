using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AssetOverviewTabContentResponseHandler : ResponsePacketHandler<AssetOverviewTabContentResponse>
{
    private readonly PlayerState playerState;
    public AssetOverviewTabContentResponseHandler(PlayerState playerState) : base((int)OperationCodes.AssetOverviewTabContent)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AssetOverviewTabContentResponse value)
    {
        await Task.CompletedTask;
    }
}
