using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class MarketHistoriesUpload
{
    public uint AlbionId;
    public string AlbionIdString;
    public int LocationId;
    public byte QualityLevel;
    public Timescale Timescale;
    public List<MarketHistory> MarketHistories = new List<MarketHistory>();
}
