using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionSellSpecificItemRequestResponseHandler : ResponsePacketHandler<AuctionSellSpecificItemRequestResponse>
{
    private readonly PlayerState playerState;
    private readonly TradeService tradeService;
    public AuctionSellSpecificItemRequestResponseHandler(PlayerState playerState, TradeService tradeService) : base((int)OperationCodes.AuctionSellSpecificItemRequest)
    {
        this.playerState = playerState;
        this.tradeService = tradeService;
    }

    protected override async Task OnActionAsync(AuctionSellSpecificItemRequestResponse value)
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
