using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetOffersResponseHandler : ResponsePacketHandler<AuctionGetOffersResponse>
{
    private readonly Uploader uploader;
    private readonly PlayerState playerState;
    public AuctionGetOffersResponseHandler(Uploader uploader, PlayerState playerState) : base((int)OperationCodes.AuctionGetOffers)
    {
        this.uploader = uploader;
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AuctionGetOffersResponse value)
    {
        if (!playerState.CheckOkToUpload()) return;

        playerState.HasEncryptedData = false;

        MarketUpload marketUpload = new MarketUpload();

        value.marketOrders.ForEach(x => x.LocationId = playerState.Location.Id);
        marketUpload.Orders.AddRange(value.marketOrders);

        if (marketUpload.Orders.Count > 0)
        {
            uploader.EnqueueUpload(new Upload(marketUpload, null, null));
        }
        await Task.CompletedTask;
    }
}
