using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetLoadoutOffersResponseHandler : ResponsePacketHandler<AuctionGetLoadoutOffersResponse>
{
    private readonly Uploader uploader;
    private readonly PlayerState playerState;
    public AuctionGetLoadoutOffersResponseHandler(Uploader uploader, PlayerState playerState) : base((int)OperationCodes.AuctionGetLoadoutOffers)
    {
        this.uploader = uploader;
        this.playerState = playerState;
    }

    protected override async Task OnActionAsync(AuctionGetLoadoutOffersResponse value)
    {
        if (!playerState.CheckOkToUpload()) return;

        MarketUpload marketUpload = new MarketUpload();

        value.marketOrders.ForEach(x =>
        {
            if (x.LocationId == null) x.LocationId = playerState.Location.IdInt ?? -2;
        });

        marketUpload.Orders.AddRange(value.marketOrders);

        if (marketUpload.Orders.Count > 0)
        {
            uploader.EnqueueUpload(new Upload(marketUpload, null, null));
        }
        await Task.CompletedTask;
    }
}
