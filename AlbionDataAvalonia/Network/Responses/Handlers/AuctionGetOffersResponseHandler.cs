using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionGetOffersResponseHandler : ResponsePacketHandler<AuctionGetOffersResponse>
{
    private readonly PlayerState playerState;
    private readonly TradeService tradeService;
    public AuctionGetOffersResponseHandler(PlayerState playerState, TradeService tradeService) : base((int)OperationCodes.AuctionGetOffers)
    {
        this.playerState = playerState;
        this.tradeService = tradeService;
    }

    protected override async Task OnActionAsync(AuctionGetOffersResponse value)
    {
        playerState.HasEncryptedData = false;

        if (!playerState.CheckOkToUpload()) return;

        tradeService.AddMarketOrdersToCache(value.marketOrders);

        await Task.CompletedTask;
    }
}
