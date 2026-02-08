using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
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
    public double UnitSilver { get; set; }
    public ulong TotalSilver => (ulong)(UnitSilver * (ulong)Amount);
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

    [NotMapped]
    public string QualityLevelFormatted
    {
        get
        {
            return QualityLevel switch
            {
                1 => "Normal",
                2 => "Good",
                3 => "Outstanding",
                4 => "Excellent",
                8 => "Masterpiece",
                _ => "Unknown"
            };
        }
    }

    public Trade()
    {
    }

    public Trade(MarketOrder order, int amount, int? albionServerId, string playerName, double salesTax)
    {
        _ = salesTax;

        if (order.LocationId == null)
        {
            throw new ArgumentNullException("LocationId is null");
        }

        switch (order.AuctionType)
        {
            case AuctionType.offer:
                Operation = TradeOperation.Buy;
                SalesTaxesPercent = 0;
                break;
            case AuctionType.request:
                Operation = TradeOperation.Sell;
                SalesTaxesPercent = 0;
                break;
        }
        Amount = amount;
        AlbionServerId = albionServerId;
        LocationId = order.Location.MarketLocation?.IdInt ?? -2;
        DateTime = DateTime.UtcNow;
        QualityLevel = order.QualityLevel;
        ItemId = order.ItemTypeId;
        PlayerName = playerName;
        var baseUnitSilver = order.UnitPriceSilver / 10000.0;
        var distanceFeeSilver = order.DistanceFee / 10000.0;
        var unitSilverWithDistanceFee = order.AuctionType == AuctionType.offer
            ? baseUnitSilver + distanceFeeSilver
            : baseUnitSilver - distanceFeeSilver;
        UnitSilver = NormalizeUnitSilver(unitSilverWithDistanceFee);
        Type = TradeType.Instant;
    }

    public Trade(AlbionMail mail, double salesTax)
    {
        _ = salesTax;

        switch (mail.AuctionType)
        {
            case AuctionType.offer:
                Operation = TradeOperation.Sell;
                SalesTaxesPercent = 0;
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
        UnitSilver = NormalizeUnitSilver(mail.UnitSilver);
        Type = TradeType.Order;

    }

    private static double NormalizeUnitSilver(double value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

}
