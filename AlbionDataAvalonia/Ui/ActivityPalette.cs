using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Media;

namespace AlbionDataAvalonia.Ui;

/// <summary>
/// The colors for kinds of activity and data, shared by the live timeline, the map, its legend and the storage bar.
/// They come from the tokens, so a kind has one color everywhere.
/// </summary>
public static class ActivityPalette
{
    public static Color Damage => UiPalette.Color("UiActivityDamageColor");
    public static Color Healing => UiPalette.Color("UiActivityHealingColor");
    public static Color Kill => UiPalette.Color("UiActivityKillColor");
    public static Color Loot => UiPalette.Color("UiActivityLootColor");
    public static Color Gathering => UiPalette.Color("UiActivityGatheringColor");
    public static Color Fishing => UiPalette.Color("UiActivityFishingColor");
    public static Color Fame => UiPalette.Color("UiActivityFameColor");
    public static Color Silver => UiPalette.Color("UiActivitySilverColor");
    public static Color Death => UiPalette.Color("UiActivityDeathColor");
    public static Color Party => UiPalette.Color("UiActivityPartyColor");
    public static Color Pvp => UiPalette.Color("UiActivityPvpColor");
    public static Color Mixed => UiPalette.Color("UiActivityMixedColor");
    public static Color Unknown => UiPalette.Color("UiActivityUnknownColor");
    public static Color PathStart => UiPalette.Color("UiMapPathStartColor");
    public static Color PathEnd => UiPalette.Color("UiMapPathEndColor");

    public static Color Marker(ActivityLiveTimelineMarkerKind kind) => kind switch
    {
        ActivityLiveTimelineMarkerKind.Death or ActivityLiveTimelineMarkerKind.Knockdown => Death,
        ActivityLiveTimelineMarkerKind.PlayerKill => Kill,
        ActivityLiveTimelineMarkerKind.Loot => Loot,
        ActivityLiveTimelineMarkerKind.Gathering => Gathering,
        ActivityLiveTimelineMarkerKind.Fishing => Fishing,
        ActivityLiveTimelineMarkerKind.Fame => Fame,
        ActivityLiveTimelineMarkerKind.Silver => Silver,
        ActivityLiveTimelineMarkerKind.Party => Party,
        _ => Fishing
    };

    public static Color Encounter(ActivityEncounterKind kind) => kind switch
    {
        ActivityEncounterKind.Pvp => Pvp,
        ActivityEncounterKind.Mixed => Mixed,
        ActivityEncounterKind.Healing => Healing,
        _ => Damage
    };

    public static Color MapMarker(ActivityMapMarkerKind kind) => kind switch
    {
        ActivityMapMarkerKind.Kill => Kill,
        ActivityMapMarkerKind.Loot => Loot,
        ActivityMapMarkerKind.Gathering => Gathering,
        _ => Death
    };

    public static Color Zone(MapZoneKind kind) => UiPalette.Color(kind switch
    {
        MapZoneKind.Blue => "UiZoneBlueColor",
        MapZoneKind.Yellow => "UiZoneYellowColor",
        MapZoneKind.Red => "UiZoneRedColor",
        MapZoneKind.Black => "UiZoneBlackColor",
        _ => "UiZoneOtherColor"
    });

    /// <summary>A kind of stored data; null is everything else in the database.</summary>
    public static Color Storage(DataCleanupType? type) => UiPalette.Color(type switch
    {
        DataCleanupType.Activity => "UiStorageActivityColor",
        DataCleanupType.Trades => "UiStorageTradesColor",
        DataCleanupType.Mails => "UiStorageMailsColor",
        DataCleanupType.AwakenedItems => "UiStorageAwakenedColor",
        _ => "UiStorageOtherColor"
    });
}
