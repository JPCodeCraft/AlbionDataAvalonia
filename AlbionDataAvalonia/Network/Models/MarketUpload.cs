using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class MarketUpload : BaseUpload
{
    public List<MarketOrder> Orders { get; set; } = new List<MarketOrder>();
}
