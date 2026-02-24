using AlbionDataAvalonia.Network.Models;
using System;

namespace AlbionDataAvalonia.Network.Events;

public class AchievementsUploadEventArgs : EventArgs
{
    public AchievementUpload AchievementUpload { get; set; }
    public UploadStatus UploadStatus { get; set; }
    public UploadScope Scope { get; set; }
    public Guid Identifier { get; set; }
    public int AchievementsCount => AchievementUpload.Achievements.Count;

    public AchievementsUploadEventArgs(AchievementUpload achievementUpload, UploadStatus uploadStatus, UploadScope scope, Guid identifier)
    {
        AchievementUpload = achievementUpload;
        UploadStatus = uploadStatus;
        Scope = scope;
        Identifier = identifier;
    }
}
