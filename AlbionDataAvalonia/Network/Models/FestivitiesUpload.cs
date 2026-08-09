using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class FestivitiesUpload
{
    public int ServerId { get; set; }
    public List<FestivitiesUploadEvent> Events { get; set; } = [];
}

public class FestivitiesUploadEvent
{
    public byte Kind { get; set; }
    public string Category { get; set; } = string.Empty;
    public string UniqueName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
