using AFMDataClient.Core;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.State.Events;
using System;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;

namespace AlbionDataAvalonia.Network.Services;

/// <summary>Maps desktop preferences and presentation state onto the shared client runtime.</summary>
public sealed class DesktopClientCore : IDisposable
{
    private readonly SettingsManager settings;
    private readonly PlayerState player;
    private readonly HttpClient publicClient;
    private readonly HttpClient afmClient;
    private readonly HttpClient backendClient;
    private ClientCoreOptions currentOptions;
    private bool synchronizingSession;

    public DesktopClientCore(SettingsManager settings, PlayerState player, DesktopUploadAuthSession auth)
    {
        this.settings = settings;
        this.player = player;
        publicClient = CreateHttpClient();
        afmClient = CreateHttpClient(new Uri(settings.AppSettings.AfmDataClientIngestApiBase));
        backendClient = CreateHttpClient(settings.AppSettings.GetAfmBackendApiBaseUri());
        currentOptions = CreateOptions();
        Core = new ClientCoreBuilder(currentOptions, auth, publicClient, afmClient, backendClient)
            .WithMarketOrders()
            .WithMarketHistory()
            .WithSpecs()
            .WithEstimatedMarketValues()
            .WithIslands()
            .WithGold()
            .WithWorldEvents()
            .Build();
        Core.Session.Changed += SynchronizeSession;
        Core.UploadResult += OnUploadResult;
        Core.PowSolved += OnPowSolved;
        settings.UserSettings.PropertyChanged += OnSettingsChanged;
        player.OnPlayerStateChanged += OnPlayerStateChanged;
    }

    public ClientCore Core { get; }

    private HttpClient CreateHttpClient(Uri? baseAddress = null)
    {
        var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"afmDataClient-v.{ClientUpdater.GetVersion() ?? "unknown"}");
        client.DefaultRequestHeaders.Referrer = new Uri("https://github.com/JPCodeCraft/AlbionDataAvalonia");
        return client;
    }

    private ClientCoreOptions CreateOptions() => new()
    {
        PrivateMarketOrders = player.UploadToAfmOnly,
        ContributeToPublic = player.ContributeToPublic,
        ShareWithFriends = player.ShareWithFriends,
        PublicItemFilters = settings.AppSettings.ItemsToUploadToAfm.ToArray(),
        UploadSpecs = settings.UserSettings.UploadSpecsToAfm,
        IslandTracking = settings.UserSettings.AfmIslandTrackerEnabled,
        DesiredConcurrency = settings.UserSettings.DesiredThreadCount,
        StorageDirectory = AppData.DataDirectoryPath,
        ClientIdentification = $"afmDataClient-v.{ClientUpdater.GetVersion() ?? "unknown"}",
        MarketOrdersIngestSubject = settings.AppSettings.MarketOrdersIngestSubject ?? string.Empty,
        MarketHistoriesIngestSubject = settings.AppSettings.MarketHistoriesIngestSubject ?? string.Empty,
        GoldDataIngestSubject = settings.AppSettings.GoldDataIngestSubject ?? string.Empty,
        BanditEventIngestSubject = settings.AppSettings.BanditEventIngestSubject ?? string.Empty
    };

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args) => UpdateCoreOptions();

    private void UpdateCoreOptions()
    {
        var options = CreateOptions();
        if (currentOptions.PublicItemFilters.SequenceEqual(options.PublicItemFilters))
            options = options with { PublicItemFilters = currentOptions.PublicItemFilters };
        if (options == currentOptions) return;
        currentOptions = options;
        Core.UpdateOptions(options);
    }

    private void OnPlayerStateChanged(object? sender, PlayerStateEventArgs args)
    {
        if (synchronizingSession) return;
        Core.Session.SetServer(player.AlbionServer);
        UpdateCoreOptions();
    }

    private void SynchronizeSession()
    {
        synchronizingSession = true;
        try
        {
            var session = Core.Session;
            player.AlbionServer = session.AlbionServer;
            player.UserObjectId = session.UserObjectId;
            player.PlayerName = session.PlayerName ?? string.Empty;
            var location = session.Location ?? AlbionDataAvalonia.Locations.AlbionLocations.Unset;
            if (!Equals(player.Location, location)) player.Location = location;
            if (session.HasPremium.HasValue) player.SetPremiumExpirationTicks(session.PremiumExpirationTicks);
            else player.ResetPremiumStatus();
        }
        finally
        {
            synchronizingSession = false;
        }
    }

    private void OnPowSolved(double milliseconds) => player.AddPowSolveTime((long)milliseconds);

    private void OnUploadResult(ClientUploadResult result)
    {
        // A skipped duplicate or invalidated session is not an attempted upload.
        if (result.Status == UploadStatus.Skipped) return;
        switch (result.Payload)
        {
            case MarketUpload market when result.Server is { } server:
                player.MarketUploadHandler(this, new MarketUploadEventArgs(market, server, result.Status, result.Scope));
                break;
            case MarketHistoriesUpload history when result.Server is { } server:
                player.MarketHistoryUploadHandler(this, new MarketHistoriesUploadEventArgs(history, server, result.Status, result.Scope));
                break;
            case GoldPriceUpload gold when result.Server is { } server:
                player.GoldPriceUploadHandler(this, new GoldPriceUploadEventArgs(gold, server, result.Status, result.Scope));
                break;
            case BanditEventUpload bandit when result.Server is { } server:
                player.BanditEventUploadHandler(this, new BanditEventUploadEventArgs(bandit, server, result.Status, result.Scope));
                break;
            case AchievementUpload specs:
                player.AchievementsUploadHandler(this, new AchievementsUploadEventArgs(specs, result.Status, result.Scope, result.Identifier));
                break;
            case GlobalMultiplierUpload multiplier:
                player.GlobalMultiplierUploadHandler(this, new GlobalMultiplierUploadEventArgs(multiplier, result.Status, result.Scope, result.Identifier));
                break;
            case FestivitiesUpload festivities:
                player.FestivitiesUploadHandler(this, new FestivitiesUploadEventArgs(festivities, result.Status, result.Scope, result.Identifier));
                break;
            case ItemEstimatedMarketValueUpload emv:
                player.ItemEstimatedMarketValueUploadHandler(this, new ItemEstimatedMarketValueUploadEventArgs(emv, result.Status, result.Scope, result.Identifier));
                break;
            default:
                if (result.Kind == "Islands") player.RecordIslandUpload(result.Status, result.Identifier);
                break;
        }
    }

    public void Dispose()
    {
        settings.UserSettings.PropertyChanged -= OnSettingsChanged;
        player.OnPlayerStateChanged -= OnPlayerStateChanged;
        Core.Session.Changed -= SynchronizeSession;
        Core.UploadResult -= OnUploadResult;
        Core.PowSolved -= OnPowSolved;
        Core.Dispose();
        publicClient.Dispose();
        afmClient.Dispose();
        backendClient.Dispose();
    }
}
