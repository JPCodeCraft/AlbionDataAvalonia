using AlbionDataAvalonia.Locations.Models;
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
    public double UnitSilver { get; set; }
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

    public AlbionMail(long id, int locationId, string playerName, AlbionMailInfoType type, DateTime received, int albionServerId)
    {
        Id = id;
        LocationId = locationId;
        PlayerName = playerName;
        Type = type;
        Received = received;
        AlbionServerId = albionServerId;
        TaxesPercent = 0;

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
        var data = GetData(mailString);
        PartialAmount = data.partialAmount;
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
    private (int partialAmount, int totalAmount, string itemId, long totalSilver, double unitSilver, long totalTaxes) GetData(string mailString)
    {
        var parts = mailString.Split('|');

        int partialAmountData = 0;
        int totalAmountData = 0;
        string itemIdData = "";
        long totalSilverData = 0;
        double unitSilverData = 0;
        long totalTaxesData = 0;
        try
        {
            switch (Type)
            {
                case AlbionMailInfoType.MARKETPLACE_SELLORDER_FINISHED_SUMMARY:
                    partialAmountData = int.Parse(parts[0]);
                    totalAmountData = partialAmountData;
                    itemIdData = parts[1];
                    totalSilverData = long.Parse(parts[2]) / 10000;
                    unitSilverData = NormalizeUnitSilver(long.Parse(parts[3]) / 10000.0);
                    totalTaxesData = 0;
                    break;
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_FINISHED_SUMMARY:
                    partialAmountData = int.Parse(parts[0]);
                    totalAmountData = partialAmountData;
                    itemIdData = parts[1];
                    totalSilverData = long.Parse(parts[2]) / 10000;
                    unitSilverData = NormalizeUnitSilver(long.Parse(parts[3]) / 10000.0);
                    totalTaxesData = 0;
                    break;
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_EXPIRED_SUMMARY:
                    partialAmountData = int.Parse(parts[0]);
                    totalAmountData = int.Parse(parts[1]);
                    var totalRefund = long.Parse(parts[2]) / 10000.0;
                    itemIdData = parts[3];
                    var remainingAmount = totalAmountData - partialAmountData;
                    unitSilverData = NormalizeUnitSilver(remainingAmount > 0 ? totalRefund / (double)remainingAmount : 0);
                    totalSilverData = (long)Math.Round(unitSilverData * partialAmountData, MidpointRounding.AwayFromZero);
                    totalTaxesData = 0;
                    break;
                case AlbionMailInfoType.MARKETPLACE_SELLORDER_EXPIRED_SUMMARY:
                    partialAmountData = int.Parse(parts[0]);
                    totalAmountData = int.Parse(parts[1]);
                    itemIdData = parts[3];
                    totalSilverData = long.Parse(parts[2]) / 10000;
                    unitSilverData = NormalizeUnitSilver(partialAmountData == 0 ? 0 : totalSilverData / (double)partialAmountData);
                    totalTaxesData = 0;
                    break;
                case AlbionMailInfoType.BLACKMARKET_SELLORDER_EXPIRED_SUMMARY:
                    partialAmountData = int.Parse(parts[0]);
                    totalAmountData = int.Parse(parts[1]);
                    itemIdData = parts[3];
                    totalSilverData = long.Parse(parts[2]) / 10000;
                    unitSilverData = NormalizeUnitSilver(partialAmountData == 0 ? 0 : totalSilverData / (double)partialAmountData);
                    totalTaxesData = 0;
                    break;
                default:
                    break;
            }
            ;

            return (partialAmountData, totalAmountData, itemIdData, totalSilverData, unitSilverData, totalTaxesData);
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
                    return $"Sold {TotalAmount:N0} {ItemId} earning {UnitSilver:N0} for each. A total of {TotalSilver:N0} was earned.";
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_FINISHED_SUMMARY:
                    return $"Bought {TotalAmount:N0} {ItemId} for {UnitSilver:N0} each. A total of {TotalSilver:N0} was spent.";
                case AlbionMailInfoType.MARKETPLACE_BUYORDER_EXPIRED_SUMMARY:
                    return $"Bought {PartialAmount:N0} of {TotalAmount:N0} {ItemId} for {TotalSilver:N0} total silver. A total of {TotalSilver:N0} was spent.";
                case AlbionMailInfoType.MARKETPLACE_SELLORDER_EXPIRED_SUMMARY:
                    return $"Sold {PartialAmount:N0} of {TotalAmount:N0} {ItemId} for {UnitSilver:N0} each. A total of {TotalSilver:N0} was earned.";
                case AlbionMailInfoType.BLACKMARKET_SELLORDER_EXPIRED_SUMMARY:
                    return $"Sold {PartialAmount:N0} of {TotalAmount:N0} {ItemId} for {UnitSilver:N0} each. A total of {TotalSilver:N0} was earned.";
                default:
                    return "Unknown mail info type";
            }
            ;
        }
        catch (Exception e)
        {
            Log.Error(e, e.Message);
            return "Error parsing mail info";
        }
    }

    private static double NormalizeUnitSilver(double value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

}
