using System;
using System.Collections.Generic;
using AlbionDataAvalonia.Locations.Models;

namespace AlbionDataAvalonia.Network.Models;

public class AlbionContainer
{
    public Guid Guid { get; set; }
    public string Name { get; set; }
    public string Icon { get; set; }
    public int ItemCount { get; set; }
    public long TotalValues { get; set; }
    public List<NewItem> Items { get; set; } = new();
    public DateTime LastUpdate { get; set; }

    public AlbionContainer(Guid guid, string name, string icon, int itemCount, long totalValues)
    {
        Guid = guid;
        Name = name;
        Icon = icon;
        ItemCount = itemCount;
        TotalValues = totalValues;
        LastUpdate = DateTime.UtcNow;
    }
}