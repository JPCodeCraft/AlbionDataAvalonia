using System;

public class NewItem
{
    public long? ObjectId { get; set; }
    public int ItemIndex { get; set; }
    public string ItemUniqueName { get; set; } = "Unset";
    public string ItemUsName { get; set; } = "Unset";
    public string? CrafterName { get; set; }
    public DateTime LastSeen { get; set; }
    public int Quantity { get; set; }
    public long CurrentDurability { get; set; }
    public long EstimatedMarketValue { get; set; }
    public long? BlackMarketEstimatedMarketValue { get; set; }
    public int Quality { get; set; }
    public bool IsAwakened { get; set; }
    public NewItem(long? objectId, int itemIndex, int quantity, long currentDurability, long estimatedMarketValue, long? blackMarketEstimatedMarketValue, int quality, string? crafterName, bool isAwakened)
    {
        ObjectId = objectId;
        ItemIndex = itemIndex;
        Quantity = quantity;
        CurrentDurability = currentDurability;
        EstimatedMarketValue = estimatedMarketValue;
        BlackMarketEstimatedMarketValue = blackMarketEstimatedMarketValue;
        Quality = quality;
        LastSeen = DateTime.UtcNow;
        CrafterName = crafterName;
        IsAwakened = isAwakened;
    }
}
