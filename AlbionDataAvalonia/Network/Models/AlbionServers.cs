using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Models;

public static class AlbionServers
{
    public static AlbionServer Americas { get; } = new AlbionServer(1, "Americas", "5.188.125", "https://pow.west.albion-online-data.com");

    public static AlbionServer Asia { get; } = new AlbionServer(2, "Asia", "5.45.187", "https://pow.east.albion-online-data.com");

    public static AlbionServer Europe { get; } = new AlbionServer(3, "Europe", "193.169.238", "https://pow.europe.albion-online-data.com");

    public static List<AlbionServer> GetAll() => typeof(AlbionServers).GetProperties().Select(field => field.GetValue(null)).OfType<AlbionServer>().ToList();
    public static AlbionServer? Get(string name) => GetAll().SingleOrDefault(server => server.Name.ToLower() == name.ToLower());
    public static AlbionServer? Get(int id) => GetAll().SingleOrDefault(server => server.Id == id);
    public static bool TryParse(string info, out AlbionServer? server)
    {
        // try to parse to an int, then its Id
        if (int.TryParse(info, out var id))
        {
            server = Get(id);
        }
        else
        {
            server = Get(info);
        }

        return server != null;
    }
}

