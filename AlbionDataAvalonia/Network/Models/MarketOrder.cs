using System;
using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Network.Models;

public class MarketOrder
{
    public ulong Id { get; set; }
    public string ItemTypeId { get; set; }
    public int LocationId { get; set; }
    public byte QualityLevel { get; set; }
    public byte EnchantmentLevel { get; set; }
    public ulong UnitPriceSilver { get; set; }
    public uint Amount { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AuctionType AuctionType { get; set; }
    public DateTime Expires { get; set; }
    public string ItemGroupTypeId { get; set; }
}
