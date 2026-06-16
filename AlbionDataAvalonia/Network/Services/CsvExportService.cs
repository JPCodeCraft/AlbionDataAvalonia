using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Loot.Models;
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
    private readonly LocalizationService _localizationService;

    public CsvExportService(LocalizationService localizationService)
    {
        _localizationService = localizationService;
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
            "Item", "Location", "Full Location", "Amount", "Unit Silver", "Total Silver"
        }));

        using var db = new LocalContext();
        var totalCount = await db.Trades.Where(x => !x.Deleted).CountAsync(cancellationToken);

        if (totalCount == 0)
        {
            progress?.Report(100);
            return;
        }

        const int batchSize = 500;
        int processed = 0;

        var batches = (int)Math.Ceiling(totalCount / (double)batchSize);

        for (int i = 0; i < batches; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var trades = await db.Trades
                .Where(x => !x.Deleted)
                .OrderByDescending(x => x.DateTime)
                .Skip(i * batchSize)
                .Take(batchSize)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var trade in trades)
            {
                var server = AlbionServers.Get(trade.AlbionServerId ?? 0);
                var location = AlbionLocations.ResolveStoredLocation(trade.RawLocationId, trade.LocationId);
                var marketName = location?.MarketLocation?.FriendlyName ?? location?.FriendlyName ?? "";
                var fullName = location?.FriendlyName ?? "";
                var itemName = _localizationService.GetUsName(trade.ItemId);

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
                    trade.UnitSilver.ToString("F2", culture),
                    trade.TotalSilver.ToString("F0", culture)
                });

                await writer.WriteLineAsync(line);
                processed++;
            }

            var percent = (int)((double)processed / totalCount * 100);
            progress?.Report(percent);
        }

        await writer.FlushAsync(cancellationToken);
        progress?.Report(100);

        Log.Information("Exported {Count} trades to CSV", processed);
    }

    public async Task ExportMailsToCsvAsync(Stream stream, CsvExportOptions? options = null, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= CsvExportOptions.FromCurrentCulture();
        var culture = options.CreateFormattingCulture();
        var delimiter = options.Delimiter;

        using var writer = new StreamWriter(stream, Encoding.UTF8);

        // Write header - matches the columns shown in MailsView.axaml DataGrid
        await writer.WriteLineAsync(string.Join(delimiter, new[]
        {
            "Server", "Player", "Received", "Type", "Item", "Location", "Full Location",
            "Amount", "Order Amount", "Unit Silver", "Total Silver"
        }));

        using var db = new LocalContext();
        var totalCount = await db.AlbionMails.Where(x => !x.Deleted).CountAsync(cancellationToken);

        if (totalCount == 0)
        {
            progress?.Report(100);
            return;
        }

        const int batchSize = 500;
        int processed = 0;

        var batches = (int)Math.Ceiling(totalCount / (double)batchSize);

        for (int i = 0; i < batches; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mails = await db.AlbionMails
                .Where(x => !x.Deleted)
                .OrderByDescending(x => x.Received)
                .Skip(i * batchSize)
                .Take(batchSize)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var mail in mails)
            {
                var server = AlbionServers.GetAll().SingleOrDefault(x => x.Id == mail.AlbionServerId);
                var location = AlbionLocations.ResolveStoredLocation(mail.RawLocationId, mail.LocationId);
                var marketName = location?.MarketLocation?.FriendlyName ?? location?.FriendlyName ?? "";
                var fullName = location?.FriendlyName ?? "";
                var itemName = _localizationService.GetUsName(mail.ItemId);

                var line = string.Join(delimiter, new[]
                {
                    Escape(server?.Name ?? "", delimiter),
                    Escape(mail.PlayerName, delimiter),
                    Escape(mail.Received.ToString(culture), delimiter),
                    Escape(mail.AuctionTypeFormatted, delimiter),
                    Escape(itemName, delimiter),
                    Escape(marketName, delimiter),
                    Escape(fullName, delimiter),
                    mail.PartialAmount.ToString("F0", culture),
                    mail.TotalAmount.ToString("F0", culture),
                    mail.UnitSilver.ToString("F2", culture),
                    mail.TotalSilver.ToString("F0", culture)
                });

                await writer.WriteLineAsync(line);
                processed++;
            }

            var percent = (int)((double)processed / totalCount * 100);
            progress?.Report(percent);
        }

        await writer.FlushAsync(cancellationToken);
        progress?.Report(100);

        Log.Information("Exported {Count} mails to CSV", processed);
    }

    public async Task ExportLootToCsvAsync(
        Stream stream,
        IReadOnlyList<LootRecord> records,
        CsvExportOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= CsvExportOptions.FromCurrentCulture();
        var culture = options.CreateFormattingCulture();
        var delimiter = options.Delimiter;

        using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteLineAsync(string.Join(delimiter, new[]
        {
            "Picked Up UTC", "Player", "Party Member At Pickup", "Source Type", "Source",
            "Location", "Item Unique Name", "Item", "Quality",
            "Amount", "Unit EMV", "Total EMV"
        }));

        if (records.Count == 0)
        {
            await writer.FlushAsync(cancellationToken);
            progress?.Report(100);
            return;
        }

        for (var i = 0; i < records.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = records[i];
            var line = string.Join(delimiter, new[]
            {
                Escape(record.PickedUpAtUtc.ToString("O", CultureInfo.InvariantCulture), delimiter),
                Escape(record.PlayerName, delimiter),
                record.WasPartyMemberAtPickup ? "true" : "false",
                Escape(record.SourceKind.ToString(), delimiter),
                Escape(record.SourceName, delimiter),
                Escape(record.LocationName, delimiter),
                Escape(record.ItemUniqueName, delimiter),
                Escape(record.ItemName, delimiter),
                record.Quality?.ToString(culture) ?? string.Empty,
                record.Amount.ToString(culture),
                record.EstimatedMarketValue?.ToString(culture) ?? string.Empty,
                record.TotalEstimatedMarketValue?.ToString(culture) ?? string.Empty
            });

            await writer.WriteLineAsync(line);
            progress?.Report((int)((i + 1d) / records.Count * 100));
        }

        await writer.FlushAsync(cancellationToken);
        progress?.Report(100);
        Log.Information("Exported {Count} filtered loot pickups to CSV", records.Count);
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
