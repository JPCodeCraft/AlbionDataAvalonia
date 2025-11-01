using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionBuyOfferResponseHandler : ResponsePacketHandler<AuctionBuyOfferResponse>
{
    private readonly PlayerState playerState;
    private readonly TradeService tradeService;
    public AuctionBuyOfferResponseHandler(PlayerState playerState, TradeService tradeService) : base((int)OperationCodes.AuctionBuyOffer)
    {
        this.playerState = playerState;
        this.tradeService = tradeService;
    }

    protected override async Task OnActionAsync(AuctionBuyOfferResponse value)
    {
        if (value.success)
        {
            await tradeService.ConfirmTrade();
        }
        else
        {
            tradeService.ClearUnconfirmedTrade();
        }

        await Task.CompletedTask;
    }
}
