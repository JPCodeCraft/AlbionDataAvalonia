using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
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

    public async Task ExportTradesToCsvAsync(Stream stream, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var culture = CultureInfo.CurrentCulture;
        var delimiter = culture.TextInfo.ListSeparator;

        using var writer = new StreamWriter(stream, Encoding.UTF8);

        // Write header - matches the columns shown in TradesView.axaml DataGrid
        await writer.WriteLineAsync(string.Join(delimiter, new[]
        {
            "Server", "Player", "Received", "Type", "Operation", "Quality",
            "Item", "Location", "Amount", "Unit Silver", "Total Silver"
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
                var location = AlbionLocations.GetByIntId(trade.LocationId);
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
                    Escape(location?.FriendlyName ?? "", delimiter),
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

    public async Task ExportMailsToCsvAsync(Stream stream, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var culture = CultureInfo.CurrentCulture;
        var delimiter = culture.TextInfo.ListSeparator;

        using var writer = new StreamWriter(stream, Encoding.UTF8);

        // Write header - matches the columns shown in MailsView.axaml DataGrid
        await writer.WriteLineAsync(string.Join(delimiter, new[]
        {
            "Server", "Player", "Received", "Type", "Item", "Location",
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
                var location = AlbionLocations.GetByIntId(mail.LocationId);
                var itemName = _localizationService.GetUsName(mail.ItemId);

                var line = string.Join(delimiter, new[]
                {
                    Escape(server?.Name ?? "", delimiter),
                    Escape(mail.PlayerName, delimiter),
                    Escape(mail.Received.ToString(culture), delimiter),
                    Escape(mail.AuctionTypeFormatted, delimiter),
                    Escape(itemName, delimiter),
                    Escape(location?.FriendlyName ?? "", delimiter),
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
