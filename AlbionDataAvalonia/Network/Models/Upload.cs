namespace AlbionDataAvalonia.Network.Models;

public class Upload
{
    public MarketUpload? MarketUpload { get; }
    public GoldPriceUpload? GoldPriceUpload { get; }
    public MarketHistoriesUpload? MarketHistoriesUpload { get; }
    public BanditEventUpload? BanditEventUpload { get; }
    public int Count => (MarketUpload is not null ? 1 : 0) + (GoldPriceUpload is not null ? 1 : 0) + (MarketHistoriesUpload is not null ? 1 : 0) + (BanditEventUpload is not null ? 1 : 0);

    public Upload(MarketUpload? marketUpload, GoldPriceUpload? goldPriceUpload, MarketHistoriesUpload? marketHistoriesUpload, BanditEventUpload? banditEventUpload = null)
    {
        MarketUpload = marketUpload;
        GoldPriceUpload = goldPriceUpload;
        MarketHistoriesUpload = marketHistoriesUpload;
        BanditEventUpload = banditEventUpload;
    }
}
