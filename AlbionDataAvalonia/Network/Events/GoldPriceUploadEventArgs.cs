using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class GoldPriceUploadEventArgs : EventArgs
{
    public GoldPriceUpload GoldPriceUpload { get; set; }
    public AlbionServer Server { get; set; }
    public UploadStatus UploadStatus { get; set; }
    public UploadScope Scope { get; set; }

    public GoldPriceUploadEventArgs(GoldPriceUpload goldPriceUpload, AlbionServer server, UploadStatus status, UploadScope scope)
    {
        GoldPriceUpload = goldPriceUpload;
        Server = server;
        UploadStatus = status;
        Scope = scope;
    }
}
