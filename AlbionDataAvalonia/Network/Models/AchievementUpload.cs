using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class AchievementUpload
{
    public string CharacterName { get; set; } = string.Empty;
    public AlbionServer Server { get; set; } = null!;
    public List<AchievementUploadEntry> Achievements { get; set; } = new();
}

public class AchievementUploadEntry
{
    public string Id { get; set; } = string.Empty;
    public byte Level { get; set; }
}
