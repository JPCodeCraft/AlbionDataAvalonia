using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AssetOverviewResponseHandler : ResponsePacketHandler<AssetOverviewResponse>
{
    private readonly PlayerState playerState;

    public AssetOverviewResponseHandler(PlayerState playerState) : base((int)OperationCodes.AssetOverview)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AssetOverviewResponse value)
    {
        await Task.CompletedTask;
    }
}
