using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Localization.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

public class TradeService
{
    private readonly PlayerState _playerState;
    private readonly SettingsManager _settingsManager;
    private readonly LocalizationService _localizationService;
    private readonly MailService _mailService;
    private List<Trade> Trades { get; set; } = new();

    private readonly Queue<MarketOrder> marketOrdersCache = new Queue<MarketOrder>();
    private Trade? unconfirmedTrade = null;

    public Action<Trade>? OnTradeAdded;

    public TradeService(PlayerState playerState, SettingsManager settingsManager, LocalizationService localizationService, MailService mailService)
    {
        _playerState = playerState;
        _settingsManager = settingsManager;
        _localizationService = localizationService;
        _mailService = mailService;

        _mailService.OnMailDataAdded += async (mail) => await HandleOnMailDataAdded(mail);
    }

    public async Task<List<Trade>> GetTrades(int countPerPage, int pageNumber = 0, int? albionServerId = null, bool showDeleted = false, int? locationId = null, TradeType? tradeType = null, TradeOperation? tradeOperation = null)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var query = db.Trades.AsQueryable();

                if (albionServerId.HasValue)
                {
                    query = query.Where(x => x.AlbionServerId == albionServerId);
                }

                if (locationId.HasValue)
                {
                    query = query.Where(x => x.LocationId == locationId);
                }

                if (tradeOperation != null)
                {
                    query = query.Where(x => x.Operation == tradeOperation);
                }

                if (tradeType != null)
                {
                    query = query.Where(x => x.Type == tradeType);
                }

                if (!showDeleted)
                {
                    query = query.Where(x => !x.Deleted);
                }

                var result = await query.OrderByDescending(x => x.DateTime).AsNoTracking().Skip(countPerPage * pageNumber).Take(countPerPage).ToListAsync();

                foreach (var trade in result)
                {
                    trade.Server = AlbionServers.Get(trade.AlbionServerId ?? 0);
                    trade.Location = AlbionLocations.Get(trade.LocationId);
                    trade.ItemName = _localizationService.GetUsName(trade.ItemId);
                }

                Log.Debug("Loaded {Count} trades", result.Count);

                return result;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return new List<Trade>();
        }
    }

    private async Task AddTradeToDb(Trade trade)
    {
        try
        {
            using (var db = new LocalContext())
            {
                await db.Trades.AddAsync(trade);
                await db.SaveChangesAsync();

                OnTradeAdded?.Invoke(trade);

                Log.Debug("Added trade {TradeId}", trade.Id);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    public async Task DeleteTrade(Guid id)
    {
        try
        {
            using (var db = new LocalContext())
            {
                var trade = await db.Trades.Where(x => x.Id == id).SingleOrDefaultAsync();

                if (trade == null) return;

                trade.Deleted = true;

                await db.SaveChangesAsync();

                Log.Debug("Deleted trade {TradeId}", id);
            }
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
        }
    }

    public void AddMarketOrdersToCache(List<MarketOrder> orders)
    {
        foreach (var order in orders)
        {
            if (marketOrdersCache.Any(o => o.Id == order.Id))
            {
                continue;
            }
            if (marketOrdersCache.Count >= 500)
            {
                marketOrdersCache.Dequeue();
            }
            marketOrdersCache.Enqueue(order);
        }
        Log.Debug("Added {Count} market orders to cache", orders.Count);
    }

    public MarketOrder? GetMarketOrderFromCache(ulong id)
    {
        var result = marketOrdersCache.FirstOrDefault(o => o.Id == id);
        if (result != null)
        {
            Log.Verbose("Got market order from cache: {OrderId}", id);
        }
        else
        {
            Log.Verbose("Market order not found in cache: {OrderId}", id);
        }
        return result;
    }

    public void SetUnconfirmedTrade(Trade trade)
    {
        unconfirmedTrade = trade;
        Log.Debug("Set unconfirmed trade: {TradeId}", trade.Id);
    }

    public void ClearUnconfirmedTrade()
    {
        unconfirmedTrade = null;
        Log.Debug("Cleared unconfirmed trade");
    }

    public async Task ConfirmTrade()
    {
        if (unconfirmedTrade == null)
        {
            Log.Warning("No unconfirmed trade to confirm");
            return;
        }
        await AddTradeToDb(unconfirmedTrade);
        unconfirmedTrade = null;
        Log.Debug("Confirmed trade");
    }

    private async Task HandleOnMailDataAdded(AlbionMail mail)
    {
        var trade = new Trade(mail, _settingsManager.UserSettings.SalesTax);
        await AddTradeToDb(trade);
        Log.Debug("Added trade from mail: {TradeId}", trade.Id);
    }
}
