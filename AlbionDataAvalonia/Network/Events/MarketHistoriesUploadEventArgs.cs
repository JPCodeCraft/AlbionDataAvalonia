using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class MarketHistoriesUploadEventArgs : EventArgs
{
    public MarketHistoriesUpload MarketHistoriesUpload { get; set; }
    public AlbionServer Server { get; set; }
    public MarketHistoriesUploadEventArgs(MarketHistoriesUpload marketHistoriesUpload, AlbionServer server)
    {
        MarketHistoriesUpload = marketHistoriesUpload;
        Server = server;
    }
}
