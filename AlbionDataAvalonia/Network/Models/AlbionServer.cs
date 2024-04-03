namespace AlbionDataAvalonia.Network.Models;

public class AlbionServer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string HostIp { get; set; }
    public string UploadUrl { get; set; }

    public AlbionServer(int id, string name, string hostIp, string uploadUrl)
    {
        Id = id;
        Name = name;
        HostIp = hostIp;
        UploadUrl = uploadUrl;
    }

}
