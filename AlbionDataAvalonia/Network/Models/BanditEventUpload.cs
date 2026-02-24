namespace AlbionDataAvalonia.Network.Models;

public class BanditEventUpload : BaseUpload
{
    public long EventTime { get; set; }
    public int Phase { get; set; }
}
