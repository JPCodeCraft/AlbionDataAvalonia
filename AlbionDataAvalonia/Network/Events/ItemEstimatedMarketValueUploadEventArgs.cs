using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class ItemEstimatedMarketValueUploadEventArgs : EventArgs
{
    public ItemEstimatedMarketValueUpload ItemEstimatedMarketValueUpload { get; set; }
    public UploadStatus UploadStatus { get; set; }
    public UploadScope Scope { get; set; }
    public Guid Identifier { get; set; }
    public int ItemsCount => ItemEstimatedMarketValueUpload.Items.Count;

    public ItemEstimatedMarketValueUploadEventArgs(ItemEstimatedMarketValueUpload itemEstimatedMarketValueUpload, UploadStatus uploadStatus, UploadScope scope, Guid identifier)
    {
        ItemEstimatedMarketValueUpload = itemEstimatedMarketValueUpload;
        UploadStatus = uploadStatus;
        Scope = scope;
        Identifier = identifier;
    }
}
