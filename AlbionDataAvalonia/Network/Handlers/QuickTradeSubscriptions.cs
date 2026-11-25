using Albion.Network;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Requests;
using AlbionDataAvalonia.Network.Responses;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Shared;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Handlers;

// Quick Sell in a city market and Quick Purchase Loadouts finish several instant trades at once.
// The windows' listings hold the prices; the replies say which trades went through.
internal static class QuickTradeSubscriptions
{
    public static void Register(ReceiverBuilder builder, PlayerState playerState, TradeService tradeService)
    {
        var tracker = new QuickTradeTracker();

        builder.SubscribeResponse<QuickSellAuctionQueryActionResponse>((int)OperationCodes.QuickSellAuctionQueryAction, value =>
        {
            if (playerState.CheckOkToUpload()) tracker.QuickSellListed(value, playerState.Location.Id);
            return Task.CompletedTask;
        });
        builder.SubscribeRequest<QuickSellAuctionSellActionRequest>((int)OperationCodes.QuickSellAuctionSellAction, value =>
        {
            tracker.QuickSellRequested(value);
            return Task.CompletedTask;
        });
        builder.SubscribeResponse<QuickSellAuctionSellActionResponse>((int)OperationCodes.QuickSellAuctionSellAction,
            value => Save(tracker.QuickSellCompleted(value), playerState, tradeService));

        builder.SubscribeResponse<AuctionGetLoadoutOffersResponse>((int)OperationCodes.AuctionGetLoadoutOffers, value =>
        {
            if (playerState.CheckOkToUpload()) tracker.LoadoutOffersListed(value, playerState.Location.Id);
            return Task.CompletedTask;
        });
        builder.SubscribeResponse<AuctionBuyLoadoutOfferResponse>((int)OperationCodes.AuctionBuyLoadoutOffer,
            value => Save(tracker.LoadoutPurchaseCompleted(value), playerState, tradeService));
    }

    private static async Task Save(IReadOnlyList<QuickTrade> completed, PlayerState playerState, TradeService tradeService)
    {
        if (completed.Count == 0) return;

        var trades = new List<Trade>();
        foreach (var item in completed)
        {
            try
            {
                trades.Add(new Trade(item.Order, item.Amount, playerState.AlbionServer?.Id, playerState.PlayerName));
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not log quick trade for order {OrderId}", item.Order.Id);
            }
        }

        await tradeService.AddTradesAsync(trades);
    }
}
