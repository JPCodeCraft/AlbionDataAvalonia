using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Network.Models;

public static class AlbionLocations
{
    public static AlbionLocation SwampCross { get; } = new() { Id = 4, Name = "SwampCross", FriendlyName = "Swamp Cross" };
    public static AlbionLocation Thetford { get; } = new() { Id = 7, Name = "Thetford", FriendlyName = "Thetford" };
    public static AlbionLocation ThetfordPortal { get; } = new() { Id = 301, Name = "ThetfordPortal", FriendlyName = "Thetford Portal" };
    public static AlbionLocation MorganasRest { get; } = new() { Id = 8, Name = "MorganasRest", FriendlyName = "Morgana's Rest" };
    public static AlbionLocation Lymhurst { get; } = new() { Id = 1002, Name = "Lymhurst", FriendlyName = "Lymhurst" };
    public static AlbionLocation LymhurstPortal { get; } = new() { Id = 1301, Name = "LymhurstPortal", FriendlyName = "Lymhurst Portal" };
    public static AlbionLocation ForestCross { get; } = new() { Id = 1006, Name = "ForestCross", FriendlyName = "Forest Cross" };
    public static AlbionLocation MerlynsRest { get; } = new() { Id = 1012, Name = "MerlynsRest", FriendlyName = "Merlyn's Rest" };
    public static AlbionLocation SteppeCross { get; } = new() { Id = 2002, Name = "SteppeCross", FriendlyName = "Steppe Cross" };
    public static AlbionLocation Bridgewatch { get; } = new() { Id = 2004, Name = "Bridgewatch", FriendlyName = "Bridgewatch" };
    public static AlbionLocation BridgewatchPortal { get; } = new() { Id = 2301, Name = "BridgewatchPortal", FriendlyName = "Bridgewatch Portal" };
    public static AlbionLocation HighlandCross { get; } = new() { Id = 3002, Name = "HighlandCross", FriendlyName = "Highland Cross" };
    public static AlbionLocation BlackMarket { get; } = new() { Id = 3003, Name = "BlackMarket", FriendlyName = "Black Market" };
    public static AlbionLocation Caerleon { get; } = new() { Id = 3005, Name = "Caerleon", FriendlyName = "Caerleon" };
    public static AlbionLocation Caerleon2 { get; } = new() { Id = 3013, Name = "Caerleon2", FriendlyName = "Caerleon 2" };
    public static AlbionLocation Martlock { get; } = new() { Id = 3008, Name = "Martlock", FriendlyName = "Martlock" };
    public static AlbionLocation MartlockPortal { get; } = new() { Id = 3301, Name = "MartlockPortal", FriendlyName = "Martlock Portal" };
    public static AlbionLocation FortSterling { get; } = new() { Id = 4002, Name = "FortSterling", FriendlyName = "Fort Sterling" };
    public static AlbionLocation FortSterlingPortal { get; } = new() { Id = 4301, Name = "FortSterlingPortal", FriendlyName = "Fort Sterling Portal" };
    public static AlbionLocation MountainCross { get; } = new() { Id = 4006, Name = "MountainCross", FriendlyName = "Mountain Cross" };
    public static AlbionLocation ArthursRest { get; } = new() { Id = 4300, Name = "ArthursRest", FriendlyName = "Arthur's Rest" };
    public static AlbionLocation Brecilien { get; } = new() { Id = 5003, Name = "Brecilien", FriendlyName = "Brecilien" };
    public static AlbionLocation Unknown { get; } = new() { Id = 0, Name = "Unknown", FriendlyName = "Unknown" };
    public static AlbionLocation Unset { get; } = new() { Id = -1, Name = "Unset", FriendlyName = "Unset" };

    public static List<AlbionLocation> GetAll() => typeof(AlbionLocations).GetProperties().Select(field => field.GetValue(null)).OfType<AlbionLocation>().ToList();
    public static AlbionLocation? Get(string name) => GetAll().SingleOrDefault(location => location.Name.ToLower() == name.ToLower() || location.FriendlyName.Replace(" ", "").ToLower() == name.Replace(" ", "").Replace("@", "").Replace("_", "").Replace("-", "").ToLower());
    public static AlbionLocation? Get(int id) => GetAll().SingleOrDefault(location => location.Id == id);
    public static bool TryParse(string info, out AlbionLocation? location)
    {
        // try to parse to an int, then its Id
        if (int.TryParse(info, out var id))
        {
            location = Get(id);
        }
        else
        {
            location = Get(info);
        }

        return location != null;
    }
}
