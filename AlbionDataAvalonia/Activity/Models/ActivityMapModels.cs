using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Activity.Models;

public enum ActivityMapMarkerKind { Kill, Loot, Gathering, Death }

/// <summary>
/// Something the local character or a party member did, placed where the local character stood. Subject is the
/// mob, player or item. For items, Title is only who did what ("Looted"), and ItemUniqueName, Quality, Quantity and
/// Value (total EMV, null without a price) let the map show and total them. A mob kill names the mob for its portrait:
/// its unique name, or its name when the kill didn't keep one.
/// </summary>
public sealed record ActivityMapMarker(ActivityMapMarkerKind Kind, string RawMapId, DateTime CapturedAtUtc, double X, double Y,
    string Title, string Detail, bool IsOwn = true, Guid EventId = default, string Subject = "", long Quantity = 0, long? Value = null,
    string ItemUniqueName = "", int Quality = 1, string MobUniqueName = "")
{
    // Positions are always the local character's, so a party member's event is placed where this
    // character stood when the client saw it. IsOwn lets the map show only the character's own events.
    public static ActivityMapMarker? From(ActivityEvent value)
    {
        if (value.IsRetracted || value.PositionX is not { } x || value.PositionY is not { } y) return null;
        var self = value.SourceRelationship == ActorRelationship.Self;
        var party = value.SourceRelationship == ActorRelationship.Party;
        var targetSelf = value.TargetRelationship == ActorRelationship.Self;
        var targetParty = value.TargetRelationship == ActorRelationship.Party;
        var confirmed = value.Evidence == ActivityEvidence.Confirmed;
        var source = Name(value.SourceName, "A party member");
        var target = Name(value.TargetName, "A party member");
        var item = Name(value.ItemName, "an item");
        (ActivityMapMarkerKind Kind, string Title, string Detail, bool Own, string Subject)? marker = value.Kind switch
        {
            ActivityEventKind.MobKilled when value.ParticipatedSelf || value.ParticipatedParty =>
                (ActivityMapMarkerKind.Kill, value.ParticipatedSelf ? $"Killed {Name(value.TargetName, "a mob")}" : $"Party killed {Name(value.TargetName, "a mob")}",
                    "", value.ParticipatedSelf, Name(value.TargetName, "Unknown mob")),
            ActivityEventKind.PlayerKilled when (self || party) && confirmed =>
                (ActivityMapMarkerKind.Kill, self ? $"Killed {Name(value.TargetName, "a player")}" : $"{source} killed {Name(value.TargetName, "a player")}",
                    Guild(value.TargetGuild, value.TargetAlliance), self, Name(value.TargetName, "Unknown player")),
            ActivityEventKind.Death when (targetSelf || targetParty) && confirmed =>
                (ActivityMapMarkerKind.Death, targetSelf ? "You died" : $"{target} died", By(value.SourceName), targetSelf, target),
            ActivityEventKind.Knockdown when (targetSelf || targetParty) && confirmed =>
                (ActivityMapMarkerKind.Death, targetSelf ? "Knocked down" : $"{target} was knocked down", By(value.SourceName), targetSelf, target),
            ActivityEventKind.Loot when (self || party) && confirmed =>
                (ActivityMapMarkerKind.Loot, self ? "Looted" : $"{source} looted", From(value.LootSourceName), self, item),
            ActivityEventKind.Gathered when self || party =>
                (ActivityMapMarkerKind.Gathering, self ? "Gathered" : $"{source} gathered", "", self, item),
            ActivityEventKind.FishingReward when self || party =>
                (ActivityMapMarkerKind.Gathering, self ? "Caught" : $"{source} caught", "", self, item),
            _ => null
        };
        if (marker is not { } found) return null;
        var hasItems = found.Kind is ActivityMapMarkerKind.Loot or ActivityMapMarkerKind.Gathering;
        return new(found.Kind, value.RawMapId, value.CapturedAtUtc, x, y, found.Title, found.Detail, found.Own, value.Id, found.Subject,
            hasItems ? Math.Max(0, value.Quantity) : 0, hasItems ? TotalValue(value) : null,
            hasItems ? value.ItemUniqueName : "", value.Quality is >= 1 and <= 5 ? value.Quality.Value : 1,
            value.Kind != ActivityEventKind.MobKilled ? "" : string.IsNullOrWhiteSpace(value.MobUniqueName) ? value.TargetName : value.MobUniqueName);
    }

    private static string Name(string name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name;
    private static string By(string name) => string.IsNullOrWhiteSpace(name) ? "" : $"by {name}";
    private static string From(string name) => string.IsNullOrWhiteSpace(name) ? "" : $"from {name}";
    // A filled journal's price is already the full journal minus the empty one.
    private static long? TotalValue(ActivityEvent value) => value.EstimatedUnitValue is { } price && value.Quantity >= 0 &&
        (value.Quantity == 0 || price <= long.MaxValue / value.Quantity) ? price * value.Quantity : null;
    private static string Guild(string guild, string alliance) =>
        string.IsNullOrWhiteSpace(alliance) ? guild : string.IsNullOrWhiteSpace(guild) ? $"[{alliance}]" : $"[{alliance}] {guild}";
}

/// <summary>The current map as the live map window shows it: where the character is, where it walked, and what it did.</summary>
public sealed record ActivityLiveMap(long Version, string RawMapId, string MapName, ActivityPathPoint? Position, double? Heading,
    IReadOnlyList<ActivityPathPoint> Trail, IReadOnlyList<ActivityMapMarker> Markers, string? Layout = null, int? LayoutTurns = null)
{
    public static ActivityLiveMap Empty { get; } = new(0, "", "", null, null, [], []);
}

/// <summary>A recorded run with its path and placed events, for the history map.</summary>
public sealed record ActivityRunMap(ActivitySession Run, IReadOnlyList<ActivityPathSegment> Segments, IReadOnlyList<ActivityMapMarker> Markers);
