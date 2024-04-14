using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class MarketUploadEventArgs : EventArgs
{
    public MarketUpload MarketUpload { get; set; }
    public AlbionServer Server { get; set; }
    public MarketUploadEventArgs(MarketUpload marketUpload, AlbionServer server)
    {
        MarketUpload = marketUpload;
        Server = server;
    }
}
