using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlbionDataAvalonia.Network.Models;

public class Trade
{
    public Guid Id { get; set; }
    public TradeType Type { get; set; }
    public int LocationId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public TradeOperation Operation { get; set; }
    public DateTime DateTime { get; set; }
    public int? AlbionServerId { get; set; }
    public int Amount { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public byte QualityLevel { get; set; }
    public ulong UnitSilver { get; set; }
    public ulong TotalSilver => UnitSilver * (ulong)Amount;
    public double SalesTaxesPercent { get; set; } = 0;
    public long SalesTaxes => (long)(TotalSilver * SalesTaxesPercent);
    public bool Deleted { get; set; } = false;


    [NotMapped]
    public string ItemName { get; set; } = string.Empty;
    [NotMapped]
    public AlbionLocation? Location { get; set; }
    [NotMapped]
    public AlbionServer? Server { get; set; }

    [NotMapped]
    public string TradeTypeFormatted
    {
        get
        {
            switch (Type)
            {
                case TradeType.Instant:
                    return "Instant";
                case TradeType.Order:
                    return "Order";
                default:
                    return "Unknown";
            }
        }
    }

    [NotMapped]
    public string TradeOperationFormatted
    {
        get
        {
            switch (Operation)
            {
                case TradeOperation.Buy:
                    return "Buy";
                case TradeOperation.Sell:
                    return "Sell";
                default:
                    return "Unknown";
            }
        }
    }

    public Trade()
    {
    }

    public Trade(MarketOrder order, int? albionServerId, string playerName, double salesTax)
    {
        switch (order.AuctionType)
        {
            case AuctionType.offer:
                Operation = TradeOperation.Buy;
                SalesTaxesPercent = 0;
                break;
            case AuctionType.request:
                Operation = TradeOperation.Sell;
                SalesTaxesPercent = salesTax;
                break;
        }
        Amount = (int)order.Amount;
        AlbionServerId = albionServerId;
        LocationId = order.LocationId;
        DateTime = DateTime.UtcNow;
        QualityLevel = order.QualityLevel;
        ItemId = order.ItemTypeId;
        PlayerName = playerName;
        UnitSilver = order.UnitPriceSilver / 10000;
        Type = TradeType.Instant;
    }

    public Trade(AlbionMail mail, double salesTax)
    {
        switch (mail.AuctionType)
        {
            case AuctionType.offer:
                Operation = TradeOperation.Sell;
                SalesTaxesPercent = salesTax;
                break;
            case AuctionType.request:
                Operation = TradeOperation.Buy;
                SalesTaxesPercent = 0.00;
                break;
        }
        Amount = mail.PartialAmount;
        AlbionServerId = mail.AlbionServerId;
        LocationId = mail.LocationId;
        DateTime = mail.Received;
        QualityLevel = 0;
        ItemId = mail.ItemId;
        PlayerName = mail.PlayerName;
        UnitSilver = (ulong)mail.UnitSilver;
        Type = TradeType.Order;

    }

}
