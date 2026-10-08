using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Network.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace AlbionDataAvalonia.ViewModels;

public sealed record ActivityOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class ActivitySummaryPresentation
{
    public static ActivitySummaryPresentation Empty { get; } = new(new(), ActivityProjection.GetRates(new(), 0));

    public ActivitySummaryPresentation(ActivitySummary summary, ActivityRates rates, bool isSessionAverage = false)
    {
        Summary = summary;
        Rates = rates;
        IsSessionAverage = isSessionAverage;
    }

    public ActivitySummary Summary { get; }
    public ActivityRates Rates { get; }
    public bool IsSessionAverage { get; }
    public string DamageDealt => ActivityDisplay.Number(Summary.DamageDealt);
    public string DamageReceived => ActivityDisplay.Number(Summary.DamageReceived);
    public string HealingDone => ActivityDisplay.Number(Summary.HealingDone);
    public string HealingReceived => ActivityDisplay.Number(Summary.HealingReceived);
    public string Fame => ActivityDisplay.Number(Summary.Fame);
    public string Silver => Summary.Silver.ToString("N2", CultureInfo.CurrentCulture);
    public string LootValue => Summary.LootValue == 0 && Summary.MissingLootPriceCount > 0 ? "Unavailable" : ActivityDisplay.Number(Summary.LootValue);
    public string LootItemsText => $"{Summary.LootItems:N0} items picked up";
    public string MobKills => ActivityDisplay.Number(Summary.MobKills);
    public string DamageRate => $"{Rates.DamagePerSecond:N1} DPS";
    public string DamageReceivedRate => $"{Rates.DamageReceivedPerSecond:N1} / combat second";
    public string HealingRate => $"{Rates.HealingPerSecond:N1} HPS";
    public string HealingReceivedRate => $"{Rates.HealingReceivedPerSecond:N1} / combat second";
    public string FameRate => $"{Rates.FamePerHour:N0} / hour";
    public string SilverRate => $"{Rates.SilverPerHour:N0} / hour";
    public string LootValueRate => $"{Rates.LootValuePerHour:N0} / hour";
    public bool HasUnknownSilver => Summary.UnknownSilverCount > 0;
    public bool HasMissingLootPrices => Summary.MissingLootPriceCount > 0;
    public string UnknownSilver => $"{Summary.UnknownSilverCount:N0} unknown net amounts";
    public string MissingLootPrices => $"{Summary.MissingLootPriceCount:N0} items without prices";
    public string PlayerOutcomes => $"{Summary.PlayerKills:N0} player kills · {Summary.Deaths:N0} deaths · {Summary.Knockdowns:N0} knockdowns";
}

public sealed class ActivitySessionRow : ObservableObject
{
    public ActivitySessionRow(ActivitySession source, ActivityScope scope)
    {
        Source = source;
        Update(source, scope);
    }

    public void Update(ActivitySession source, ActivityScope scope)
    {
        Source = source;
        var summary = ActivityProjection.GetSummary(source, scope);
        FameText = ActivityDisplay.Number(summary.Fame);
        SilverText = summary.Silver.ToString("N2", CultureInfo.CurrentCulture);
        LootValueText = ActivityDisplay.Number(summary.LootValue);
        OnPropertyChanged(string.Empty);
    }

    public ActivitySession Source { get; private set; }
    public Guid Id => Source.Id;
    public string Title => ActivityDisplay.SessionTitle(Source);
    public string Character => Source.CharacterName;
    public string Server => ActivityDisplay.Server(Source.Server);
    public string StartedText => ActivityDisplay.LocalDate(Source.StartedAtUtc);
    public string DurationText => ActivityDisplay.Duration(ActivityProjection.GetDurationSeconds(Source, Source.State == ActivityState.Open ? DateTime.UtcNow : null));
    public string FameText { get; private set; } = "0";
    public string SilverText { get; private set; } = "0";
    public string LootValueText { get; private set; } = "0";
    public string MetadataText => $"{StartedText} · {Character} · {Server}";
    public string EarningsText => $"Fame {FameText} · silver {SilverText} · loot {LootValueText}";
    public string StateText => $"{(Source.IsPartial ? "Partial · " : "")}{ActivityDisplay.State(Source.State, Source.EndReason)}{(Source.HasInferredGrouping ? " · inferred grouping" : "")}";
}

public sealed class ActivityEncounterRow : ObservableObject
{
    public ActivityEncounterRow(ActivityEncounter source, ActivityScope scope)
    {
        Source = source;
        Update(source, scope);
    }

    public void Update(ActivityEncounter source, ActivityScope scope)
    {
        Source = source;
        var summary = ActivityProjection.GetSummary(source, scope);
        var rates = ActivityProjection.GetRates(summary, source);
        DamageText = ActivityDisplay.Number(summary.DamageDealt);
        HealingText = ActivityDisplay.Number(summary.HealingDone);
        DamageRateText = rates.DamagePerSecond.ToString("N1", CultureInfo.CurrentCulture);
        HealingRateText = rates.HealingPerSecond.ToString("N1", CultureInfo.CurrentCulture);
        OnPropertyChanged(string.Empty);
    }

    public ActivityEncounter Source { get; private set; }
    public Guid Id => Source.Id;
    public int Number => Source.Number;
    public string StartedText => ActivityDisplay.LocalTime(Source.StartedAtUtc);
    public string Location => Source.LocationName;
    public string Classification => ActivityDisplay.EncounterKind(Source.Kind);
    public string DurationText => ActivityDisplay.Duration(Source.CombatDurationSeconds);
    public string DamageText { get; private set; } = "0";
    public string DamageRateText { get; private set; } = "0";
    public string HealingText { get; private set; } = "0";
    public string HealingRateText { get; private set; } = "0";
    public string StateText => ActivityDisplay.State(Source.State, Source.EndReason);
}

public sealed class ActivityPlayerRow(ActivityActorTotal source)
{
    public ActivityActorTotal Source { get; } = source;
    public string ActorId => Source.ActorId;
    public string Name => Source.Name;
    public string Relationship => ActivityDisplay.Relationship(Source.Relationship);
    public string DamageDealt => ActivityDisplay.Number(Source.DamageDealt);
    public string DamageReceived => ActivityDisplay.Number(Source.DamageReceived);
    public string HealingDone => ActivityDisplay.Number(Source.HealingDone);
    public string HealingReceived => ActivityDisplay.Number(Source.HealingReceived);
}

public sealed class ActivityMobRow(ActivityMobTotal source)
{
    public string Name => source.Name;
    public string Count => ActivityDisplay.Number(source.Kills);
    public string InferredCount => ActivityDisplay.Number(source.InferredKills);
}

public sealed class ActivityLootRow(ActivityEvent source)
{
    public ActivityEvent SourceEvent { get; } = source;
    public Guid Id => source.Id;
    public string TimeText => ActivityDisplay.LocalTime(source.CapturedAtUtc);
    public string DateTimeText => ActivityDisplay.LocalDate(source.CapturedAtUtc);
    public string Recipient => string.IsNullOrWhiteSpace(source.SourceName) ? "Unknown" : source.SourceName;
    public string Guild => source.SourceGuild;
    public string Alliance => source.SourceAlliance;
    public string Relationship => ActivityDisplay.Relationship(source.SourceRelationship);
    public string ItemUniqueName => source.ItemUniqueName ?? "";
    public string ItemName => string.IsNullOrWhiteSpace(source.ItemName) ? source.ItemUniqueName ?? "Unknown item" : source.ItemName;
    public int ImageQuality => source.Quality ?? 1;
    public string Quality => ItemQuality.Format(source.Quality);
    public string AmountText => source.Quantity > 0 ? ActivityDisplay.Number(source.Quantity) : source.Evidence == ActivityEvidence.Supplemental ? "Pending" : "Unknown";
    public string UnitValueText => source.EstimatedUnitValue is { } value ? ActivityDisplay.Number(value) : "Unavailable";
    public string ValueText => source.Quantity <= 0 ? source.Evidence == ActivityEvidence.Supplemental ? "Quantity pending" : "Unknown quantity" : ActivityProjection.GetLootValue(source) is { } value ? ActivityDisplay.Number(value) : "Unavailable";
    public string Source => string.IsNullOrWhiteSpace(source.LootSourceName) ? source.LootSourceKind : source.LootSourceName;
    public string Location => source.LocationName;
    public string Evidence => source.Evidence == ActivityEvidence.Supplemental ? "Pending" : source.Evidence.ToString();
    public string ItemDetails => $"{ItemName}\n{Quality} quality\nUnit estimate: {UnitValueText} silver";
    public string RecipientDetails => $"{Recipient} · {Relationship}{(string.IsNullOrWhiteSpace(Guild) ? "" : $"\nGuild: {Guild}")}{(string.IsNullOrWhiteSpace(Alliance) ? "" : $"\nAlliance: {Alliance}")}";
    public string ContextDetails => $"{Location}\n{(source.SessionId is null ? "Outside a recorded session" : "Session loot")}";
}

public sealed class ActivityEventRow(ActivityEvent source)
{
    public ActivityEvent SourceEvent { get; } = source;
    public Guid Id => source.Id;
    public string TimeText => ActivityDisplay.LocalTime(source.CapturedAtUtc);
    public string Kind => source.Kind switch
    {
        ActivityEventKind.MobKilled => "Mob killed",
        ActivityEventKind.PlayerKilled => "Player killed",
        ActivityEventKind.CaptureGap => "Capture gap",
        ActivityEventKind.OutcomeEvidence => "Outcome evidence",
        _ => source.Kind.ToString()
    };
    public string Source => source.SourceName;
    public string Target => source.TargetName;
    public string Location => source.LocationName;
    public string Evidence => source.Evidence == ActivityEvidence.Supplemental ? source.Kind == ActivityEventKind.Loot ? "Pending" : "Unverified" : source.Evidence.ToString();
    public string Description => source.Kind switch
    {
        ActivityEventKind.Fame when source.Evidence == ActivityEvidence.Supplemental => "Award unknown",
        ActivityEventKind.Damage or ActivityEventKind.Healing or ActivityEventKind.Fame => ActivityDisplay.Number(source.Amount),
        ActivityEventKind.Silver => source.HasKnownNetSilver ? $"{source.SilverRaw / 10000d:N2} silver" : "Net amount unknown",
        ActivityEventKind.Loot => source.Quantity > 0 ? $"{source.Quantity:N0} × {source.ItemName}" : $"Quantity unknown · {source.ItemName}",
        ActivityEventKind.MobKilled => string.IsNullOrWhiteSpace(source.TargetName) ? source.MobUniqueName ?? $"Mob {source.MobIndex}" : source.TargetName,
        ActivityEventKind.CaptureGap => "Recording interrupted",
        ActivityEventKind.Location => source.LocationName,
        _ => $"{ActivityDisplay.Relationship(source.SourceRelationship)} → {ActivityDisplay.Relationship(source.TargetRelationship)}"
    };
}

internal static class ActivityDisplay
{
    public static string Server(string value) => int.TryParse(value, out var id) ? AlbionServers.Get(id)?.Name ?? value : value;
    public static string EncounterKind(ActivityEncounterKind value) => value switch
    { ActivityEncounterKind.Pve => "PvE", ActivityEncounterKind.Pvp => "PvP", _ => value.ToString() };
    public static string SessionTitle(ActivitySession value)
    {
        if (value.Kind == ActivitySessionKind.Roaming) return string.IsNullOrWhiteSpace(value.LocationName) ? "Activity" : $"Activity in {value.LocationName}";
        var kind = SessionKind(value.Kind);
        return string.IsNullOrWhiteSpace(value.LocationName) || string.Equals(kind, value.LocationName, StringComparison.OrdinalIgnoreCase)
            ? kind : $"{kind} · {value.LocationName}";
    }
    public static string SessionKind(ActivitySessionKind value) => value switch
    {
        ActivitySessionKind.MistsDungeon => "Mists dungeon",
        ActivitySessionKind.AbyssalDepths => "Abyssal Depths",
        ActivitySessionKind.DragonArea => "Dragon area",
        ActivitySessionKind.StaticDungeon => "Static dungeon",
        _ => value.ToString()
    };
    public static string Number(double value) => value.ToString("N0", CultureInfo.CurrentCulture);
    public static string LocalDate(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public static string LocalTime(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
    public static string Duration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalDays >= 1 ? $"{(int)duration.TotalDays}d {duration:hh\\:mm\\:ss}" : $"{(int)duration.TotalHours}:{duration:mm\\:ss}";
    }
    public static string Relationship(ActorRelationship value) => value switch { ActorRelationship.Self => "You", ActorRelationship.Party => "Party", ActorRelationship.Other => "Other", _ => "Unknown" };
    public static string State(ActivityState state, ActivityEndReason reason) => state == ActivityState.Open ? "Active"
        : state == ActivityState.Interrupted ? "Interrupted" : reason == ActivityEndReason.Finished ? "Finished" : "Ended";
}
