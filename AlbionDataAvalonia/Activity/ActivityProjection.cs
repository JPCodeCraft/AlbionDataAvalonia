using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;

namespace AlbionDataAvalonia.Activity;

public static class ActivityProjection
{
    private static readonly Dictionary<ActivityScope, Func<ActivityEvent, ActivitySummary>> Contributions =
        Enum.GetValues<ActivityScope>().ToDictionary(scope => scope, scope => ContributionExpression(scope).Compile());

    // The same expression is used for SQL aggregation and transactional/live contributions.
    public static Expression<Func<ActivityEvent, ActivitySummary>> ContributionExpression(ActivityScope scope, string? actorKey = null)
    {
        var party = scope != ActivityScope.Personal;
        var all = scope == ActivityScope.AllObserved;
        Expression<Func<ActivityEvent, ActivitySummary>> expression = e => new ActivitySummary
        {
            DamageDealt = !e.IsRetracted && e.Kind == ActivityEventKind.Damage &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.Amount : 0,
            DamageReceived = !e.IsRetracted && e.Kind == ActivityEventKind.Damage &&
                (actorKey == null || e.TargetActorId == actorKey) &&
                (all || e.TargetRelationship == ActorRelationship.Self || (party && e.TargetRelationship == ActorRelationship.Party)) ? e.Amount : 0,
            HealingDone = !e.IsRetracted && e.Kind == ActivityEventKind.Healing &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.Amount : 0,
            HealingReceived = !e.IsRetracted && e.Kind == ActivityEventKind.Healing &&
                (actorKey == null || e.TargetActorId == actorKey) &&
                (all || e.TargetRelationship == ActorRelationship.Self || (party && e.TargetRelationship == ActorRelationship.Party)) ? e.Amount : 0,
            Fame = !e.IsRetracted && e.Kind == ActivityEventKind.Fame && e.Evidence != ActivityEvidence.Supplemental &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.Amount : 0,
            SilverRaw = !e.IsRetracted && e.Kind == ActivityEventKind.Silver && e.Evidence == ActivityEvidence.Confirmed && e.HasKnownNetSilver &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.SilverRaw : 0,
            UnknownSilverEvents = !e.IsRetracted && e.Kind == ActivityEventKind.Silver && !e.HasKnownNetSilver && e.Evidence != ActivityEvidence.Supplemental &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? 1 : 0,
            // Everyone also counts items taken by an unknown player, such as from a public chest.
            LootValue = !e.IsRetracted && e.Kind == ActivityEventKind.Loot && (e.Evidence == ActivityEvidence.Confirmed || all && e.Evidence == ActivityEvidence.Inferred) && e.EstimatedUnitValue != null &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.EstimatedUnitValue.Value * e.Quantity : 0,
            LootItems = !e.IsRetracted && e.Kind == ActivityEventKind.Loot && (e.Evidence == ActivityEvidence.Confirmed || all && e.Evidence == ActivityEvidence.Inferred) &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.Quantity : 0,
            MissingPriceItems = !e.IsRetracted && e.Kind == ActivityEventKind.Loot && (e.Evidence == ActivityEvidence.Confirmed || all && e.Evidence == ActivityEvidence.Inferred) && e.EstimatedUnitValue == null &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.Quantity : 0,
            MobKills = !e.IsRetracted && e.Kind == ActivityEventKind.MobKilled &&
                (all || e.ParticipatedSelf || (party && e.ParticipatedParty)) ? 1 : 0,
            PlayerKills = !e.IsRetracted && (e.Kind == ActivityEventKind.PlayerKilled ||
                (e.Kind == ActivityEventKind.Death && e.SourceKind == ActivityActorKind.Player && e.TargetKind == ActivityActorKind.Player)) && e.Evidence == ActivityEvidence.Confirmed &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? 1 : 0,
            Deaths = !e.IsRetracted && e.Kind == ActivityEventKind.Death && e.Evidence == ActivityEvidence.Confirmed &&
                (actorKey == null || e.TargetActorId == actorKey) &&
                (all || e.TargetRelationship == ActorRelationship.Self || (party && e.TargetRelationship == ActorRelationship.Party)) ? 1 : 0,
            Knockdowns = !e.IsRetracted && e.Kind == ActivityEventKind.Knockdown && e.Evidence == ActivityEvidence.Confirmed &&
                (actorKey == null || e.TargetActorId == actorKey) &&
                (all || e.TargetRelationship == ActorRelationship.Self || (party && e.TargetRelationship == ActorRelationship.Party)) ? 1 : 0
        };
        return WithResourceContributions(expression, scope, actorKey);
    }

    private static Expression<Func<ActivityEvent, ActivitySummary>> WithResourceContributions(
        Expression<Func<ActivityEvent, ActivitySummary>> original, ActivityScope scope, string? actorKey)
    {
        var party = scope != ActivityScope.Personal;
        var all = scope == ActivityScope.AllObserved;
        Expression<Func<ActivityEvent, ActivitySummary>> resources = e => new ActivitySummary
        {
            GatheringItems = !e.IsRetracted && e.Evidence == ActivityEvidence.Confirmed &&
                (e.Kind == ActivityEventKind.Gathered || e.Kind == ActivityEventKind.LegacyGatheringAggregate && ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Method") == (long)ActivityResourceMethod.Gathering) &&
                (actorKey == null || e.SourceActorId == actorKey) && (all || e.SourceRelationship == ActorRelationship.Self || party && e.SourceRelationship == ActorRelationship.Party) ? e.Quantity : 0,
            FishingItems = !e.IsRetracted && e.Evidence == ActivityEvidence.Confirmed &&
                (e.Kind == ActivityEventKind.FishingReward || e.Kind == ActivityEventKind.LegacyGatheringAggregate && ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Method") == (long)ActivityResourceMethod.Fishing) &&
                (actorKey == null || e.SourceActorId == actorKey) && (all || e.SourceRelationship == ActorRelationship.Self || party && e.SourceRelationship == ActorRelationship.Party) ? e.Quantity : 0,
            UnclassifiedResourceItems = !e.IsRetracted && e.Evidence == ActivityEvidence.Confirmed && e.Kind == ActivityEventKind.LegacyGatheringAggregate &&
                (ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Method") == null || ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Method") == 0 || ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Method") == (long)ActivityResourceMethod.Mixed) &&
                (actorKey == null || e.SourceActorId == actorKey) && (all || e.SourceRelationship == ActorRelationship.Self || party && e.SourceRelationship == ActorRelationship.Party) ? e.Quantity : 0,
            MissingResourcePriceItems = !e.IsRetracted && e.Evidence == ActivityEvidence.Confirmed &&
                (e.Kind == ActivityEventKind.Gathered || e.Kind == ActivityEventKind.FishingReward || e.Kind == ActivityEventKind.LegacyGatheringAggregate) &&
                (e.Kind == ActivityEventKind.LegacyGatheringAggregate ? ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Legacy.RecordedTotalValue") == null : e.EstimatedUnitValue == null) &&
                (actorKey == null || e.SourceActorId == actorKey) && (all || e.SourceRelationship == ActorRelationship.Self || party && e.SourceRelationship == ActorRelationship.Party) ? e.Quantity : 0,
            LegacyAggregateEvents = !e.IsRetracted && e.Kind == ActivityEventKind.LegacyGatheringAggregate &&
                (all || e.SourceRelationship == ActorRelationship.Self || party && e.SourceRelationship == ActorRelationship.Party) ? 1 : 0
        };
        var parameter = original.Parameters[0];
        var bindings = ((MemberInitExpression)original.Body).Bindings.Cast<MemberAssignment>().ToList();
        foreach (var binding in ((MemberInitExpression)resources.Body).Bindings.Cast<MemberAssignment>())
            bindings.Add(Expression.Bind(binding.Member, new ParameterSubstitution(resources.Parameters[0], parameter).Visit(binding.Expression)!));
        AddFame(nameof(ActivitySummary.PveFame), ActivityFameCategory.Pve);
        AddFame(nameof(ActivitySummary.GatheringFame), ActivityFameCategory.Gathering);
        AddFame(nameof(ActivitySummary.FishingFame), ActivityFameCategory.Fishing);
        AddFame(nameof(ActivitySummary.PvpFame), ActivityFameCategory.Pvp);
        Expression<Func<ActivityEvent, bool>> unknownFame = e => ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == null ||
            ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") < (long)ActivityFameCategory.Pve ||
            ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") > (long)ActivityFameCategory.Pvp;
        bindings.Add(Expression.Bind(typeof(ActivitySummary).GetProperty(nameof(ActivitySummary.UnknownFameAmount))!, Expression.Convert(
            Expression.Condition(new ParameterSubstitution(unknownFame.Parameters[0], parameter).Visit(unknownFame.Body)!,
                bindings.Single(x => x.Member.Name == nameof(ActivitySummary.Fame)).Expression, Expression.Constant(0d)), typeof(double?))));
        AddCount(nameof(ActivitySummary.GatheringEvents), ActivityClassification.GatheringExpression(scope, actorKey));
        AddCount(nameof(ActivitySummary.PveEvents), ActivityClassification.PveExpression(scope, actorKey));
        AddCount(nameof(ActivitySummary.PvpEvents), ActivityClassification.PvpExpression(scope, actorKey));
        AddValue(nameof(ActivitySummary.GatheringValue), nameof(ActivitySummary.GatheringItems));
        AddValue(nameof(ActivitySummary.FishingValue), nameof(ActivitySummary.FishingItems));
        AddValue(nameof(ActivitySummary.UnclassifiedResourceValue), nameof(ActivitySummary.UnclassifiedResourceItems));
        AddNonLethal(nameof(ActivitySummary.NonLethalPlayerKills), nameof(ActivitySummary.PlayerKills));
        AddNonLethal(nameof(ActivitySummary.NonLethalDeaths), nameof(ActivitySummary.Deaths));
        AddQuickLearnFame(nameof(ActivitySummary.GatheringQuickLearnFame), ActivityFameCategory.Gathering, ActivityFameCategory.Fishing);
        AddQuickLearnFame(nameof(ActivitySummary.CombatQuickLearnFame), ActivityFameCategory.Pve, ActivityFameCategory.Pvp);
        return Expression.Lambda<Func<ActivityEvent, ActivitySummary>>(Expression.MemberInit(Expression.New(typeof(ActivitySummary)), bindings), parameter);

        void AddNonLethal(string property, string countProperty)
        {
            // Older death rows carry no flag and stay lethal.
            Expression<Func<ActivityEvent, bool>> nonLethal = e => ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.Lethal") == 0;
            var predicate = new ParameterSubstitution(nonLethal.Parameters[0], parameter).Visit(nonLethal.Body)!;
            var count = bindings.Single(x => x.Member.Name == countProperty).Expression;
            bindings.Add(Expression.Bind(typeof(ActivitySummary).GetProperty(property)!, Expression.Condition(predicate, count, Expression.Constant(0L))));
        }

        // A fame row whose award includes Quick Learn keeps the bonus in UPDATE_FAME key 12 (fixed point).
        void AddQuickLearnFame(string property, ActivityFameCategory first, ActivityFameCategory second)
        {
            Expression<Func<ActivityEvent, double>> bonus = e => ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.UsesEnhancedAward") == 1 &&
                (ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)first || ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)second)
                ? (ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.RawFixedPointComponents.12") ?? 0) / 10000d : 0d;
            var fame = bindings.Single(x => x.Member.Name == nameof(ActivitySummary.Fame)).Expression;
            bindings.Add(Expression.Bind(typeof(ActivitySummary).GetProperty(property)!, Expression.Condition(
                Expression.GreaterThan(fame, Expression.Constant(0d)),
                new ParameterSubstitution(bonus.Parameters[0], parameter).Visit(bonus.Body)!, Expression.Constant(0d))));
        }

        void AddCount(string property, Expression<Func<ActivityEvent, bool>> predicate) => bindings.Add(Expression.Bind(typeof(ActivitySummary).GetProperty(property)!,
            Expression.Condition(new ParameterSubstitution(predicate.Parameters[0], parameter).Visit(predicate.Body)!, Expression.Constant(1L), Expression.Constant(0L))));
        void AddFame(string property, ActivityFameCategory category)
        {
            Expression<Func<ActivityEvent, bool>> matches = e => ActivityDatabaseFunctions.JsonLong(e.DetailsJson, "$.FameCategory") == (long)category;
            var predicate = new ParameterSubstitution(matches.Parameters[0], parameter).Visit(matches.Body)!;
            var fame = bindings.Single(x => x.Member.Name == nameof(ActivitySummary.Fame)).Expression;
            bindings.Add(Expression.Bind(typeof(ActivitySummary).GetProperty(property)!, Expression.Condition(predicate, fame, Expression.Constant(0d))));
        }
        void AddValue(string property, string quantityProperty)
        {
            var eligible = Expression.GreaterThan(bindings.Single(x => x.Member.Name == quantityProperty).Expression, Expression.Constant(0L));
            var value = new ParameterSubstitution(ActivityClassification.ItemValueExpression.Parameters[0], parameter).Visit(ActivityClassification.ItemValueExpression.Body)!;
            bindings.Add(Expression.Bind(typeof(ActivitySummary).GetProperty(property)!, Expression.Condition(eligible,
                Expression.Coalesce(value, Expression.Constant(0L)), Expression.Constant(0L))));
        }
    }
    private sealed class ParameterSubstitution(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    { protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node); }

    public static ActivitySummary Contribution(ActivityEvent observation, ActivityScope scope) => Contributions[scope](observation);
    public static long? GetLootValue(ActivityEvent observation) => ActivityClassification.GetItemValue(observation);
    internal static Expression<Func<ActivityEvent, double>> ChartExpression(ActivityScope scope, string? actorKey, ActivityChartMetric metric)
    {
        var contribution = ContributionExpression(scope, actorKey);
        var property = metric == ActivityChartMetric.Silver ? nameof(ActivitySummary.SilverRaw) : metric.ToString();
        var bindings = ((MemberInitExpression)contribution.Body).Bindings.OfType<MemberAssignment>().ToArray();
        Expression metricValue = metric is ActivityChartMetric.ResourceItems or ActivityChartMetric.ResourceValue
            ? bindings.Where(x => x.Member.Name == (metric == ActivityChartMetric.ResourceItems ? nameof(ActivitySummary.GatheringItems) : nameof(ActivitySummary.GatheringValue)) ||
                x.Member.Name == (metric == ActivityChartMetric.ResourceItems ? nameof(ActivitySummary.FishingItems) : nameof(ActivitySummary.FishingValue)) ||
                x.Member.Name == (metric == ActivityChartMetric.ResourceItems ? nameof(ActivitySummary.UnclassifiedResourceItems) : nameof(ActivitySummary.UnclassifiedResourceValue)))
                .Select(x => x.Expression).Aggregate((a, b) => Expression.Add(a, b))
            : metric == ActivityChartMetric.CombatFame ? Sum(nameof(ActivitySummary.PveFame), nameof(ActivitySummary.PvpFame))
            : metric == ActivityChartMetric.ResourceFame ? Sum(nameof(ActivitySummary.GatheringFame), nameof(ActivitySummary.FishingFame))
            : metric == ActivityChartMetric.UnclassifiedFame ? Expression.Coalesce(
                bindings.Single(x => x.Member.Name == nameof(ActivitySummary.UnknownFameAmount)).Expression, Expression.Constant(0d))
            : bindings.Single(x => x.Member.Name == property).Expression;
        Expression value = metricValue.Type == typeof(double) ? metricValue : Expression.Convert(metricValue, typeof(double));
        if (metric == ActivityChartMetric.Silver) value = Expression.Divide(value, Expression.Constant(10000d));
        return Expression.Lambda<Func<ActivityEvent, double>>(value, contribution.Parameters);

        Expression Sum(params string[] properties) => bindings.Where(x => properties.Contains(x.Member.Name))
            .Select(x => x.Expression).Aggregate(Expression.Add);
    }
    internal static Expression<Func<ActivityEvent, long>> CountExpression(ActivityScope scope, string property, string? actorKey = null)
    {
        var contribution = ContributionExpression(scope, actorKey);
        var bindings = ((MemberInitExpression)contribution.Body).Bindings.OfType<MemberAssignment>().ToArray();
        Expression value = property == nameof(ActivitySummary.ResourceValue)
            ? bindings.Where(x => x.Member.Name == nameof(ActivitySummary.GatheringValue) || x.Member.Name == nameof(ActivitySummary.FishingValue) ||
                x.Member.Name == nameof(ActivitySummary.UnclassifiedResourceValue)).Select(x => x.Expression).Aggregate(Expression.Add)
            : bindings.Single(x => x.Member.Name == property).Expression;
        return Expression.Lambda<Func<ActivityEvent, long>>(value, contribution.Parameters);
    }

    public static ActivitySummary Add(ActivitySummary a, ActivitySummary b, int sign = 1) => a with
    {
        GatheringEvents = a.GatheringEvents + sign * b.GatheringEvents,
        PveEvents = a.PveEvents + sign * b.PveEvents,
        PvpEvents = a.PvpEvents + sign * b.PvpEvents,
        GatheringItems = a.GatheringItems + sign * b.GatheringItems,
        FishingItems = a.FishingItems + sign * b.FishingItems,
        UnclassifiedResourceItems = a.UnclassifiedResourceItems + sign * b.UnclassifiedResourceItems,
        GatheringValue = a.GatheringValue + sign * b.GatheringValue,
        FishingValue = a.FishingValue + sign * b.FishingValue,
        UnclassifiedResourceValue = a.UnclassifiedResourceValue + sign * b.UnclassifiedResourceValue,
        MissingResourcePriceItems = a.MissingResourcePriceItems + sign * b.MissingResourcePriceItems,
        LegacyAggregateEvents = a.LegacyAggregateEvents + sign * b.LegacyAggregateEvents,
        DamageDealt = a.DamageDealt + sign * b.DamageDealt,
        DamageReceived = a.DamageReceived + sign * b.DamageReceived,
        HealingDone = a.HealingDone + sign * b.HealingDone,
        HealingReceived = a.HealingReceived + sign * b.HealingReceived,
        Fame = a.Fame + sign * b.Fame,
        PveFame = a.PveFame + sign * b.PveFame,
        GatheringFame = a.GatheringFame + sign * b.GatheringFame,
        FishingFame = a.FishingFame + sign * b.FishingFame,
        PvpFame = a.PvpFame + sign * b.PvpFame,
        UnknownFameAmount = a.UnclassifiedFame + sign * b.UnclassifiedFame,
        SilverRaw = a.SilverRaw + sign * b.SilverRaw,
        UnknownSilverEvents = a.UnknownSilverEvents + sign * b.UnknownSilverEvents,
        LootValue = a.LootValue + sign * b.LootValue,
        LootItems = a.LootItems + sign * b.LootItems,
        MissingPriceItems = a.MissingPriceItems + sign * b.MissingPriceItems,
        MobKills = a.MobKills + sign * b.MobKills,
        PlayerKills = a.PlayerKills + sign * b.PlayerKills,
        Deaths = a.Deaths + sign * b.Deaths,
        Knockdowns = a.Knockdowns + sign * b.Knockdowns,
        NonLethalPlayerKills = a.NonLethalPlayerKills + sign * b.NonLethalPlayerKills,
        NonLethalDeaths = a.NonLethalDeaths + sign * b.NonLethalDeaths,
        GatheringQuickLearnFame = a.GatheringQuickLearnFame + sign * b.GatheringQuickLearnFame,
        CombatQuickLearnFame = a.CombatQuickLearnFame + sign * b.CombatQuickLearnFame
    };
    public static string Serialize(ActivitySummary summary) => JsonSerializer.Serialize(summary);
    public static ActivitySummary Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<ActivitySummary>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
    public static ActivitySummary GetSummary(ActivitySession session, ActivityScope scope) => Deserialize(scope switch
    {
        ActivityScope.Personal => session.PersonalSummaryJson,
        ActivityScope.Party => session.PartySummaryJson,
        _ => session.ObservedSummaryJson
    });
    public static ActivitySummary GetSummary(ActivityEncounter encounter, ActivityScope scope) => Deserialize(scope switch
    {
        ActivityScope.Personal => encounter.PersonalSummaryJson,
        ActivityScope.Party => encounter.PartySummaryJson,
        _ => encounter.ObservedSummaryJson
    });
    public static double GetDurationSeconds(ActivitySession session, DateTime? nowUtc = null) => Math.Max(0,
        session.ObservedDurationSeconds + (session.State == ActivityState.Open && session.ObservationStartedAtUtc is { } started
            ? ((nowUtc ?? session.LastObservedAtUtc) - started).TotalSeconds : 0));
    public static ActivityRates GetRates(ActivitySummary summary, ActivitySession session, double combatDurationSeconds, DateTime? nowUtc = null) =>
        GetRates(summary, GetDurationSeconds(session, nowUtc), combatDurationSeconds);
    public static ActivityRates GetRates(ActivitySummary summary, ActivityEncounter encounter) => GetRates(summary, encounter.CombatDurationSeconds);
    public static double GetFameRate(double amount, ActivityRates rates) => amount * 3600 / Math.Max(1, rates.DurationSeconds);
    public static ActivityRates GetRates(ActivitySummary summary, double durationSeconds, double? combatDurationSeconds = null)
    {
        var denominator = Math.Max(1, durationSeconds);
        var combatSeconds = Math.Max(0, combatDurationSeconds ?? durationSeconds);
        var combatDenominator = Math.Max(1, combatSeconds);
        return new(durationSeconds, combatSeconds > 0 ? summary.DamageDealt / combatDenominator : 0,
            combatSeconds > 0 ? summary.HealingDone / combatDenominator : 0,
            summary.Fame * 3600 / denominator, summary.Silver * 3600 / denominator, summary.LootValue * 3600d / denominator,
            combatSeconds > 0 ? summary.DamageReceived / combatDenominator : 0,
            combatSeconds > 0 ? summary.HealingReceived / combatDenominator : 0, combatSeconds,
            summary.ResourceItems * 3600d / denominator, summary.ResourceValue * 3600d / denominator);
    }
}
