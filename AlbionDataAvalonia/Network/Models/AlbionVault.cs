using System;
using System.Collections.Generic;
using AlbionDataAvalonia.Locations.Models;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Network.Models;

public class AlbionVault
{
    public Guid Guid { get; set; }
    public AlbionLocation Location { get; set; }
    public List<AlbionContainer> Containers { get; set; }
    public DateTime LastUpdate { get; set; }

    public AlbionVault(Guid guid, AlbionLocation location, List<AlbionContainer> containers)
    {
        Guid = guid;
        Location = location;
        Containers = containers;
        LastUpdate = DateTime.UtcNow;
    }
}