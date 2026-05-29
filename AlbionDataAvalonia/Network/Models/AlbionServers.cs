using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Models;

public static class AlbionServers
{
    public static AlbionServer Americas { get; } = new AlbionServer(1, "Americas", "https://pow.west.albion-online-data.com", ["5.188.125", "85.234.70"]);
    // public static AlbionServer Americas { get; } = new AlbionServer(1, "Americas", "http://localhost:3000", ["5.188.125", "85.234.70"]);

    public static AlbionServer Asia { get; } = new AlbionServer(2, "Asia", "https://pow.east.albion-online-data.com", ["5.45.187"]);

    public static AlbionServer Europe { get; } = new AlbionServer(3, "Europe", "https://pow.europe.albion-online-data.com", ["193.169.238"]);

    private static readonly List<AlbionServer> _all = [Americas, Asia, Europe];
    public static List<AlbionServer> GetAll() => _all;
    public static AlbionServer? Get(string name) => _all.SingleOrDefault(server => server.Name.ToLower() == name.ToLower());
    public static AlbionServer? Get(int id) => _all.SingleOrDefault(server => server.Id == id);
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

