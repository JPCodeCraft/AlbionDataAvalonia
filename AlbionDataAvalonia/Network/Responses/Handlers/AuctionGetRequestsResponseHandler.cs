using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetRequestsResponseHandler : ResponsePacketHandler<AuctionGetRequestsResponse>
{
    private readonly Uploader uploader;
    private readonly PlayerState playerState;
    private readonly TradeService tradeService;
    public AuctionGetRequestsResponseHandler(Uploader uploader, PlayerState playerState, TradeService tradeService) : base((int)OperationCodes.AuctionGetRequests)
    {
        this.uploader = uploader;
        this.playerState = playerState;
        this.tradeService = tradeService;
    }

    protected override async Task OnActionAsync(AuctionGetRequestsResponse value)
    {
        playerState.HasEncryptedData = false;

        if (!playerState.CheckOkToUpload()) return;

        tradeService.AddMarketOrdersToCache(value.marketOrders);

        MarketUpload marketUpload = new MarketUpload();

        value.marketOrders.ForEach(x =>
        {
            if (x.LocationId == null) x.LocationId = playerState.Location.Id;
        });

        marketUpload.Orders.AddRange(value.marketOrders);

        if (marketUpload.Orders.Count > 0)
        {
            uploader.EnqueueUpload(new Upload(marketUpload, null, null));
        }
        await Task.CompletedTask;
    }
}
