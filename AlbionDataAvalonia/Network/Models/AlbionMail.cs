using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace AlbionDataAvalonia.Network.Models;

[Index(nameof(Id), IsUnique = true)]
[Index(nameof(AlbionServerId), nameof(LocationId), nameof(AuctionType), nameof(Deleted), nameof(Received))]
[Index(nameof(TotalSilver))]
public class AlbionMail
{
    public long Id { get; set; }

    public int LocationId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public AlbionMailInfoType Type { get; set; }
    public AuctionType AuctionType { get; set; }
    [NotMapped]
    public string AuctionTypeFormatted
    {
        get
        {
            switch (AuctionType)
            {
                case AuctionType.offer:
                    return "Sold";
                case AuctionType.request:
                    return "Bought";
                default:
                    return "Unknown";
            }
        }
    }

    public DateTime Received { get; set; }

    public int AlbionServerId { get; set; }
    public int PartialAmount { get; set; }
    public int TotalAmount { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public long TotalSilver { get; set; }
    public long UnitSilver { get; set; }
    public double TaxesPercent { get; set; }
    public long TotalTaxes { get; set; }
    public bool IsSet { get; set; } = false;
    public bool Deleted { get; set; } = false;

    [NotMapped]
    public string ItemName { get; set; } = string.Empty;
    [NotMapped]
    public AlbionLocation? Location { get; set; }
    [NotMapped]
    public AlbionServer? Server { get; set; }

    public AlbionMail()
    {

    }

    public AlbionMail(long id, int locationId, string playerName, AlbionMailInfoType type, DateTime received, int albionServerId, double taxes)
    {
        Id = id;
        LocationId = locationId;
        PlayerName = playerName;
        Type = type;
        Received = received;
        AlbionServerId = albionServerId;
        TaxesPercent = taxes;

        switch (Type)
        {
            case AlbionMailInfoType.MARKETPLACE_SELLORDER_FINISHED_SUMMARY:
                AuctionType = AuctionType.offer;
                break;
            case AlbionMailInfoType.MARKETPLACE_BUYORDER_FINISHED_SUMMARY:
                AuctionType = AuctionType.request;
                break;
            case AlbionMailInfoType.MARKETPLACE_BUYORDER_EXPIRED_SUMMARY:
                AuctionType = AuctionType.request;
                break;
            case AlbionMailInfoType.MARKETPLACE_SELLORDER_EXPIRED_SUMMARY:
                AuctionType = AuctionType.offer;
                break;
            case AlbionMailInfoType.BLACKMARKET_SELLORDER_EXPIRED_SUMMARY:
                AuctionType = AuctionType.offer;
                break;
            default:
                AuctionType = AuctionType.unknown;
                break;
        }
    }

    public void SetData(string mailString)
    {
        var data = GetData(TaxesPercent, mailString);
        PartialAmount = data.parcialAmount;
        TotalAmount = data.totalAmount;
        ItemId = data.itemId;
        TotalSilver = data.totalSilver;
        UnitSilver = data.unitSilver;
        TotalTaxes = data.totalTaxes;
        IsSet = true;
    }


    //MARKETPLACE_SELLORDER_FINISHED_SUMMARY "1|T5_2H_SHAPESHIFTER_SET3@1|1549840000|1549840000" AMOUNT|ITEM_ID|TOTAL_SILVER|UNIT_SILVER
    //MARKETPLACE_BUYORDER_FINISHED_SUMMARY "10|T7_ALCHEMY_RARE_ENT|11000100000|1100010000" AMOUNT|ITEM_ID|TOTAL_SILVER|UNIT_SILVER
    //MARKETPLACE_BUYORDER_EXPIRED_SUMMARY "23|100|65450000000|T5_ALCHEMY_RARE_PANTHER|" BOUGHT_AMOUNT|TOTAL_AMOUNT|TOTAL_REFUND|ITEM_ID
    //MARKETPLACE_SELLORDER_EXPIRED_SUMMARY "0|39|0|T7_JOURNAL_HUNTER_FULL|" SOLD_AMOUNT|TOTAL_AMOUNT|TOTAL_SILVER|ITEM_ID
    //BLACKMARKET_SELLORDER_EXPIRED_SUMMARY "6|53|4420680000|T6_OFF_HORN_KEEPER@1|" SOLD_AMOUNT|TOTAL_AMOUNT|TOTAL_SILVER|ITEM_ID
    private (int parcialAmount, int totalAmount, string itemId, long totalSilver, long unitSilver, long totalTaxes) GetData(double taxes, string mailString)
    {
        try
        {
            var parts = mailString.Split('|');
            switch (Type)
            {
                case AlbionMailInfoType.MARKETPLACE_SELLORDER_FINISHED_SUMMARY:
                    return (int.Parse(parts[0]), int.Parse(parts[0]), parts[1], (long)(long.Parse(parts[2]) * (1 - taxes)) / 10000, (long)(long.Parse(parts[3]) * (1 - taxes)) / 10000, (long)(long.Parse(parts[2]) * (taxes)) / 10000);
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_FINISHED_SUMMARY:
                    return (int.Parse(parts[0]), int.Parse(parts[0]), parts[1], long.Parse(parts[2]) / 10000, long.Parse(parts[3]) / 10000, 0);
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_EXPIRED_SUMMARY:
                    int boughtAmount = int.Parse(parts[0]);
                    int totalAmount = int.Parse(parts[1]);
                    long totalRefund = long.Parse(parts[2]) / 10000;
                    long unitSilverCost = (long)((float)totalRefund / ((totalAmount - boughtAmount) == 0 ? 1 : (float)(totalAmount - boughtAmount)));
                    long totalSilverCost = unitSilverCost * boughtAmount;
                    return (boughtAmount, totalAmount, parts[3], totalSilverCost, unitSilverCost, (long)(totalSilverCost * taxes));
                case AlbionMailInfoType.MARKETPLACE_SELLORDER_EXPIRED_SUMMARY:
                    int soldAmount = int.Parse(parts[0]);
                    long totalSilver = (long)(long.Parse(parts[2]) * (1 - taxes)) / 10000;
                    long unitSilver = (long)((float)totalSilver / ((float)soldAmount == 0 ? 1 : (float)soldAmount));
                    return (soldAmount, int.Parse(parts[1]), parts[3], totalSilver, unitSilver, (long)(long.Parse(parts[2]) * taxes) / 10000);
                case AlbionMailInfoType.BLACKMARKET_SELLORDER_EXPIRED_SUMMARY:
                    soldAmount = int.Parse(parts[0]);
                    totalSilver = (long)(long.Parse(parts[2]) * (1 - taxes)) / 10000;
                    unitSilver = (long)((float)totalSilver / ((float)soldAmount == 0 ? 1 : (float)soldAmount));
                    return (soldAmount, int.Parse(parts[1]), parts[3], totalSilver, unitSilver, (long)(long.Parse(parts[2]) * taxes) / 10000);
                default:
                    return (0, 0, "", 0, 0, 0);
            };
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return (0, 0, "", 0, 0, 0);
        }
    }

    public string GetMailFriendlyString()
    {
        try
        {
            switch (Type)
            {
                case AlbionMailInfoType.MARKETPLACE_SELLORDER_FINISHED_SUMMARY:
                    return $"Sold {TotalAmount:N0} {ItemId} earning {UnitSilver:N0} for each. A total of {TotalSilver:N0} was earned. Taxes cost: {TotalTaxes:N0}";
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_FINISHED_SUMMARY:
                    return $"Bought {TotalAmount:N0} {ItemId} for {UnitSilver:N0} each. A total of {TotalSilver:N0} was spent.";
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_EXPIRED_SUMMARY:
                    return $"Bought {PartialAmount:N0} of {TotalAmount:N0} {ItemId} for {TotalSilver:N0} total silver. A total of {TotalSilver:N0} was spent.";
                case AlbionMailInfoType.MARKETPLACE_SELLORDER_EXPIRED_SUMMARY:
                    return $"Sold {PartialAmount:N0} of {TotalAmount:N0} {ItemId} for {UnitSilver:N0} each. A total of {TotalSilver:N0} was earned. Taxes cost: {TotalTaxes:N0}";
                case AlbionMailInfoType.BLACKMARKET_SELLORDER_EXPIRED_SUMMARY:
                    return $"Sold {PartialAmount:N0} of {TotalAmount:N0} {ItemId} for {UnitSilver:N0} each. A total of {TotalSilver:N0} was earned. Taxes cost: {TotalTaxes:N0}";
                default:
                    return "Unknown mail info type";
            };
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return "Error parsing mail info";
        }
    }

}
