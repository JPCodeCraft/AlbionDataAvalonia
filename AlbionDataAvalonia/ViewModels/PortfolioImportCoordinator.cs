using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.Ui;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>The outcome of loading which trades are already in Portfolio.</summary>
/// <param name="IsCurrent">False when a newer load or an upload replaced it, so the caller should ignore it.</param>
public sealed record PortfolioRefresh(bool IsCurrent, bool Success, string? ErrorMessage);

/// <summary>
/// Adds trades to the AFM Portfolio for the Trades tab: it knows which trades are already there, saves the quality
/// and Premium chosen for an upload, builds the import requests and words the result. It holds no UI state.
/// </summary>
public sealed class PortfolioImportCoordinator(PortfolioUploadService uploads, TradeService trades, PlayerState playerState)
{
    private readonly LatestLoad refreshes = new();
    private HashSet<Guid> uploadedTradeIds = new();

    public bool IsUploaded(Guid tradeId) => uploadedTradeIds.Contains(tradeId);

    public Task<bool> CanUploadAsync(CancellationToken cancellationToken = default) => uploads.CanUploadAsync(cancellationToken);

    /// <summary>Loads the IDs of trades already in Portfolio.</summary>
    public async Task<PortfolioRefresh> RefreshUploadedAsync(CancellationToken cancellationToken = default)
    {
        var ticket = refreshes.Begin();
        PortfolioUploadedTradeIdsResult result;
        try
        {
            result = await uploads.GetUploadedTradeIdsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(refreshes.IsCurrent(ticket), false, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh uploaded Portfolio trade ids");
            result = PortfolioUploadedTradeIdsResult.Failed("Failed to load portfolio positions.");
        }

        if (!refreshes.IsCurrent(ticket)) return new(false, false, null);
        if (result.Success) uploadedTradeIds = result.TradeIds;
        return new(true, result.Success, result.ErrorMessage);
    }

    /// <summary>The Premium status a sell trade is uploaded with when the user doesn't choose one.</summary>
    public bool ResolvePremium(Trade trade) => trade.HasPremium ?? playerState.HasPremium ?? true;

    public static PortfolioTradeQualityKey QualityKey(TradeRowViewModel row) => new(row.ItemId, row.Source.AlbionServerId);

    /// <summary>How many Portfolio posts the rows would make; the service caps a single upload.</summary>
    public static int EstimatePostCount(IEnumerable<TradeRowViewModel> rows) =>
        PortfolioUploadService.EstimatePortfolioPostCount(rows.Select(row => new PortfolioTradePostEstimate(
            row.Source.ItemId,
            row.Source.AlbionServerId,
            row.Source.Amount,
            LocationIndex(row.Source),
            row.Source.QualityLevel > 0 ? row.Source.QualityLevel : 0)));

    /// <summary>
    /// Saves the chosen qualities and Premium status on the trades, uploads them and returns the status line to show.
    /// </summary>
    public async Task<string> ImportAsync(
        IReadOnlyList<TradeRowViewModel> rows,
        IReadOnlyDictionary<PortfolioTradeQualityKey, int> qualityOverrides,
        bool allowReupload,
        bool? premiumOverride,
        CancellationToken cancellationToken = default)
    {
        await SaveQualityOverridesAsync(rows, qualityOverrides, cancellationToken);
        await SavePremiumOverrideAsync(rows, premiumOverride, cancellationToken);

        var requests = new List<PortfolioTradeImportRequest>();
        var invalidTrades = 0;
        foreach (var row in rows)
        {
            var trade = row.Source;
            var locationIndex = LocationIndex(trade);
            if (locationIndex < 0 || string.IsNullOrWhiteSpace(trade.ItemId) || trade.Amount <= 0)
            {
                invalidTrades++;
                Log.Warning(
                    "Portfolio import request skipped invalid trade {TradeId}. Item={ItemId} Amount={Amount} LocationIndex={LocationIndex}",
                    trade.Id,
                    trade.ItemId,
                    trade.Amount,
                    locationIndex);
                continue;
            }

            var qualityIndex = trade.QualityLevel > 0
                ? trade.QualityLevel
                : qualityOverrides.TryGetValue(QualityKey(row), out var selectedQuality) ? selectedQuality : 1;
            bool? hasPremium = trade.Operation == TradeOperation.Sell ? premiumOverride ?? ResolvePremium(trade) : null;

            requests.Add(new PortfolioTradeImportRequest(
                trade.Id,
                trade.ItemId,
                trade.AlbionServerId,
                trade.Type,
                trade.Operation,
                trade.Amount,
                trade.UnitSilver,
                trade.DateTime,
                locationIndex,
                qualityIndex,
                HasPremium: hasPremium));
        }

        var result = await uploads.ImportTradesAsync(requests, allowReupload, cancellationToken);
        var presentIds = result.ImportedTradeIds.Concat(result.SkippedTradeIds).Distinct().ToList();
        if (presentIds.Count > 0)
        {
            // A response begun before this upload must not erase its newly confirmed IDs.
            refreshes.Invalidate();
            uploadedTradeIds.UnionWith(presentIds);
        }

        var status = $"Portfolio: {UiFormat.Whole(result.ImportedCount)} imported";
        if (result.ReuploadedCount > 0) status += $", {UiFormat.Whole(result.ReuploadedCount)} reuploaded";
        if (result.SkippedCount > 0) status += $", {UiFormat.Whole(result.SkippedCount)} skipped";
        var failedCount = result.FailedCount + invalidTrades;
        if (failedCount > 0) status += $", {UiFormat.Whole(failedCount)} failed";
        if (result.Errors.Count > 0) status += $". {result.Errors[0]}";
        else if (result.Warnings.Count > 0) status += $". {result.Warnings[0]}";
        return status;
    }

    private async Task SavePremiumOverrideAsync(IReadOnlyList<TradeRowViewModel> rows, bool? premiumOverride, CancellationToken cancellationToken)
    {
        if (premiumOverride is not { } premium) return;
        var sellRows = rows.Where(row => row.Source.Operation == TradeOperation.Sell).ToList();
        if (sellRows.Count == 0) return;

        var updatedCount = await trades.UpdateTradePremiumStatusAsync(sellRows.Select(row => row.Source.Id), premium, cancellationToken);
        if (updatedCount != sellRows.Count) throw new InvalidOperationException("Failed to save selected Portfolio Premium statuses.");
        foreach (var row in sellRows) row.Source.HasPremium = premium;
    }

    private async Task SaveQualityOverridesAsync(
        IReadOnlyList<TradeRowViewModel> rows,
        IReadOnlyDictionary<PortfolioTradeQualityKey, int> qualityOverrides,
        CancellationToken cancellationToken)
    {
        var rowsToUpdate = rows
            .Where(row => row.QualityLevel == 0
                && !row.UploadedToPortfolio
                && qualityOverrides.TryGetValue(QualityKey(row), out var quality)
                && quality is >= 1 and <= 5)
            .ToList();

        foreach (var group in rowsToUpdate.GroupBy(row => qualityOverrides[QualityKey(row)]))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var qualityLevel = (byte)group.Key;
            var groupRows = group.ToList();
            var updatedCount = await trades.UpdateTradeQualityLevelsAsync(groupRows.Select(row => row.Source.Id), qualityLevel);
            if (updatedCount != groupRows.Count) throw new InvalidOperationException("Failed to save selected Portfolio trade qualities.");
            foreach (var row in groupRows) row.SetQualityLevel(qualityLevel);
        }
    }

    private static int LocationIndex(Trade trade) => trade.Location?.MarketLocation?.IdInt ?? trade.Location?.IdInt ?? trade.LocationId;
}
