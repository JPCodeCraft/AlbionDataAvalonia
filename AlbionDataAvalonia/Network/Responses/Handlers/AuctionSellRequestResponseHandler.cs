using Albion.Network;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionSellRequestResponseHandler : ResponsePacketHandler<AuctionSellRequestResponse>
{
    private readonly TradeService tradeService;

    public AuctionSellRequestResponseHandler(TradeService tradeService) : base((int)OperationCodes.AuctionSellRequest)
    {
        this.tradeService = tradeService;
    }

    protected override async Task OnActionAsync(AuctionSellRequestResponse value)
    {
        if (value.ReturnCode == 0)
        {
            await tradeService.ConfirmTrade();
        }
        else
        {
            tradeService.ClearUnconfirmedTrade();
        }
    }
}
