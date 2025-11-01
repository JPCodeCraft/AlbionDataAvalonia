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
    public int Quality { get; set; }
    public bool IsAwakened { get; set; }
    public LegendarySoul? LegendarySoul { get; set; }

    public NewItem(long? objectId, int itemIndex, int quantity, long currentDurability, long estimatedMarketValue, int quality, string? crafterName, bool isAwakened)
    {
        ObjectId = objectId;
        ItemIndex = itemIndex;
        Quantity = quantity;
        CurrentDurability = currentDurability;
        EstimatedMarketValue = estimatedMarketValue;
        Quality = quality;
        LastSeen = DateTime.UtcNow;
        CrafterName = crafterName;
        IsAwakened = isAwakened;
    }
}