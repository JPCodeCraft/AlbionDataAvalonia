namespace AlbionDataAvalonia.Locations.Models;

public class AlbionLocation
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string FriendlyName { get; set; }

    public AlbionLocation(int id, string name, string friendlyName)
    {
        Id = id;
        Name = name;
        FriendlyName = friendlyName;
    }
}