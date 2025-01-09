namespace AlbionDataAvalonia.Network.Models;

public class AfmMarketUpload : MarketUpload
{
    public int ServerId { get; set; }
    public string UploaderId { get; set; }

    public AfmMarketUpload(MarketUpload marketUpload, int serverId, string uploaderId)
    {
        Orders = marketUpload.Orders;
        ServerId = serverId;
        UploaderId = uploaderId;
    }
}
