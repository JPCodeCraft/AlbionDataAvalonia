using Albion.Network;
using AlbionData.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetRequestsResponseHandler : ResponsePacketHandler<AuctionGetRequestsResponse>
{
    private readonly Uploader uploader;
    private readonly PlayerState playerState;
    public AuctionGetRequestsResponseHandler(Uploader uploader, PlayerState playerState) : base((int)OperationCodes.AuctionGetRequests)
    {
        this.uploader = uploader;
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AuctionGetRequestsResponse value)
    {
        if (!playerState.CheckLocationIDIsSet()) return;

        MarketUpload marketUpload = new MarketUpload();

        value.marketOrders.ForEach(x => x.LocationId = (ushort)playerState.Location);
        marketUpload.Orders.AddRange(value.marketOrders);

        if (marketUpload.Orders.Count > 0)
        {
            uploader.EnqueueUpload(new Upload(marketUpload, null, null));
        }
        await Task.CompletedTask;
    }
}
