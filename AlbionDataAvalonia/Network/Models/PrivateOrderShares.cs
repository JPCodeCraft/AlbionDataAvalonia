using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Models;

public class PrivateOrderShareEntry
{
    public string Value { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Resolved { get; set; }
}

public class PrivateOrderSharesResponse
{
    public List<PrivateOrderShareEntry> SharedUsers { get; set; } = new();
}

public class SavePrivateOrderSharesResponse : PrivateOrderSharesResponse
{
    public List<string> UnresolvedEntries { get; set; } = new();
}

public class PrivateOrderSharesRequest
{
    public List<string> SharedUsers { get; set; } = new();
}
