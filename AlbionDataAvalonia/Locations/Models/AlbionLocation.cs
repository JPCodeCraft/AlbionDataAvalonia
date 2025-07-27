namespace AlbionDataAvalonia.Locations.Models;

public class AlbionLocation
{

    public string Id { get; set; }
    public int? IdInt { get; set; }
    public string Name { get; set; }
    public string FriendlyName { get; set; }
    public AlbionLocation? MarketLocation { get; set; } = null;

    public AlbionLocation(string id, string name, string friendlyName = "")
    {
        Id = id;
        IdInt = AlbionLocations.GetLocationIdInt(Id);
        Name = name;
        FriendlyName = friendlyName;
    }
}