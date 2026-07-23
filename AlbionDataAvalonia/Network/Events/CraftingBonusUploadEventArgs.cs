using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class CraftingBonusUploadEventArgs : EventArgs
{
    public CraftingBonusUpload CraftingBonusUpload { get; }
    public UploadStatus UploadStatus { get; }
    public UploadScope Scope { get; }
    public Guid Identifier { get; }

    public CraftingBonusUploadEventArgs(
        CraftingBonusUpload craftingBonusUpload,
        UploadStatus uploadStatus,
        UploadScope scope,
        Guid identifier)
    {
        CraftingBonusUpload = craftingBonusUpload;
        UploadStatus = uploadStatus;
        Scope = scope;
        Identifier = identifier;
    }
}
