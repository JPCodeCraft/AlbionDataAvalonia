using AlbionDataAvalonia.Network.Models;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Settings;

public class AppSettings
{
    public string? NPCapDownloadUrl { get; set; }
    public string? PacketFilterPortText { get; set; }
    public List<AlbionServer> AlbionServers { get; set; } = new List<AlbionServer>();
    public string? MarketOrdersIngestSubject { get; set; }
    public string? MarketHistoriesIngestSubject { get; set; }
    public string? GoldDataIngestSubject { get; set; }
    public string? LatestVersionUrl { get; set; }
    public string? LatesVersionDownloadUrl { get; set; }
    public string? FileNameFormat { get; set; }
    public double FirstUpdateCheckDelayMins { get; set; }
    public double UpdateCheckIntervalHours { get; set; }
    public double AppSettingsRetryLoadIntervalMins { get; set; }
    public string? AppDataFolderName { get; set; }
    public int AmountOfDailyFileLogsToKeep { get; set; }
}
