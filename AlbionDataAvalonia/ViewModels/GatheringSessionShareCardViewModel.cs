using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Network.Models;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlbionDataAvalonia.ViewModels;

public sealed class GatheringSessionShareCardViewModel
{
    private static readonly CultureInfo ShareCulture = CultureInfo.GetCultureInfo("en-US");

    public GatheringSessionShareCardViewModel(
        GatheringCompletedSessionRowViewModel session,
        IReadOnlyList<GatheringSessionShareItemViewModel> topItems,
        Bitmap? logo)
    {
        SessionId = session.Id;
        StartedAtUtc = session.StartedAtUtc;
        EndedAtUtc = session.EndedAtUtc;
        ActiveElapsed = session.ActiveElapsed;
        TotalAmount = session.TotalAmount;
        TotalEstimatedMarketValue = session.TotalEstimatedMarketValue;
        SilverPerHour = session.SilverPerHour;
        AlbionServerId = session.AlbionServerId;
        PlayerName = session.PlayerName;
        Source = session.Source;
        TopItems = topItems;
        Logo = logo;
    }

    public Guid SessionId { get; }
    public DateTime StartedAtUtc { get; }
    public DateTime EndedAtUtc { get; }
    public TimeSpan ActiveElapsed { get; }
    public long TotalAmount { get; }
    public long TotalEstimatedMarketValue { get; }
    public long SilverPerHour { get; }
    public int? AlbionServerId { get; }
    public string PlayerName { get; }
    public GatheringSessionSource Source { get; }
    public IReadOnlyList<GatheringSessionShareItemViewModel> TopItems { get; }
    public Bitmap? Logo { get; }
    public bool HasLogo => Logo is not null;
    public bool ShowLogoPlaceholder => Logo is null;

    public string TitleText
    {
        get
        {
            var dayText = StartedAtUtc.ToLocalTime().ToString("MMM d", ShareCulture);
            var activityText = Source switch
            {
                GatheringSessionSource.Fishing => "Fishing",
                GatheringSessionSource.Mixed => "Gathering & Fishing",
                _ => "Gathering"
            };

            return $"{dayText} {activityText}";
        }
    }

    public string DateRangeText
    {
        get
        {
            var timeRangeText = $"{StartedAtUtc.ToLocalTime().ToString("h:mm tt", ShareCulture)} - {EndedAtUtc.ToLocalTime().ToString("h:mm tt", ShareCulture)}";
            var parts = new List<string>();
            if (GetAlbionServerName() is { } albionServerName)
            {
                parts.Add(albionServerName);
            }

            if (!string.IsNullOrWhiteSpace(PlayerName))
            {
                parts.Add(PlayerName);
            }

            parts.Add(timeRangeText);
            return string.Join(" | ", parts);
        }
    }

    private string? GetAlbionServerName()
    {
        if (AlbionServerId is null)
        {
            return null;
        }

        return AlbionServers.Get(AlbionServerId.Value)?.Name ?? $"Server {AlbionServerId.Value}";
    }

    public string TotalAmountText => TotalAmount.ToString("N0", CultureInfo.CurrentCulture);
    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue.ToString("N0", CultureInfo.CurrentCulture);
    public string SilverPerHourText => SilverPerHour.ToString("N0", CultureInfo.CurrentCulture);
    public string ActiveElapsedText => ActiveElapsed.TotalHours >= 1
        ? $"{(int)ActiveElapsed.TotalHours:00}:{ActiveElapsed.Minutes:00}:{ActiveElapsed.Seconds:00}"
        : $"{ActiveElapsed.Minutes:00}:{ActiveElapsed.Seconds:00}";
}

public sealed class GatheringSessionShareItemViewModel
{
    public GatheringSessionShareItemViewModel(GatheringHistoryItemRowViewModel row)
    {
        ItemName = row.ItemName;
        Amount = row.Amount;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        ItemImage = row.ItemImage;
        OverflowText = string.Empty;
    }

    private GatheringSessionShareItemViewModel(int hiddenItemCount)
    {
        ItemName = string.Empty;
        Amount = 0;
        TotalEstimatedMarketValue = null;
        ItemImage = null;
        IsOverflowIndicator = true;
        OverflowText = $"+{hiddenItemCount.ToString("N0", CultureInfo.CurrentCulture)} items";
    }

    public static GatheringSessionShareItemViewModel CreateOverflow(int hiddenItemCount) => new(hiddenItemCount);

    public string ItemName { get; }
    public long Amount { get; }
    public long? TotalEstimatedMarketValue { get; }
    public Bitmap? ItemImage { get; }
    public bool IsOverflowIndicator { get; }
    public bool IsRegularItem => !IsOverflowIndicator;
    public string OverflowText { get; }
    public bool HasItemImage => IsRegularItem && ItemImage is not null;
    public bool ShowItemImagePlaceholder => IsRegularItem && ItemImage is null;
    public string AmountText => Amount.ToString("N0", CultureInfo.CurrentCulture);
    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue is null
        ? "-"
        : TotalEstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
}
