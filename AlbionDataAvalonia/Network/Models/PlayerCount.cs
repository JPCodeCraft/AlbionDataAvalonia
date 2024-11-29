using AlbionDataAvalonia.Locations.Models;
using System;

namespace AlbionDataAvalonia.Network.Models;

public class PlayerCount
{
    public AlbionLocation Location { get; set; }
    public AlbionServer Server { get; set; }
    public DateTime DateTime { get; set; }
    public byte? NonFlaggedCount { get; set; }
    public byte? FlaggedCount { get; set; }
    public bool IsBz { get; set; }
}
