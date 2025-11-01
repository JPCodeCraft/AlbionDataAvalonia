using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AssetOverviewUnfreezeCacheResponseHandler : ResponsePacketHandler<AssetOverviewUnfreezeCacheResponse>
{
    private readonly PlayerState playerState;

    public AssetOverviewUnfreezeCacheResponseHandler(PlayerState playerState) : base((int)OperationCodes.AssetOverviewUnfreezeCache)
    {
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AssetOverviewUnfreezeCacheResponse value)
    {
        await Task.CompletedTask;
    }
}
