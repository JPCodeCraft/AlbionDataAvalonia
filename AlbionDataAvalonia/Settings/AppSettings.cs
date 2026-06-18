using System.Collections.Generic;

namespace AlbionDataAvalonia.Settings;

public class AppSettings
{
    public string? NPCapDownloadUrl { get; set; }
    public string? PacketFilterPortText { get; set; }
    public string? MarketOrdersIngestSubject { get; set; }
    public string? MarketHistoriesIngestSubject { get; set; }
    public string? GoldDataIngestSubject { get; set; }
    public string? BanditEventIngestSubject { get; set; }
    public string? LatestVersionUrl { get; set; }
    public string? LatesVersionDownloadUrl { get; set; }
    public string? FileNameFormat { get; set; }
    public double FirstUpdateCheckDelayMins { get; set; }
    public double UpdateCheckIntervalHours { get; set; }
    public double AppSettingsRetryLoadIntervalMins { get; set; }
    public int NetworkDevicesStartDelaySecs { get; set; }
    public int NetworkDevicesIdleMinutes { get; set; }
    public int NetworkDevicesIdleCheckMinutes { get; set; }

    public string AfmAuthClientId { get; set; } = string.Empty;
    public string AfmAuthRedirectUri { get; set; } = string.Empty;
    public string AfmAuthApiUrl { get; set; } = string.Empty;
    public string AfmTopItemsApiBase { get; set; } = string.Empty;
    public string AfmDataClientIngestApiBase { get; set; } = string.Empty;

    public List<string> ItemsToUploadToAfm { get; set; } = new List<string>();
}
