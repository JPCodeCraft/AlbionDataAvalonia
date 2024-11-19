using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

public class AuctionSellSpecificItemRequestRequestHandler : RequestPacketHandler<AuctionSellSpecificItemRequestRequest>
{
    private readonly PlayerState playerState;
    private readonly TradeService tradeService;
    private readonly SettingsManager settingsManager;

    public AuctionSellSpecificItemRequestRequestHandler(PlayerState playerState, TradeService tradeService, SettingsManager settingsManager) : base((int)OperationCodes.AuctionSellSpecificItemRequest)
    {
        this.playerState = playerState;
        this.tradeService = tradeService;
        this.settingsManager = settingsManager;
    }

    protected override async Task OnActionAsync(AuctionSellSpecificItemRequestRequest value)
    {
        if (!playerState.CheckOkToUpload()) return;

        var order = tradeService.GetMarketOrderFromCache(value.orderId);

        if (order == null)
        {
            Log.Error("Order not found: {OrderId}", value.orderId);
            return;
        }

        var trade = new Trade(order, value.amount, playerState.AlbionServer?.Id, playerState.PlayerName, settingsManager.UserSettings.SalesTax);

        tradeService.SetUnconfirmedTrade(trade);

        await Task.CompletedTask;
    }
}
