using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AlbionDataAvalonia.Activity;

public sealed record ActivityResourceIdentity(string? Family, int? Tier, int? Enchantment)
{
    public static ActivityResourceIdentity FromUniqueName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return new(null, null, null);
        var text = name.Trim().ToUpperInvariant();
        var separator = text.IndexOf('_');
        int? tier = separator > 1 && text[0] == 'T' && int.TryParse(text.AsSpan(1, separator - 1), out var parsedTier) ? parsedTier : null;
        var enchantmentIndex = text.LastIndexOf('@');
        int? enchantment = tier is null ? null : enchantmentIndex < 0 ? 0 :
            int.TryParse(text.AsSpan(enchantmentIndex + 1), out var parsedEnchantment) ? parsedEnchantment : null;
        var body = separator >= 0 ? text[(separator + 1)..] : text;
        var token = body.Split('_', '@')[0];
        var family = token switch
        {
            "WOOD" => "Wood",
            "ORE" => "Ore",
            "ROCK" => "Stone",
            "HIDE" => "Hide",
            "FIBER" => "Fiber",
            "FISH" => "Fish",
            "SEAWEED" => "Seaweed",
            _ => null
        };
        return new(family, tier, enchantment);
    }
}

public static class ActivityClassification
{
    public const int TagVersion = 6;
    public static bool IsResource(ActivityEventKind kind) => kind is ActivityEventKind.Gathered or ActivityEventKind.FishingReward or ActivityEventKind.LegacyGatheringAggregate;
    public static bool IsItem(ActivityEventKind kind) => kind == ActivityEventKind.Loot || IsResource(kind);
    public static bool IsParty(ActivityEventKind kind) => kind is ActivityEventKind.PartyJoined or ActivityEventKind.PartyLeft;
    public static bool IsTemporal(ActivityEventKind kind) => kind is not (ActivityEventKind.LegacyGatheringAggregate or ActivityEventKind.LegacyImportMarker);
    public static ActivityResourceMethod ResourceMethod(ActivityEvent value) => ResourceMethodValue(value);
    internal static readonly Expression<Func<ActivityEvent, ActivityResourceMethod>> ResourceMethodExpression = e =>
        e.Kind == ActivityEventKind.Gathered ? ActivityResourceMethod.Gathering :
        e.Kind == ActivityEventKind.FishingReward ? ActivityResourceMethod.Fishing :
        e.Kind == ActivityEventKind.LegacyGatheringAggregate ?
            (ActivityResourceMethod)(ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Method") ?? 0) : ActivityResourceMethod.Unknown;
    private static readonly Func<ActivityEvent, ActivityResourceMethod> ResourceMethodValue = ResourceMethodExpression.Compile();
    internal static readonly Expression<Func<ActivityEvent, long?>> ItemValueExpression = e =>
        e.Kind == ActivityEventKind.LegacyGatheringAggregate ? ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Legacy.RecordedTotalValue") :
        e.EstimatedUnitValue == null ? null : e.EstimatedUnitValue.Value * e.Quantity;
    private static readonly Func<ActivityEvent, long?> ItemValue = ItemValueExpression.Compile();
    public static long? GetItemValue(ActivityEvent value) => ItemValue(value);
    public static ActivityFameCategory GetFameCategory(ActivityEvent value)
    {
        if (value.Kind != ActivityEventKind.Fame) return ActivityFameCategory.Unknown;
        var category = ActivityDatabaseFunctions.JsonLong(value.DetailsJson, "$.FameCategory");
        return category is >= (long)ActivityFameCategory.Pve and <= (long)ActivityFameCategory.Pvp
            ? (ActivityFameCategory)category.Value : ActivityFameCategory.Unknown;
    }

    internal static Expression<Func<ActivityEvent, bool>> GatheringExpression(ActivityScope scope, string? actorKey = null)
    {
        var party = scope != ActivityScope.Personal;
        var all = scope == ActivityScope.AllObserved;
        return e => !e.IsRetracted && (e.Kind == ActivityEventKind.Gathered || e.Kind == ActivityEventKind.FishingReward || e.Kind == ActivityEventKind.LegacyGatheringAggregate) &&
            e.Quantity > 0 && e.Evidence == ActivityEvidence.Confirmed && (actorKey == null || e.SourceActorId == actorKey) &&
            (all || e.SourceRelationship == ActorRelationship.Self || party && e.SourceRelationship == ActorRelationship.Party);
    }
    internal static Expression<Func<ActivityEvent, bool>> CombatExpression(ActivityScope scope, string? actorKey = null)
    {
        var party = scope != ActivityScope.Personal;
        var all = scope == ActivityScope.AllObserved;
        return e => !e.IsRetracted && (e.Kind == ActivityEventKind.Damage || e.Kind == ActivityEventKind.Healing) &&
            (actorKey == null || e.SourceActorId == actorKey || e.TargetActorId == actorKey) &&
            (all || e.SourceRelationship == ActorRelationship.Self || e.TargetRelationship == ActorRelationship.Self ||
                party && (e.SourceRelationship == ActorRelationship.Party || e.TargetRelationship == ActorRelationship.Party));
    }
    internal static Expression<Func<ActivityEvent, bool>> PveExpression(ActivityScope scope, string? actorKey = null)
    {
        var party = scope != ActivityScope.Personal;
        var all = scope == ActivityScope.AllObserved;
        return e => !e.IsRetracted && (e.Kind == ActivityEventKind.MobKilled && actorKey == null && (all || e.ParticipatedSelf || party && e.ParticipatedParty) ||
            (e.Kind == ActivityEventKind.Damage && e.Amount > 0 || e.Kind == ActivityEventKind.Death && e.Evidence == ActivityEvidence.Confirmed) &&
            (e.SourceKind == ActivityActorKind.Mob || e.TargetKind == ActivityActorKind.Mob) &&
            (actorKey == null || e.SourceActorId == actorKey || e.TargetActorId == actorKey) &&
            (all || e.SourceRelationship == ActorRelationship.Self || e.TargetRelationship == ActorRelationship.Self ||
                party && (e.SourceRelationship == ActorRelationship.Party || e.TargetRelationship == ActorRelationship.Party)));
    }
    internal static Expression<Func<ActivityEvent, bool>> PvpExpression(ActivityScope scope, string? actorKey = null)
    {
        var party = scope != ActivityScope.Personal;
        var all = scope == ActivityScope.AllObserved;
        return e => !e.IsRetracted && (e.Kind == ActivityEventKind.Damage && e.Amount > 0 ||
                (e.Kind == ActivityEventKind.Death || e.Kind == ActivityEventKind.PlayerKilled) && e.Evidence == ActivityEvidence.Confirmed) &&
            e.SourceKind == ActivityActorKind.Player && e.TargetKind == ActivityActorKind.Player && e.SourceActorId != e.TargetActorId &&
            !((e.SourceRelationship == ActorRelationship.Self || e.SourceRelationship == ActorRelationship.Party) &&
                (e.TargetRelationship == ActorRelationship.Self || e.TargetRelationship == ActorRelationship.Party)) &&
            (actorKey == null || e.SourceActorId == actorKey || e.TargetActorId == actorKey) &&
            (all || e.SourceRelationship == ActorRelationship.Self || e.TargetRelationship == ActorRelationship.Self ||
                party && (e.SourceRelationship == ActorRelationship.Party || e.TargetRelationship == ActorRelationship.Party));
    }

    internal static IQueryable<ActivityEvent> FilterCategories(IQueryable<ActivityEvent> query, IQueryable<ActivityEvent> allEvents, ActivityQuery filter)
    {
        if (filter.ShowCategories == ActivityCategories.All) return query;
        if (filter.SessionId is { } sessionId) allEvents = allEvents.Where(e => e.SessionId == sessionId);
        if (filter.EncounterId is { } encounterId) allEvents = allEvents.Where(e => e.EncounterId == encounterId);
        // Encounter IDs are globally unique, and the writer enforces their session ownership.
        // Distinct sets avoid scanning an encounter's evidence again for every filtered event.
        var pve = allEvents.Where(PveExpression(filter.Scope, filter.ActorKey)).Where(e => e.EncounterId != null)
            .Select(e => e.EncounterId!.Value).Distinct();
        var pvp = allEvents.Where(PvpExpression(filter.Scope, filter.ActorKey)).Where(e => e.EncounterId != null)
            .Select(e => e.EncounterId!.Value).Distinct();
        return query.Where(CategoryExpression(filter.ShowCategories,
            e => e.EncounterId != null && pve.Contains(e.EncounterId.Value),
            e => e.EncounterId != null && pvp.Contains(e.EncounterId.Value)));
    }

    internal static Expression<Func<ActivityEvent, bool>> CategoryExpression(ActivityCategories show,
        IReadOnlyCollection<Guid> pveEncounters, IReadOnlyCollection<Guid> pvpEncounters) => CategoryExpression(show,
        e => e.EncounterId != null && pveEncounters.Contains(e.EncounterId.Value),
        e => e.EncounterId != null && pvpEncounters.Contains(e.EncounterId.Value));

    private static Expression<Func<ActivityEvent, bool>> CategoryExpression(ActivityCategories show,
        Expression<Func<ActivityEvent, bool>> pve, Expression<Func<ActivityEvent, bool>> pvp)
    {
        if (show == ActivityCategories.All) return e => true;
        var gathering = (show & ActivityCategories.Gathering) != 0;
        var fishing = (show & ActivityCategories.Fishing) != 0;
        var showPve = (show & ActivityCategories.Pve) != 0;
        var showPvp = (show & ActivityCategories.Pvp) != 0;
        var other = (show & ActivityCategories.Other) != 0;
        var loot = (show & ActivityCategories.Loot) != 0;
        var fame = (show & ActivityCategories.Fame) != 0;
        var silver = (show & ActivityCategories.Silver) != 0;
        var outcomes = (show & ActivityCategories.Outcomes) != 0;
        var combat = (show & ActivityCategories.Combat) != 0;
        Expression<Func<ActivityEvent, bool, bool, bool>> predicate = (e, isPve, isPvp) =>
            gathering && (e.Kind == ActivityEventKind.Gathered || e.Kind == ActivityEventKind.FishingReward || e.Kind == ActivityEventKind.LegacyGatheringAggregate ||
                e.Kind == ActivityEventKind.Fame && (ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)ActivityFameCategory.Gathering ||
                    ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)ActivityFameCategory.Fishing)) ||
            fishing && (e.Kind == ActivityEventKind.FishingReward || e.Kind == ActivityEventKind.LegacyGatheringAggregate &&
                ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Method") == (long)ActivityResourceMethod.Fishing ||
                e.Kind == ActivityEventKind.Fame && ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)ActivityFameCategory.Fishing) ||
            loot && e.Kind == ActivityEventKind.Loot || fame && e.Kind == ActivityEventKind.Fame || silver && e.Kind == ActivityEventKind.Silver ||
            outcomes && (e.Kind == ActivityEventKind.Death || e.Kind == ActivityEventKind.PlayerKilled || e.Kind == ActivityEventKind.MobKilled || e.Kind == ActivityEventKind.Knockdown) ||
            combat && (e.Kind == ActivityEventKind.Damage || e.Kind == ActivityEventKind.Healing) ||
            e.Kind == ActivityEventKind.Fame &&
                ((showPve || combat) && ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)ActivityFameCategory.Pve ||
                    (showPvp || combat) && ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)ActivityFameCategory.Pvp ||
                    other && (ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == null ||
                        ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") < (long)ActivityFameCategory.Pve ||
                        ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") > (long)ActivityFameCategory.Pvp)) ||
            (e.Kind == ActivityEventKind.Damage || e.Kind == ActivityEventKind.Healing || e.Kind == ActivityEventKind.Loot ||
                e.Kind == ActivityEventKind.Silver || e.Kind == ActivityEventKind.MobKilled ||
                e.Kind == ActivityEventKind.Death || e.Kind == ActivityEventKind.Knockdown || e.Kind == ActivityEventKind.PlayerKilled || e.Kind == ActivityEventKind.OutcomeEvidence) &&
                (showPve && isPve || showPvp && isPvp || other && !isPve && !isPvp) ||
            other && (e.Kind == ActivityEventKind.Location || e.Kind == ActivityEventKind.CaptureGap) ||
            // Party changes explain the other activity, so every category filter keeps them.
            e.Kind == ActivityEventKind.PartyJoined || e.Kind == ActivityEventKind.PartyLeft;
        var parameter = predicate.Parameters[0];
        var body = new ReplaceParameter(predicate.Parameters[1], new ReplaceParameter(pve.Parameters[0], parameter).Visit(pve.Body)!)
            .Visit(predicate.Body)!;
        body = new ReplaceParameter(predicate.Parameters[2], new ReplaceParameter(pvp.Parameters[0], parameter).Visit(pvp.Body)!).Visit(body)!;
        return Expression.Lambda<Func<ActivityEvent, bool>>(body, parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? replacement : base.VisitParameter(node);
    }
}

