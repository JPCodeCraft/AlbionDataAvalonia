using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.State;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

public class TradeService
{
    private readonly PlayerState _playerState;
    private readonly ItemsIdsService _itemsIdsService;
    private readonly MailService _mailService;
    private List<Trade> Trades { get; set; } = new();

    private readonly Queue<MarketOrder> marketOrdersCache = new Queue<MarketOrder>();
    private Trade? unconfirmedTrade = null;

    public Action<Trade>? OnTradeAdded;

    public TradeService(PlayerState playerState, ItemsIdsService itemsIdsService, MailService mailService)
    {
        _playerState = playerState;
        _itemsIdsService = itemsIdsService;
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

                SetTradeProperties(result);

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

    public async Task<List<int>> GetDistinctLocationIds(int? albionServerId = null)
    {
        try
        {
            using var db = new LocalContext();
            var query = db.Trades.Where(x => !x.Deleted);
            if (albionServerId.HasValue)
            {
                query = query.Where(x => x.AlbionServerId == albionServerId);
            }

            return await query.Select(x => x.LocationId).Distinct().ToListAsync();
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return new List<int>();
        }
    }

    public async Task<CleanupPreview> GetCleanupPreviewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new LocalContext();
            var totalCount = await db.Trades.CountAsync(x => !x.Deleted, cancellationToken);
            var options = new List<CleanupCountOption>();

            foreach (var threshold in CleanupThresholds.Create(DateTime.UtcNow))
            {
                var count = await db.Trades.CountAsync(
                    x => !x.Deleted && x.DateTime < threshold.CutoffUtc,
                    cancellationToken);
                options.Add(new CleanupCountOption(threshold.Label, threshold.CutoffUtc, count));
            }

            return new CleanupPreview(totalCount, options);
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return new CleanupPreview(0, []);
        }
    }

    public async Task<int> CleanupTradesOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new LocalContext();
            var trades = await db.Trades
                .Where(x => !x.Deleted && x.DateTime < cutoffUtc)
                .ToListAsync(cancellationToken);

            foreach (var trade in trades)
            {
                trade.Deleted = true;
            }

            await db.SaveChangesAsync(cancellationToken);

            Log.Information("Cleaned up {Count} trades older than {CutoffUtc:O}", trades.Count, cutoffUtc);
            return trades.Count;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return 0;
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

                SetTradeProperties(trade);

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

    public async Task<int> DeleteTradesAsync(IEnumerable<Guid> tradeIds, CancellationToken cancellationToken = default)
    {
        var ids = tradeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        try
        {
            using var db = new LocalContext();
            var trades = await db.Trades
                .Where(x => !x.Deleted && ids.Contains(x.Id))
                .ToListAsync(cancellationToken);

            foreach (var trade in trades)
            {
                trade.Deleted = true;
            }

            await db.SaveChangesAsync(cancellationToken);

            Log.Information("Deleted {Count} selected trades", trades.Count);
            return trades.Count;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return 0;
        }
    }

    public async Task<int> UpdateTradeQualityLevelsAsync(IEnumerable<Guid> tradeIds, byte qualityLevel)
    {
        if (qualityLevel > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(qualityLevel), "Quality level must be between 0 and 5.");
        }

        var ids = tradeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        try
        {
            using var db = new LocalContext();
            var trades = await db.Trades
                .Where(x => ids.Contains(x.Id))
                .ToListAsync();

            foreach (var trade in trades)
            {
                trade.QualityLevel = qualityLevel;
            }

            await db.SaveChangesAsync();

            Log.Debug("Updated {Count} trade quality levels to {QualityLevel}", trades.Count, qualityLevel);
            return trades.Count;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return 0;
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
        var trade = new Trade(mail);
        await AddTradeToDb(trade);
        Log.Debug("Added trade from mail: {TradeId}", trade.Id);
    }

    private void SetTradeProperties(List<Trade> trades)
    {
        foreach (var trade in trades)
        {
            SetTradeProperties(trade);
        }
    }

    private void SetTradeProperties(Trade trade)
    {
        trade.Server = AlbionServers.Get(trade.AlbionServerId ?? 0);
        trade.Location = AlbionLocations.ResolveStoredLocation(trade.RawLocationId, trade.LocationId);
        trade.ItemName = _itemsIdsService.GetUsNameByUniqueName(trade.ItemId);
    }
}
