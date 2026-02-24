using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class GlobalMultiplierUploadEventArgs : EventArgs
{
    public GlobalMultiplierUpload GlobalMultiplierUpload { get; set; }
    public UploadStatus UploadStatus { get; set; }
    public UploadScope Scope { get; set; }
    public Guid Identifier { get; set; }

    public GlobalMultiplierUploadEventArgs(GlobalMultiplierUpload globalMultiplierUpload, UploadStatus uploadStatus, UploadScope scope, Guid identifier)
    {
        GlobalMultiplierUpload = globalMultiplierUpload;
        UploadStatus = uploadStatus;
        Scope = scope;
        Identifier = identifier;
    }
}
