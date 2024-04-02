using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Models;

public static class AlbionServers
{
    public static readonly AlbionServer West = new AlbionServer(1, "Americas", "5.188.125", "https://pow.west.albion-online-data.com");
    public static readonly AlbionServer East = new AlbionServer(2, "Asia", "5.45.187", "https://pow.east.albion-online-data.com");
    public static readonly AlbionServer Europe = new AlbionServer(3, "Europe", "193.169.238", "https://pow.east.albion-online-data.com");
    public static List<AlbionServer> GetAllServers()
    {
        return typeof(AlbionServers)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(AlbionServer))
            .Select(f => f.GetValue(null))
            .OfType<AlbionServer>()
            .ToList();
    }
}
