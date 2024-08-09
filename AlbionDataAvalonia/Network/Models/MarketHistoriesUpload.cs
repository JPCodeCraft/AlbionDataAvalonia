using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class MarketHistoriesUpload : BaseUpload
{
    public uint AlbionId;
    public int LocationId;
    public byte QualityLevel;
    public Timescale Timescale;
    public List<MarketHistory> MarketHistories = new List<MarketHistory>();
}
