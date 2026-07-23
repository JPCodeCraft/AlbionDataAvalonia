using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class CraftingBonusUpload
{
    public int ServerId { get; set; }
    public List<CraftingBonusUploadEntry> Entries { get; set; } = [];
}

public class CraftingBonusUploadEntry
{
    public byte EventType { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string UniqueName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
