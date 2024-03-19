namespace AlbionDataAvalonia.Network.Models;

public class AlbionServer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string IpBase { get; set; }
    public string UploadUrl { get; set; }

    public AlbionServer(int id, string name, string ipBase, string uploadUrl)
    {
        Id = id;
        Name = name;
        IpBase = ipBase;
        UploadUrl = uploadUrl;
    }

}
