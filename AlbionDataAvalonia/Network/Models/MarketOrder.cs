using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using AlbionTopItems.Models.Converters;
using System;
using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Network.Models;

public class MarketOrder
{
    public ulong Id { get; set; }
    public string ItemTypeId { get; set; }
    public string ItemGroupTypeId { get; set; }

    [JsonConverter(typeof(LocationIdConverter))]
    public int? LocationId { get; set; }

    public byte QualityLevel { get; set; }
    public byte EnchantmentLevel { get; set; }
    public ulong UnitPriceSilver { get; set; }
    public uint Amount { get; set; }
    public AuctionType AuctionType { get; set; }
    public DateTime Expires { get; set; }
    [JsonIgnore]
    public AlbionLocation Location
    {
        get
        {
            return AlbionLocations.Get(LocationId ?? -2) ?? AlbionLocations.Unknown;
        }
    }
}
