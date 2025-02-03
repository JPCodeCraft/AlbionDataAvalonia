namespace AlbionDataAvalonia.Locations.Models;

public class AlbionLocation
{
    public string Id { get; set; }
    public int? IdInt { get; set; }
    public string Name { get; set; }
    public string FriendlyName { get; set; }

    public AlbionLocation(string id, string name, string friendlyName)
    {
        Id = id;
        IdInt = AlbionLocations.GetIdInt(id);
        Name = name;
        FriendlyName = friendlyName;
    }
}