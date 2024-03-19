
using AlbionData.Models;
using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class GoldPriceUploadEventArgs : EventArgs
{
    public GoldPriceUpload GoldPriceUpload { get; set; }
    public AlbionServer Server { get; set; }
    public GoldPriceUploadEventArgs(GoldPriceUpload goldPriceUpload, AlbionServer server)
    {
        GoldPriceUpload = goldPriceUpload;
        Server = server;
    }
}
