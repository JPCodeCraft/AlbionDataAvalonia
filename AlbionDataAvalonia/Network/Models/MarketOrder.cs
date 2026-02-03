using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AlbionDataAvalonia.Network.Models;

public class MarketOrder
{
    public ulong Id { get; set; }
    public string ItemTypeId { get; set; } = string.Empty;
    public string ItemGroupTypeId { get; set; } = string.Empty;
    public string LocationId { get; set; } = string.Empty;
    public byte QualityLevel { get; set; }
    public byte EnchantmentLevel { get; set; }
    public ulong UnitPriceSilver { get; set; }
    public uint Amount { get; set; }
    public AuctionType AuctionType { get; set; }
    public DateTime Expires { get; set; }
    [JsonIgnore]
    public ulong DistanceFee { get; set; }
    [JsonIgnore]
    public AlbionLocation Location
    {
        get
        {
            string? query;
            if (LocationId.Contains("@"))
            {
                query = LocationId.Split('@')[1];
            }
            else
            {
                query = LocationId;
            }
            return AlbionLocations.Get(query) ?? AlbionLocations.Unknown;
        }
    }
}
