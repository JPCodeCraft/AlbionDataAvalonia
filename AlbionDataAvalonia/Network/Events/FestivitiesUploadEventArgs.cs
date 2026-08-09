using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class FestivitiesUploadEventArgs : EventArgs
{
    public FestivitiesUpload FestivitiesUpload { get; }
    public UploadStatus UploadStatus { get; }
    public UploadScope Scope { get; }
    public Guid Identifier { get; }

    public FestivitiesUploadEventArgs(
        FestivitiesUpload festivitiesUpload,
        UploadStatus uploadStatus,
        UploadScope scope,
        Guid identifier)
    {
        FestivitiesUpload = festivitiesUpload;
        UploadStatus = uploadStatus;
        Scope = scope;
        Identifier = identifier;
    }
}
