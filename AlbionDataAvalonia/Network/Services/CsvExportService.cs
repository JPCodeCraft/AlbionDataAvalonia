using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services;

public class CsvExportService
{
    private readonly ItemsIdsService itemsIdsService;

    public CsvExportService(ItemsIdsService itemsIdsService)
    {
        this.itemsIdsService = itemsIdsService;
    }

    public async Task ExportTradesToCsvAsync(Stream stream, CsvExportOptions? options = null, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= CsvExportOptions.FromCurrentCulture();
        var culture = options.CreateFormattingCulture();
        var delimiter = options.Delimiter;

        using var writer = new StreamWriter(stream, Encoding.UTF8);

        // Write header - matches the columns shown in TradesView.axaml DataGrid
        await writer.WriteLineAsync(string.Join(delimiter, new[]
        {
            "Server", "Player", "Received", "Type", "Operation", "Quality",
            "Item", "Location", "Full Location", "Amount", "Unit Silver", "Total Silver", "Order Amount"
        }));

        using var db = new LocalContext();
        // An order that expired with nothing filled has no trade, but its setup fee was paid, so its mail
        // becomes a row with amount 0 and the full order amount. The mail doesn't say a sell order's price.
        var unfilled = (await db.AlbionMails
                .Where(x => !x.Deleted && x.IsSet && x.PartialAmount == 0 && x.TotalAmount > 0 && x.AuctionType != AuctionType.unknown)
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Select(mail => new Trade(mail))
            .OrderByDescending(x => x.DateTime)
            .ToList();
        var tradeCount = await db.Trades.Where(x => !x.Deleted && x.Amount > 0).CountAsync(cancellationToken);
        var totalCount = tradeCount + unfilled.Count;

        if (totalCount == 0)
        {
            progress?.Report(100);
            return;
        }

        const int batchSize = 500;
        int processed = 0;
        int nextUnfilled = 0;

        async Task WriteAsync(Trade trade)
        {
            var server = AlbionServers.Get(trade.AlbionServerId ?? 0);
            var location = AlbionLocations.ResolveStoredLocation(trade.RawLocationId, trade.LocationId);
            var marketName = location?.MarketLocation?.FriendlyName ?? location?.FriendlyName ?? "";
            var fullName = location?.FriendlyName ?? "";
            var itemName = itemsIdsService.GetUsNameByUniqueName(trade.ItemId);

            var line = string.Join(delimiter, new[]
            {
                Escape(server?.Name ?? "", delimiter),
                Escape(trade.PlayerName, delimiter),
                Escape(trade.DateTime.ToString(culture), delimiter),
                Escape(trade.TradeTypeFormatted, delimiter),
                Escape(trade.TradeOperationFormatted, delimiter),
                Escape(trade.QualityLevelFormatted, delimiter),
                Escape(itemName, delimiter),
                Escape(marketName, delimiter),
                Escape(fullName, delimiter),
                trade.Amount.ToString(culture),
                trade.Amount == 0 && trade.UnitSilver == 0 ? "" : trade.UnitSilver.ToString("F2", culture),
                trade.TotalSilver.ToString("F0", culture),
                trade.OrderAmount?.ToString(culture) ?? ""
            });

            await writer.WriteLineAsync(line);
            processed++;
        }

        var batches = (int)Math.Ceiling(tradeCount / (double)batchSize);

        for (int i = 0; i < batches; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trades = await db.Trades
                .Where(x => !x.Deleted && x.Amount > 0)
                .OrderByDescending(x => x.DateTime)
                .Skip(i * batchSize)
                .Take(batchSize)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var trade in trades)
            {
                // Keep the newest-first order across trades and unfilled orders.
                while (nextUnfilled < unfilled.Count && unfilled[nextUnfilled].DateTime >= trade.DateTime)
                    await WriteAsync(unfilled[nextUnfilled++]);
                await WriteAsync(trade);
            }

            var percent = (int)((double)processed / totalCount * 100);
            progress?.Report(percent);
        }

        while (nextUnfilled < unfilled.Count)
            await WriteAsync(unfilled[nextUnfilled++]);

        await writer.FlushAsync(cancellationToken);
        progress?.Report(100);

        Log.Information("Exported {Count} trades and {Unfilled} unfilled orders to CSV", processed - unfilled.Count, unfilled.Count);
    }

    public async Task ExportLootToCsvAsync(
        Stream stream,
        IReadOnlyList<ActivityEvent> records,
        CsvExportOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        records = records.Where(record => record.Kind == ActivityEventKind.Loot && !record.IsRetracted).ToArray();
        options ??= CsvExportOptions.FromCurrentCulture();
        var culture = options.CreateFormattingCulture();
        var delimiter = options.Delimiter;

        if (stream.CanSeek)
        {
            stream.SetLength(0);
            stream.Position = 0;
        }

        using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteLineAsync(string.Join(delimiter, new[]
        {
            "Picked Up UTC", "Server", "Player", "Player Alliance", "Player Guild",
            "Party Member At Pickup", "Source Type", "Source", "Source Alliance", "Source Guild",
            "Location", "Location ID", "Item Unique Name", "Item", "Quality",
            "Amount", "Unit EMV", "Total EMV"
        }));

        for (var index = 0; index < records.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = records[index];
            var serverName = AlbionServers.TryParse(record.Server, out var server) ? server?.Name ?? string.Empty : string.Empty;
            var line = string.Join(delimiter, new[]
            {
                Escape(FormatUtc(record.CapturedAtUtc), delimiter),
                Escape(serverName, delimiter),
                Escape(record.SourceName, delimiter),
                Escape(record.SourceAlliance, delimiter),
                Escape(record.SourceGuild, delimiter),
                record.SourceRelationship switch
                {
                    ActorRelationship.Self or ActorRelationship.Party => "true",
                    ActorRelationship.Other => "false",
                    _ => "Unknown"
                },
                Escape(record.LootSourceKind, delimiter),
                Escape(record.LootSourceName, delimiter),
                Escape(record.TargetAlliance, delimiter),
                Escape(record.TargetGuild, delimiter),
                Escape(record.LocationName, delimiter),
                Escape(record.RawMapId, delimiter),
                Escape(record.ItemUniqueName ?? string.Empty, delimiter),
                Escape(record.ItemName, delimiter),
                Escape(ItemQuality.Format(record.Quality), delimiter),
                record.Quantity > 0 ? record.Quantity.ToString(culture) : string.Empty,
                record.EstimatedUnitValue?.ToString(culture) ?? string.Empty,
                record.Quantity > 0 ? ActivityProjection.GetLootValue(record)?.ToString(culture) ?? string.Empty : string.Empty
            });

            await writer.WriteLineAsync(line);
            progress?.Report((int)((index + 1d) / records.Count * 100));
        }

        await writer.FlushAsync(cancellationToken);
        progress?.Report(100);
        Log.Information("Exported {Count} filtered loot pickups to CSV", records.Count);
    }

    public async Task ExportLootToViewerAsync(
        Stream stream,
        IReadOnlyList<ActivityEvent> records,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        records = records.Where(record => record.Kind == ActivityEventKind.Loot && !record.IsRetracted && record.Quantity > 0).ToArray();

        if (stream.CanSeek)
        {
            stream.SetLength(0);
            stream.Position = 0;
        }

        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join(';', new[]
        {
            "timestamp_utc",
            "looted_by__alliance",
            "looted_by__guild",
            "looted_by__name",
            "item_id",
            "item_name",
            "quantity",
            "looted_from__alliance",
            "looted_from__guild",
            "looted_from__name"
        }));

        for (var index = 0; index < records.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = records[index];
            var line = string.Join(';', new[]
            {
                FormatUtc(record.CapturedAtUtc),
                SanitizeViewerValue(record.SourceAlliance),
                SanitizeViewerValue(record.SourceGuild),
                SanitizeViewerValue(record.SourceName),
                SanitizeViewerValue(record.ItemUniqueName),
                SanitizeViewerValue(record.ItemName),
                record.Quantity.ToString(CultureInfo.InvariantCulture),
                SanitizeViewerValue(record.TargetAlliance),
                SanitizeViewerValue(record.TargetGuild),
                SanitizeViewerValue(record.LootSourceName)
            });

            await writer.WriteLineAsync(line);
            progress?.Report((int)((index + 1d) / records.Count * 100));
        }

        await writer.FlushAsync(cancellationToken);
        progress?.Report(100);
        Log.Information("Exported {Count} filtered loot pickups for AO Loot Logger Viewer", records.Count);
    }

    private static string FormatUtc(DateTime value)
    {
        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return utcValue.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string SanitizeViewerValue(string? value)
    {
        return (value ?? string.Empty)
            .Replace(';', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static string Escape(string value, string delimiter)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If value contains delimiter, quotes, or newlines, wrap in quotes and escape internal quotes
        if (value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
