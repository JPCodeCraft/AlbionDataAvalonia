using System.Collections.Generic;
using System.Text.Json.Serialization;
using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;

namespace AlbionDataAvalonia.Network.Models;

public class MarketHistoriesUpload : BaseUpload
{
    public uint AlbionId;
    public int LocationId;
    public byte QualityLevel;
    public Timescale Timescale;
    public List<MarketHistory> MarketHistories = new List<MarketHistory>();
    [JsonIgnore]
    public AlbionLocation Location
    {
        get
        {
            return AlbionLocations.Get(LocationId) ?? AlbionLocations.Unknown;
        }
    }
}
