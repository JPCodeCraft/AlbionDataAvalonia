using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public sealed class PortfolioPositionDto
{
    public string? Id { get; set; }
    public string UniqueName { get; set; } = string.Empty;
    public string Server { get; set; } = string.Empty;
    public int QualityIndex { get; set; }
    public List<PortfolioTransactionDto> Transactions { get; set; } = new();
}

public sealed class PortfolioTransactionDto
{
    public string? Id { get; set; }
    public string Operation { get; set; } = string.Empty;
    public int Amount { get; set; }
    public double UnitPrice { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string LocationIndex { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? DataClientTradeId { get; set; }
    public string? BuyFromType { get; set; }
    public string? SellToType { get; set; }
    public bool? HasPremium { get; set; }
}

public readonly record struct PortfolioTradeQualityKey(string ItemId, int? AlbionServerId);

public sealed record PortfolioTradeImportRequest(
    Guid TradeId,
    string ItemId,
    int? AlbionServerId,
    TradeType TradeType,
    TradeOperation TradeOperation,
    int Amount,
    double UnitSilver,
    DateTime DateTime,
    int LocationIndex,
    int QualityIndex);

public sealed record PortfolioTradePostEstimate(
    string ItemId,
    int? AlbionServerId,
    int Amount,
    int LocationIndex,
    int QualityIndex);

public sealed record PortfolioUploadedTradeIdsResult(
    bool Success,
    HashSet<Guid> TradeIds,
    string? ErrorMessage)
{
    public static PortfolioUploadedTradeIdsResult Succeeded(HashSet<Guid> tradeIds)
    {
        return new PortfolioUploadedTradeIdsResult(true, tradeIds, null);
    }

    public static PortfolioUploadedTradeIdsResult Failed(string errorMessage)
    {
        return new PortfolioUploadedTradeIdsResult(false, new HashSet<Guid>(), errorMessage);
    }
}

public sealed class PortfolioImportResult
{
    public int RequestedCount { get; init; }
    public List<Guid> ImportedTradeIds { get; } = new();
    public List<Guid> ReuploadedTradeIds { get; } = new();
    public List<Guid> SkippedTradeIds { get; } = new();
    public List<Guid> FailedTradeIds { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public int ImportedCount => ImportedTradeIds.Count;
    public int ReuploadedCount => ReuploadedTradeIds.Count;
    public int SkippedCount => SkippedTradeIds.Count;
    public int FailedCount => FailedTradeIds.Count;
}
