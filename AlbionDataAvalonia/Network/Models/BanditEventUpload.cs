namespace AlbionDataAvalonia.Network.Models;

public class BanditEventUpload : BaseUpload
{
    public long EventTime { get; set; }
    public bool AdvanceNotice { get; set; }
}
