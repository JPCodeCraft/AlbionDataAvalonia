using Serilog;
using System;

namespace AlbionDataAvalonia.Network.Models;

public class AlbionMail
{
    public long Id { get; set; }

    public int LocationId { get; set; }

    public MailInfoType Type { get; set; }

    public DateTime Expires { get; set; }

    public int AlbionServerId { get; set; }

    //MARKETPLACE_SELLORDER_FINISHED_SUMMARY "1|T5_2H_SHAPESHIFTER_SET3@1|1549840000|1549840000" AMOUNT|ITEM_ID|TOTAL_SILVER|UNIT_SILVER
    //MARKETPLACE_BUYORDER_FINISHED_SUMMARY "10|T7_ALCHEMY_RARE_ENT|11000100000|1100010000" AMOUNT|ITEM_ID|TOTAL_SILVER|UNIT_SILVER
    //MARKETPLACE_BUYORDER_EXPIRED_SUMMARY "23|100|65450000000|T5_ALCHEMY_RARE_PANTHER|" BOUGHT_AMOUNT|TOTAL_AMOUNT|TOTAL_REFUND|ITEM_ID
    //MARKETPLACE_SELLORDER_EXPIRED_SUMMARY "0|39|0|T7_JOURNAL_HUNTER_FULL|" SOLD_AMOUNT|TOTAL_AMOUNT|TOTAL_SILVER|ITEM_ID
    //BLACKMARKET_SELLORDER_EXPIRED_SUMMARY "6|53|4420680000|T6_OFF_HORN_KEEPER@1|" SOLD_AMOUNT|TOTAL_AMOUNT|TOTAL_SILVER|ITEM_ID
    public string MailString { get; set; } = string.Empty;

    public (int parcialAmount, int totalAmount, string itemId, long totalSilver, long unitSilver, long totalTaxes) GetData(double taxes)
    {
        try
        {
            var parts = MailString.Split('|');
            switch (Type)
            {
                case MailInfoType.MARKETPLACE_SELLORDER_FINISHED_SUMMARY:
                    return (int.Parse(parts[0]), int.Parse(parts[0]), parts[1], (long)(long.Parse(parts[2]) * (1 - taxes)) / 10000, (long)(long.Parse(parts[3]) * (1 - taxes)) / 10000, (long)(long.Parse(parts[2]) * (taxes)) / 10000);
                case MailInfoType.MARKETPLACE_BUYORDER_FINISHED_SUMMARY:
                    return (int.Parse(parts[0]), int.Parse(parts[0]), parts[1], long.Parse(parts[2]) / 10000, long.Parse(parts[3]) / 10000, 0);
                case MailInfoType.MARKETPLACE_BUYORDER_EXPIRED_SUMMARY:
                    int boughtAmount = int.Parse(parts[0]);
                    int totalAmount = int.Parse(parts[1]);
                    long totalRefund = long.Parse(parts[2]) / 10000;
                    long unitSilverCost = (long)((float)totalRefund / ((totalAmount - boughtAmount) == 0 ? 1 : (float)(totalAmount - boughtAmount)));
                    long totalSilverCost = unitSilverCost * boughtAmount;
                    return (boughtAmount, totalAmount, parts[3], totalSilverCost, unitSilverCost, (long)(totalSilverCost * taxes));
                case MailInfoType.MARKETPLACE_SELLORDER_EXPIRED_SUMMARY:
                    int soldAmount = int.Parse(parts[0]);
                    long totalSilver = (long)(long.Parse(parts[2]) * (1 - taxes)) / 10000;
                    long unitSilver = (long)((float)totalSilver / ((float)soldAmount == 0 ? 1 : (float)soldAmount));
                    return (soldAmount, int.Parse(parts[1]), parts[3], totalSilver, unitSilver, (long)(long.Parse(parts[2]) * taxes) / 10000);
                case MailInfoType.BLACKMARKET_SELLORDER_EXPIRED_SUMMARY:
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

    public string GetMailFriendlyString(double taxes)
    {
        try
        {
            var data = GetData(taxes);
            switch (Type)
            {
                case MailInfoType.MARKETPLACE_SELLORDER_FINISHED_SUMMARY:
                    return $"Sold {data.totalAmount:N0} {data.itemId} earning {data.unitSilver:N0} for each. A total of {data.totalSilver:N0} was earned. Taxes cost: {data.totalTaxes:N0}";
                case MailInfoType.MARKETPLACE_BUYORDER_FINISHED_SUMMARY:
                    return $"Bought {data.totalAmount:N0} {data.itemId} for {data.unitSilver:N0} each. A total of {data.totalSilver:N0} was spent.";
                case MailInfoType.MARKETPLACE_BUYORDER_EXPIRED_SUMMARY:
                    return $"Bought {data.parcialAmount:N0} of {data.totalAmount:N0} {data.itemId} for {data.totalSilver:N0} total silver. A total of {data.totalSilver:N0} was spent.";
                case MailInfoType.MARKETPLACE_SELLORDER_EXPIRED_SUMMARY:
                    return $"Sold {data.parcialAmount:N0} of {data.totalAmount:N0} {data.itemId} for {data.unitSilver:N0} each. A total of {data.totalSilver:N0} was earned. Taxes cost: {data.totalTaxes:N0}";
                case MailInfoType.BLACKMARKET_SELLORDER_EXPIRED_SUMMARY:
                    return $"Sold {data.parcialAmount:N0} of {data.totalAmount:N0} {data.itemId} for {data.unitSilver:N0} each. A total of {data.totalSilver:N0} was earned. Taxes cost: {data.totalTaxes:N0}";
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
