using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlbionDataAvalonia.Activity.Models;

public enum ActivityMapMarkerKind { Kill, Loot, Gathering, Death }

/// <summary>Something the local character or a party member did, placed where the local character stood.</summary>
public sealed record ActivityMapMarker(ActivityMapMarkerKind Kind, string RawMapId, DateTime CapturedAtUtc, double X, double Y,
    string Title, string Detail, bool IsOwn = true)
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
        (ActivityMapMarkerKind Kind, string Title, string Detail, bool Own)? marker = value.Kind switch
        {
            ActivityEventKind.MobKilled when value.ParticipatedSelf || value.ParticipatedParty =>
                (ActivityMapMarkerKind.Kill, value.ParticipatedSelf ? $"Killed {Name(value.TargetName, "a mob")}" : $"Party killed {Name(value.TargetName, "a mob")}",
                    "", value.ParticipatedSelf),
            ActivityEventKind.PlayerKilled when (self || party) && confirmed =>
                (ActivityMapMarkerKind.Kill, self ? $"Killed {Name(value.TargetName, "a player")}" : $"{source} killed {Name(value.TargetName, "a player")}",
                    Guild(value.TargetGuild, value.TargetAlliance), self),
            ActivityEventKind.Death when (targetSelf || targetParty) && confirmed =>
                (ActivityMapMarkerKind.Death, targetSelf ? "You died" : $"{target} died", By(value.SourceName), targetSelf),
            ActivityEventKind.Knockdown when (targetSelf || targetParty) && confirmed =>
                (ActivityMapMarkerKind.Death, targetSelf ? "Knocked down" : $"{target} was knocked down", By(value.SourceName), targetSelf),
            ActivityEventKind.Loot when (self || party) && confirmed =>
                (ActivityMapMarkerKind.Loot, self ? $"Looted {Items(value)}" : $"{source} looted {Items(value)}",
                    Join(From(value.LootSourceName), Value(value)), self),
            ActivityEventKind.Gathered when self || party =>
                (ActivityMapMarkerKind.Gathering, self ? $"Gathered {Items(value)}" : $"{source} gathered {Items(value)}", Value(value), self),
            ActivityEventKind.FishingReward when self || party =>
                (ActivityMapMarkerKind.Gathering, self ? $"Caught {Items(value)}" : $"{source} caught {Items(value)}", Value(value), self),
            _ => null
        };
        return marker is { } found ? new(found.Kind, value.RawMapId, value.CapturedAtUtc, x, y, found.Title, found.Detail, found.Own) : null;
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
