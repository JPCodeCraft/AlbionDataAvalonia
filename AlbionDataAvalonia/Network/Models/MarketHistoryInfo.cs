namespace AlbionDataAvalonia.Network.Models
{
    public class MarketHistoryInfo
    {
        public uint AlbionId { get; set; }
        public Timescale Timescale { get; set; }
        public ushort Quality { get; set; }
        public int LocationID { get; set; }
    }
}
