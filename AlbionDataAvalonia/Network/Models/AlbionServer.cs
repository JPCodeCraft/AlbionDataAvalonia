namespace AlbionDataAvalonia.Network.Models;

public class AlbionServer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string[] HostIps { get; set; }
    public string UploadUrl { get; set; }

    public AlbionServer(int id, string name, string uploadUrl, string[] hostIps)
    {
        Id = id;
        Name = name;
        HostIps = hostIps;
        UploadUrl = uploadUrl;
    }

}
