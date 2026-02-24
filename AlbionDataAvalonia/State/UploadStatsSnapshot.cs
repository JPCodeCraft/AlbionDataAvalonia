namespace AlbionDataAvalonia.State;

public class PublicUploadStatsSnapshot
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int MarketOffersCount { get; set; }
    public int MarketRequestsCount { get; set; }
    public int MonthlyHistoriesCount { get; set; }
    public int WeeklyHistoriesCount { get; set; }
    public int DailyHistoriesCount { get; set; }
    public int GoldHistoriesCount { get; set; }
    public int BanditEventsCount { get; set; }
}

public class PrivateUploadStatsSnapshot
{
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int MarketOffersCount { get; set; }
    public int MarketRequestsCount { get; set; }
    public int AchievementsCount { get; set; }
    public int GlobalMultipliersCount { get; set; }
}
