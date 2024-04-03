using AlbionDataAvalonia.Network.Models;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Settings;

public class AppSettings
{
    public string? NPCapDownloadUrl { get; set; }
    public string? PacketFilterPortText { get; set; }
    public List<AlbionServer> AlbionServers { get; set; } = new List<AlbionServer>();
}
