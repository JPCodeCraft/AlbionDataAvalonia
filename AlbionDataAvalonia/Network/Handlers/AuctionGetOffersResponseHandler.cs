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
    private readonly TradeService tradeService;
    public AuctionGetOffersResponseHandler(Uploader uploader, PlayerState playerState, TradeService tradeService) : base((int)OperationCodes.AuctionGetOffers)
    {
        this.uploader = uploader;
        this.playerState = playerState;
        this.tradeService = tradeService;
    }

    protected override async Task OnActionAsync(AuctionGetOffersResponse value)
    {
        playerState.HasEncryptedData = false;

        if (!playerState.CheckOkToUpload()) return;

        tradeService.AddMarketOrdersToCache(value.marketOrders);

        MarketUpload marketUpload = new MarketUpload();

        value.marketOrders.ForEach(x =>
        {
            if (x.LocationId == null) x.LocationId = playerState.Location.IdInt ?? -2;
        }
        );

        marketUpload.Orders.AddRange(value.marketOrders);

        if (marketUpload.Orders.Count > 0)
        {
            uploader.EnqueueUpload(new Upload(marketUpload, null, null));
        }
        await Task.CompletedTask;
    }
}
