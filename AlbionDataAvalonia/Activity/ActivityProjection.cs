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
        return e => new ActivitySummary
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
            LootValue = !e.IsRetracted && e.Kind == ActivityEventKind.Loot && e.Evidence == ActivityEvidence.Confirmed && e.EstimatedUnitValue != null &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.EstimatedUnitValue.Value * e.Quantity : 0,
            LootItems = !e.IsRetracted && e.Kind == ActivityEventKind.Loot && e.Evidence == ActivityEvidence.Confirmed &&
                (actorKey == null || e.SourceActorId == actorKey) &&
                (all || e.SourceRelationship == ActorRelationship.Self || (party && e.SourceRelationship == ActorRelationship.Party)) ? e.Quantity : 0,
            MissingPriceItems = !e.IsRetracted && e.Kind == ActivityEventKind.Loot && e.Evidence == ActivityEvidence.Confirmed && e.EstimatedUnitValue == null &&
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
    }

    public static ActivitySummary Contribution(ActivityEvent observation, ActivityScope scope) => Contributions[scope](observation);
    public static long? GetLootValue(ActivityEvent observation) => observation.EstimatedUnitValue is { } price ? price * observation.Quantity : null;
    internal static Expression<Func<ActivityEvent, double>> ChartExpression(ActivityScope scope, string? actorKey, ActivityChartMetric metric)
    {
        var contribution = ContributionExpression(scope, actorKey);
        var property = metric == ActivityChartMetric.Silver ? nameof(ActivitySummary.SilverRaw) : metric.ToString();
        var binding = ((MemberInitExpression)contribution.Body).Bindings.OfType<MemberAssignment>().Single(x => x.Member.Name == property);
        Expression value = binding.Expression.Type == typeof(double) ? binding.Expression : Expression.Convert(binding.Expression, typeof(double));
        if (metric == ActivityChartMetric.Silver) value = Expression.Divide(value, Expression.Constant(10000d));
        return Expression.Lambda<Func<ActivityEvent, double>>(value, contribution.Parameters);
    }
    public static ActivitySummary Add(ActivitySummary a, ActivitySummary b, int sign = 1) => a with
    {
        DamageDealt = a.DamageDealt + sign * b.DamageDealt,
        DamageReceived = a.DamageReceived + sign * b.DamageReceived,
        HealingDone = a.HealingDone + sign * b.HealingDone,
        HealingReceived = a.HealingReceived + sign * b.HealingReceived,
        Fame = a.Fame + sign * b.Fame,
        SilverRaw = a.SilverRaw + sign * b.SilverRaw,
        UnknownSilverEvents = a.UnknownSilverEvents + sign * b.UnknownSilverEvents,
        LootValue = a.LootValue + sign * b.LootValue,
        LootItems = a.LootItems + sign * b.LootItems,
        MissingPriceItems = a.MissingPriceItems + sign * b.MissingPriceItems,
        MobKills = a.MobKills + sign * b.MobKills,
        PlayerKills = a.PlayerKills + sign * b.PlayerKills,
        Deaths = a.Deaths + sign * b.Deaths,
        Knockdowns = a.Knockdowns + sign * b.Knockdowns
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
    public static ActivityRates GetRates(ActivitySummary summary, double durationSeconds, double? combatDurationSeconds = null)
    {
        var denominator = Math.Max(1, durationSeconds);
        var combatSeconds = Math.Max(0, combatDurationSeconds ?? durationSeconds);
        var combatDenominator = Math.Max(1, combatSeconds);
        return new(durationSeconds, combatSeconds > 0 ? summary.DamageDealt / combatDenominator : 0,
            combatSeconds > 0 ? summary.HealingDone / combatDenominator : 0,
            summary.Fame * 3600 / denominator, summary.Silver * 3600 / denominator, summary.LootValue * 3600d / denominator,
            combatSeconds > 0 ? summary.DamageReceived / combatDenominator : 0,
            combatSeconds > 0 ? summary.HealingReceived / combatDenominator : 0, combatSeconds);
    }
}
