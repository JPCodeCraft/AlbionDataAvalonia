using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlbionDataAvalonia.Activity.Models;

public enum ActivityMapMarkerKind { Kill, Loot, Gathering, Death }

/// <summary>Something the local character did at a spot on a map.</summary>
public sealed record ActivityMapMarker(ActivityMapMarkerKind Kind, string RawMapId, DateTime CapturedAtUtc, double X, double Y,
    string Title, string Detail)
{
    // Positions are always the local character's, so only its own kills, pickups and deaths go on the map;
    // a party member's pickup would otherwise appear where this character stood.
    public static ActivityMapMarker? From(ActivityEvent value)
    {
        if (value.IsRetracted || value.PositionX is not { } x || value.PositionY is not { } y) return null;
        var self = value.SourceRelationship == ActorRelationship.Self;
        var confirmed = value.Evidence == ActivityEvidence.Confirmed;
        (ActivityMapMarkerKind Kind, string Title, string Detail)? marker = value.Kind switch
        {
            ActivityEventKind.MobKilled when value.ParticipatedSelf =>
                (ActivityMapMarkerKind.Kill, $"Killed {Name(value.TargetName, "a mob")}", ""),
            ActivityEventKind.PlayerKilled when self && confirmed =>
                (ActivityMapMarkerKind.Kill, $"Killed {Name(value.TargetName, "a player")}", Guild(value.TargetGuild, value.TargetAlliance)),
            ActivityEventKind.Death when value.TargetRelationship == ActorRelationship.Self && confirmed =>
                (ActivityMapMarkerKind.Death, "You died", By(value.SourceName)),
            ActivityEventKind.Knockdown when value.TargetRelationship == ActorRelationship.Self && confirmed =>
                (ActivityMapMarkerKind.Death, "Knocked down", By(value.SourceName)),
            ActivityEventKind.Loot when self && confirmed =>
                (ActivityMapMarkerKind.Loot, $"Looted {Items(value)}", Join(From(value.LootSourceName), Value(value))),
            ActivityEventKind.Gathered when self =>
                (ActivityMapMarkerKind.Gathering, $"Gathered {Items(value)}", Value(value)),
            ActivityEventKind.FishingReward when self =>
                (ActivityMapMarkerKind.Gathering, $"Caught {Items(value)}", Value(value)),
            _ => null
        };
        return marker is { } found ? new(found.Kind, value.RawMapId, value.CapturedAtUtc, x, y, found.Title, found.Detail) : null;
    }

    private static string Name(string name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name;
    private static string By(string name) => string.IsNullOrWhiteSpace(name) ? "" : $"by {name}";
    private static string From(string name) => string.IsNullOrWhiteSpace(name) ? "" : $"from {name}";
    private static string Items(ActivityEvent value) =>
        $"{(value.Quantity > 1 ? $"{value.Quantity.ToString("N0", CultureInfo.CurrentCulture)}× " : "")}{Name(value.ItemName, "an item")}";
    private static string Value(ActivityEvent value) => value.EstimatedUnitValue is { } price && price > 0 && value.Quantity > 0
        ? $"about {(price * value.Quantity).ToString("N0", CultureInfo.CurrentCulture)} silver" : "";
    private static string Guild(string guild, string alliance) =>
        string.IsNullOrWhiteSpace(alliance) ? guild : string.IsNullOrWhiteSpace(guild) ? $"[{alliance}]" : $"[{alliance}] {guild}";
    private static string Join(string first, string second) =>
        string.IsNullOrEmpty(first) ? second : string.IsNullOrEmpty(second) ? first : $"{first} · {second}";
}

/// <summary>The current map as the live map window shows it: where the character is, where it walked, and what it did.</summary>
public sealed record ActivityLiveMap(long Version, string RawMapId, string MapName, ActivityPathPoint? Position, double? Heading,
    IReadOnlyList<ActivityPathPoint> Trail, IReadOnlyList<ActivityMapMarker> Markers)
{
    public static ActivityLiveMap Empty { get; } = new(0, "", "", null, null, [], []);
}

/// <summary>A recorded run with its path and placed events, for the history map.</summary>
public sealed record ActivityRunMap(ActivitySession Run, IReadOnlyList<ActivityPathSegment> Segments, IReadOnlyList<ActivityMapMarker> Markers);
