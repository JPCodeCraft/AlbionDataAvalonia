using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class MarketHistoriesUploadEventArgs : EventArgs
{
    public MarketHistoriesUpload MarketHistoriesUpload { get; set; }
    public AlbionServer Server { get; set; }
    public UploadStatus UploadStatus { get; set; }
    public UploadScope Scope { get; set; }

    public MarketHistoriesUploadEventArgs(MarketHistoriesUpload marketHistoriesUpload, AlbionServer server, UploadStatus uploadStatus, UploadScope scope)
    {
        MarketHistoriesUpload = marketHistoriesUpload;
        Server = server;
        UploadStatus = uploadStatus;
        Scope = scope;
    }
}
