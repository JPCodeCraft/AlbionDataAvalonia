using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Ui.Services;
using AlbionDataAvalonia.Views.Dialogs;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

// The Trades toolbar actions: each asks what it needs through dialogs, then runs on the selected rows.
public partial class TradesViewModel
{
    private readonly IDialogService dialogs = null!;
    private readonly IClipboardService clipboard = null!;
    private readonly IFileDialogService files = null!;

    [RelayCommand]
    private async Task ExportCsv()
    {
        try
        {
            if (await files.PickCsvExportAsync("Export trades to CSV", $"trades_{DateTime.Now:yyyyMMdd_HHmmss}.csv") is not { } target) return;
            await using var stream = await target.File.OpenWriteAsync();
            await ExportToCsvAsync(stream, target.Options);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not export trades to CSV.");
            SetPortfolioImportStatus("Export failed. The file may be incomplete.");
        }
    }

    [RelayCommand]
    private async Task AddToPortfolio()
    {
        try
        {
            var selectedRows = SelectedTrades.ToList();
            if (selectedRows.Count == 0 || !CanAddSelectedTradesToPortfolio) return;

            if (!await EnsurePortfolioSignedInAsync())
            {
                await dialogs.AlertAsync("Sign in required", "Sign in to AFM before adding trades to Portfolio.");
                return;
            }
            if (!await RefreshPortfolioUploadedTradeIdsAsync(showStatusOnFailure: true)) return;

            var alreadyUploaded = selectedRows.Count(row => row.UploadedToPortfolio);
            var allowReupload = alreadyUploaded > 0 && await ConfirmReuploadAsync(alreadyUploaded, selectedRows.Count);
            if (alreadyUploaded > 0 && !allowReupload) return;

            if (await AskUnknownQualitiesAsync(selectedRows) is not { } qualityOverrides) return;

            var sellPremiumStatuses = selectedRows
                .Where(row => row.Source.Operation == TradeOperation.Sell)
                .Select(row => _portfolio.ResolvePremium(row.Source))
                .ToList();
            if (await dialogs.ShowAsync(new PortfolioUploadDialog(selectedRows.Count, sellPremiumStatuses)) is not { } confirmation) return;

            await AddTradesToPortfolioAsync(selectedRows, qualityOverrides, allowReupload, confirmation.PremiumOverride);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not add the selected trades to Portfolio.");
        }
    }

    [RelayCommand]
    private async Task SetQuality()
    {
        try
        {
            var selectedRows = SelectedTrades.ToList();
            if (selectedRows.Count == 0 || !CanSetSelectedTradeQuality) return;
            if (await dialogs.ShowAsync(new TradeQualityDialog(selectedRows.Count(row => !row.UploadedToPortfolio))) is not { } quality) return;
            await SetSelectedTradesQualityAsync(selectedRows, quality);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not set the quality of the selected trades.");
        }
    }

    [RelayCommand]
    private async Task DeleteSelected()
    {
        try
        {
            var selectedRows = SelectedTrades.ToList();
            if (selectedRows.Count == 0 || !await dialogs.ConfirmDeleteAsync("trade", "trades", selectedRows.Count)) return;
            await DeleteSelectedTradesAsync(selectedRows);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not delete the selected trades.");
        }
    }

    /// <summary>Copies a footer total as a plain number, so it pastes into a sheet.</summary>
    [RelayCommand]
    private Task CopyValue(object? value) => value is IFormattable number
        ? clipboard.SetTextAsync(number.ToString(value is decimal ? "F2" : "F0", CultureInfo.InvariantCulture))
        : Task.CompletedTask;

    private Task<bool> ConfirmReuploadAsync(int uploaded, int selected)
    {
        var one = uploaded == 1;
        return dialogs.ConfirmAsync(
            $"Upload {(one ? "trade" : "trades")} again?",
            $"{uploaded:N0} of {Ui.UiFormat.Count(selected, "selected trade")} {(one ? "is" : "are")} already marked as uploaded to Portfolio. " +
            $"If you continue, {(one ? "it" : "they")} will be added again as new Portfolio {(one ? "transaction" : "transactions")}.",
            "Upload again",
            destructive: false);
    }

    // Trades with Unknown quality need a Portfolio quality per item and server; none needed returns an empty map.
    private async Task<IReadOnlyDictionary<PortfolioTradeQualityKey, int>?> AskUnknownQualitiesAsync(IReadOnlyList<TradeRowViewModel> rows)
    {
        var groups = rows
            .Where(row => row.QualityLevel == 0)
            .GroupBy(PortfolioImportCoordinator.QualityKey)
            .Select(group => new PortfolioQualitySelectionGroup(group.Key, group.First().ItemName, group.First().Server?.Name ?? "Unknown server", group.Count()))
            .OrderBy(group => group.ItemName)
            .ThenBy(group => group.ServerName)
            .ToList();
        return groups.Count == 0
            ? new Dictionary<PortfolioTradeQualityKey, int>()
            : await dialogs.ShowAsync(new PortfolioQualityDialog(groups));
    }
}
