using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class BanditEventUploadEventArgs : EventArgs
{
    public BanditEventUpload BanditEventUpload { get; set; }
    public AlbionServer Server { get; set; }
    public UploadStatus UploadStatus { get; set; }

    public BanditEventUploadEventArgs(BanditEventUpload banditEventUpload, AlbionServer server, UploadStatus status)
    {
        BanditEventUpload = banditEventUpload;
        Server = server;
        UploadStatus = status;
    }
}
