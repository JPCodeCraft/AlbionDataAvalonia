using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class ItemEstimatedMarketValueUpload : BaseUpload
{
    public int ServerId { get; set; }
    public List<ItemEstimatedMarketValueUploadEntry> Items { get; set; } = new();
}

public class ItemEstimatedMarketValueUploadEntry
{
    public string ItemUniqueName { get; set; } = string.Empty;
    public long Emv { get; set; }
    public long? BlackMarketEmv { get; set; }
    public int Quality { get; set; }
    public DateOnly Day { get; set; }
}
