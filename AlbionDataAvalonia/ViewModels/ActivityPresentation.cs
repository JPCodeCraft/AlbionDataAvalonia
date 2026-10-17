using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels;

public sealed class ActivitySummaryPresentation
{
    public static ActivitySummaryPresentation Empty { get; } = new(new(), ActivityProjection.GetRates(new(), 0));

    public ActivitySummaryPresentation(ActivitySummary summary, ActivityRates rates, bool isSessionAverage = false, bool hasRecordedDuration = true,
        double? runSeconds = null)
    {
        Summary = summary;
        Rates = rates;
        IsSessionAverage = isSessionAverage;
        HasRecordedDuration = hasRecordedDuration;
        RunSeconds = runSeconds;
    }

    // A play session also reports its rate over time spent in runs, without the time between them.
    public double? RunSeconds { get; }
    public bool HasRunRate => RunSeconds is > 0 && HasRecordedDuration && !IsImported;
    public string TotalSilverPerRunHourText => HasRunRate
        ? $"{UiFormat.Compact((Summary.Silver + Summary.LootValue + Summary.ResourceValue) * 3600 / RunSeconds!.Value)} / h in runs" : "";

    public ActivitySummary Summary { get; }
    public bool IsImported => Summary.IsLegacyAggregate;
    public ActivityRates Rates { get; }
    public bool IsSessionAverage { get; }
    public bool HasRecordedDuration { get; }
    public string DamageDealt => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.DamageDealt);
    public string DamageReceived => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.DamageReceived);
    public string HealingDone => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.HealingDone);
    public string HealingReceived => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.HealingReceived);
    public string Fame => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.Fame);
    public string CombatFame => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.CombatFame);
    public string GatheringFame => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.GatheringFame + Summary.FishingFame);
    public string UnknownFame => UiFormat.Compact(Summary.UnclassifiedFame);
    public bool HasUnknownFame => Summary.UnclassifiedFame > 0 && !Summary.IsLegacyAggregate;
    public bool HasPvpFame => Summary.PvpFame > 0;
    public string TotalSilver => UiFormat.Compact(Summary.Silver + Summary.LootValue + Summary.ResourceValue);
    public string TotalSilverPerHour => HasRecordedDuration ? UiFormat.Compact(Rates.SilverPerHour + Rates.LootValuePerHour + Rates.ResourceValuePerHour) : "Unavailable";
    public string TotalSilverDetailsText => (IsImported ? "Imported gathering EMV only" : "Silver + loot EMV + gathering EMV")
        + (HasMissingItemPrices || HasUnknownSilver ? "\nExcludes items without EMV and unknown silver" : "");
    public string Silver => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.Silver);
    public string LootValue => Summary.LootValue == 0 && Summary.MissingLootPriceCount > 0 ? "Unavailable" : UiFormat.Compact(Summary.LootValue);
    public string LootItemsText => $"{UiFormat.Compact(Summary.LootItems)} items picked up";
    public string MobKills => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.MobKills);
    public string PlayerKills => Summary.IsLegacyAggregate ? "Unavailable" : UiFormat.Compact(Summary.PlayerKills);
    public string DamageRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{UiFormat.Compact(Rates.DamagePerSecond)} DPS";
    public string DamageReceivedRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{UiFormat.Compact(Rates.DamageReceivedPerSecond)} DPS";
    public string HealingRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{UiFormat.Compact(Rates.HealingPerSecond)} HPS";
    public string HealingReceivedRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{UiFormat.Compact(Rates.HealingReceivedPerSecond)} HPS";
    public string FameRate => Summary.IsLegacyAggregate || !HasRecordedDuration ? "Not recorded" : $"{UiFormat.Compact(Rates.FamePerHour)} / hour";
    public string CombatFameRate => FameRateText(Summary.CombatFame);
    public string GatheringFameRate => FameRateText(Summary.GatheringFame + Summary.FishingFame);
    public string CombatFameDetailsText => HasPvpFame ? $"PvE {UiFormat.Compact(Summary.PveFame)} · PvP {UiFormat.Compact(Summary.PvpFame)}" : "Your combat fame";
    public string GatheringFameDetailsText => Summary.FishingFame > 0 ? $"Gathering {UiFormat.Compact(Summary.GatheringFame)} · Fishing {UiFormat.Compact(Summary.FishingFame)}" : "Your gathering fame";
    public string CombatFameFooterText => (IsSessionAverage ? CombatFameRate : "")
        + (HasPvpFame ? $"{(IsSessionAverage ? " · " : "")}PvP {UiFormat.Compact(Summary.PvpFame)}" : "");
    public string UnknownFameText => $"Unknown fame {UnknownFame}";
    public string SilverRate => Summary.IsLegacyAggregate || !HasRecordedDuration ? "Not recorded" : $"{UiFormat.Compact(Rates.SilverPerHour)} / hour";
    public string LootValueRate => HasRecordedDuration ? ValueRateText(Summary.LootValue, Summary.MissingPriceItems, Rates.LootValuePerHour) : "Hourly rate unavailable";
    public bool HasUnknownSilver => Summary.UnknownSilverCount > 0;
    public bool HasMissingLootPrices => Summary.MissingLootPriceCount > 0;
    public string UnknownSilver => $"{Summary.UnknownSilverCount:N0} pickups with an unknown amount";
    public string SilverFooterText => (IsSessionAverage ? SilverRate : "")
        + (HasUnknownSilver ? $"{(IsSessionAverage ? " · " : "")}{Summary.UnknownSilverCount:N0} unknown" : "");
    public string SilverDetailsText => "After taxes and penalties"
        + (HasUnknownSilver ? $"\n{UnknownSilver}" : "");
    public string MissingLootPrices => $"{Summary.MissingLootPriceCount:N0} items without EMV";
    // Non-lethal kills and deaths count in the totals and are labelled wherever they appear.
    public string PlayerKillsLabel => Summary.NonLethalPlayerKills <= 0 ? "PvP kills"
        : Summary.NonLethalPlayerKills == Summary.PlayerKills ? "PvP kills · non-lethal" : $"PvP kills · {Summary.NonLethalPlayerKills:N0} non-lethal";
    public string DeathsAndKnockdowns => Summary.IsLegacyAggregate ? "" : string.Join(" · ", new[]
    {
        Summary.Deaths <= 0 ? null : Summary.NonLethalDeaths == Summary.Deaths
            ? UiFormat.Count(Summary.Deaths, "non-lethal death")
            : UiFormat.Count(Summary.Deaths, "death") + ActivityDisplay.NonLethal(Summary.NonLethalDeaths),
        Summary.Knockdowns > 0 ? UiFormat.Count(Summary.Knockdowns, "knockdown") : null
    }.Where(value => value is not null));
    public string OutcomesDetailsText => $"Mob kills: {MobKills}\nPvP kills: {PlayerKills}{ActivityDisplay.NonLethal(Summary.NonLethalPlayerKills)}" +
        $"\nDeaths: {Summary.Deaths:N0}{ActivityDisplay.NonLethal(Summary.NonLethalDeaths)}\nKnockdowns: {Summary.Knockdowns:N0}" +
        (Summary.NonLethalPlayerKills + Summary.NonLethalDeaths > 0 ? "\nNon-lethal: no lethal flag, e.g. arenas" : "");
    public bool HasResources => Summary.ResourceItems > 0;
    public bool HasMissingItemPrices => Summary.MissingPriceItems + Summary.MissingResourcePriceItems > 0;
    public string ResourceItemsText => $"{UiFormat.Compact(Summary.ResourceItems)} items gathered";
    public string ResourceQuantityText => UiFormat.Compact(Summary.ResourceItems);
    public string ResourceBreakdownText => $"Gathering {UiFormat.Compact(Summary.GatheringItems)} · Fishing {UiFormat.Compact(Summary.FishingItems)}" + (Summary.UnclassifiedResourceItems > 0 ? $" · Other {UiFormat.Compact(Summary.UnclassifiedResourceItems)}" : "");
    public string ResourceValueText => Summary.ResourceValue == 0 && Summary.MissingResourcePriceItems > 0 ? "Unavailable" : UiFormat.Compact(Summary.ResourceValue);
    public string ResourceRateText => HasRecordedDuration ? $"{UiFormat.Compact(Rates.ResourceItemsPerHour)} items / hour · {ValueRateText(Summary.ResourceValue, Summary.MissingResourcePriceItems, Rates.ResourceValuePerHour)}" : "Hourly rate unavailable";
    public string ItemsCountText => $"{UiFormat.Compact(Summary.LootItems + Summary.ResourceItems)} items acquired";
    public string ItemsFooterText => $"{UiFormat.Compact(Summary.LootItems)} items"
        + (IsSessionAverage && HasRecordedDuration && (Summary.LootValue > 0 || !HasMissingLootPrices) ? $" · {UiFormat.Compact(Rates.LootValuePerHour)} / h" : "")
        + (HasMissingLootPrices ? " *" : "");
    public string ItemsDetailsText => "Black Market EMV when available, otherwise EMV" + (HasMissingLootPrices ? $"\n{MissingLootPrices}" : "");
    public string ResourceValueDetailsText => ResourceRateText + (Summary.MissingResourcePriceItems > 0 ? $"\n{Summary.MissingResourcePriceItems:N0} items without EMV" : "");
    private static string ValueRateText(long knownValue, long missingItems, double rate) => missingItems > 0 && knownValue == 0
        ? "EMV / hour unavailable" : $"{UiFormat.Compact(rate)} EMV / hour";
    private string FameRateText(double amount) => Summary.IsLegacyAggregate || !HasRecordedDuration ? "Not recorded" : $"{UiFormat.Compact(ActivityProjection.GetFameRate(amount, Rates))} / hour";

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
        if (summary is not null && Source == source && this.scope == scope) return;
        Source = source;
        this.scope = scope;
        summary = ActivityProjection.GetSummary(source, scope);
        OnPropertyChanged(string.Empty);
    }

    public void RefreshDuration() => OnPropertyChanged(nameof(DurationText));

    public ActivitySession Source { get; private set; }
    public Guid Id => Source.Id;
    public string Title => ActivityDisplay.SessionTitle(Source);
    public string DurationText => UiFormat.Duration(ActivityProjection.GetDurationSeconds(Source, Source.State == ActivityState.Open ? DateTime.UtcNow : null));
    public string RunLabel => ActivityDisplay.RunLabel(Source);
    public string TotalSilverText => UiFormat.Compact((summary?.Silver ?? 0) + (summary?.LootValue ?? 0) + (summary?.ResourceValue ?? 0));
    public string RunSummaryText => $"{DurationText} · {TotalSilverText} silver";
    public string RunDetailsText => $"{Title}\n{UiFormat.LocalTime(Source.StartedAtUtc)} · {DurationText}\nSilver and items {TotalSilverText}";
    // The run the tracker has open now.
    private bool isLive;
    public bool IsLive { get => isLive; set => SetProperty(ref isLive, value); }
    private ActivityScope scope;
    private ActivitySummary? summary;
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
        if (hasSummary && Source == source && this.scope == scope) return;
        Source = source;
        this.scope = scope;
        hasSummary = true;
        var summary = ActivityProjection.GetSummary(source, scope);
        var rates = ActivityProjection.GetRates(summary, source);
        DamageText = UiFormat.Compact(summary.DamageDealt);
        HealingText = UiFormat.Compact(summary.HealingDone);
        DamageRateText = UiFormat.Compact(rates.DamagePerSecond);
        HealingRateText = UiFormat.Compact(rates.HealingPerSecond);
        OnPropertyChanged(string.Empty);
    }

    private ActivityScope scope;
    private bool hasSummary;

    public ActivityEncounter Source { get; private set; }
    public Guid Id => Source.Id;
    public int Number => Source.Number;
    public string StartedText => UiFormat.LocalTime(Source.StartedAtUtc);
    public string Location => Source.LocationName;
    public string Classification => ActivityDisplay.EncounterKind(Source.Kind);
    public string DurationText => UiFormat.Duration(Source.CombatDurationSeconds);
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
    public string Name => ActivityDisplay.ActorName(Source.Name);
    public string Relationship => ActivityDisplay.Relationship(Source.Relationship);
    public string DamageDealt => UiFormat.Compact(Source.DamageDealt);
    public string DamageReceived => UiFormat.Compact(Source.DamageReceived);
    public string HealingDone => UiFormat.Compact(Source.HealingDone);
    public string HealingReceived => UiFormat.Compact(Source.HealingReceived);
    // Earnings stay blank when nothing was recorded; the game reports fame only for you.
    public string Fame => Source.Fame > 0 ? UiFormat.Compact(Source.Fame) : "";
    public string Silver => Source.SilverRaw > 0 ? UiFormat.Compact(Source.SilverRaw / 10000d) : "";
    public string Loot => ItemsText(Source.LootItems, Source.LootValue);
    public string Gathered => ItemsText(Source.GatheredItems, Source.GatheredValue);
    public string Deaths => Source.Deaths > 0 ? Source.Deaths.ToString("N0", CultureInfo.CurrentCulture) + ActivityDisplay.NonLethal(Source.NonLethalDeaths) : "";
    private static string ItemsText(long items, long value) => items <= 0 ? ""
        : $"{(value > 0 ? UiFormat.Compact(value) : "–")} ({items.ToString("N0", CultureInfo.CurrentCulture)})";
}

// Players-table columns with at least one recorded value.
public sealed record ActivityPlayerColumns(bool Fame, bool Silver, bool Loot, bool Gathered, bool Deaths)
{
    public static readonly ActivityPlayerColumns None = new(false, false, false, false, false);
    public static ActivityPlayerColumns From(IEnumerable<ActivityActorTotal> totals)
    {
        var rows = totals.ToArray();
        return new(rows.Any(x => x.Fame > 0), rows.Any(x => x.SilverRaw > 0), rows.Any(x => x.LootItems > 0),
            rows.Any(x => x.GatheredItems > 0), rows.Any(x => x.Deaths > 0));
    }
}

public sealed class ActivityMobRow(ActivityMobTotal source)
{
    public string Name => ActivityDisplay.MobName(source.Name);
    public string Count => UiFormat.Compact(source.Kills);
    public string InferredCount => UiFormat.Compact(source.InferredKills);
}

public sealed class ActivityLootRow(ActivityEvent source)
{
    public ActivityEvent SourceEvent { get; } = source;
    public Guid Id => source.Id;
    public bool IsImported => source.Kind == ActivityEventKind.LegacyGatheringAggregate;
    public string TimeText => IsImported ? "Session total" : UiFormat.LocalTime(source.CapturedAtUtc);
    public string DateTimeText => UiFormat.LocalDateTime(source.CapturedAtUtc) + (IsImported ? " · session total" : "");
    public string Recipient => ActivityDisplay.ActorName(source.SourceName, source.SourceKind);
    public string Guild => source.SourceGuild;
    public string Alliance => source.SourceAlliance;
    // Target affiliation is the looted player's when the loot came from a player.
    public string LootSourceGuild => source.TargetGuild;
    public string LootSourceAlliance => source.TargetAlliance;
    public string Relationship => ActivityDisplay.Relationship(source.SourceRelationship);
    public string ItemUniqueName => source.ItemUniqueName ?? "";
    public string ItemName => ActivityDisplay.ItemName(source.ItemName);
    public int ImageQuality => source.Quality ?? 1;
    public string Quality => ItemQuality.Format(source.Quality);
    public string AmountText => source.Quantity > 0 ? UiFormat.Compact(source.Quantity) : source.Evidence == ActivityEvidence.Supplemental ? "Pending" : "Unknown";
    public string UnitValueText => source.EstimatedUnitValue is { } value ? UiFormat.Compact(value) : "Unavailable";
    public string ValueText => source.Quantity <= 0 ? source.Evidence == ActivityEvidence.Supplemental ? "Quantity pending" : "Unknown quantity" : ActivityProjection.GetLootValue(source) is { } value
        ? UiFormat.Compact(value) + (EmvName == BlackMarketEmv ? $" ({BlackMarketEmv})" : "") : "Unavailable";
    private const string BlackMarketEmv = "Black Market EMV";
    // Loot prefers the Black Market EMV; rows priced before the kind was recorded cannot tell which.
    private string EmvName => source.Kind != ActivityEventKind.Loot ? "EMV" : ActivityItemValuationService.IsBlackMarketEmv(source) switch
    {
        true => BlackMarketEmv,
        false => "EMV",
        null => "Black Market EMV or EMV"
    };
    public string Source => source.Kind switch
    {
        ActivityEventKind.Gathered => "Gathering",
        ActivityEventKind.FishingReward => "Fishing",
        ActivityEventKind.LegacyGatheringAggregate => "Gathering",
        _ => ActivityDisplay.LootSource(source.LootSourceName, source.LootSourceKind)
    };
    public string Location => string.IsNullOrWhiteSpace(source.LocationName) ? "Unavailable" : source.LocationName;
    public string Evidence => IsImported ? "Imported summary" : source.Evidence == ActivityEvidence.Supplemental ? "Pending" : source.Evidence.ToString();
    public string ItemDetails => $"{ItemName} · {Quality}\n" + (source.EstimatedUnitValue is null
        ? "EMV unavailable" : $"{EmvName} each: {UnitValueText} silver")
        + (source.EstimatedUnitValue is not null && source.Quality is null
            ? source.Kind == ActivityEventKind.Loot ? $"\nAverage {EmvName} across qualities 1-4" : "\nNormal-quality EMV" : "")
        + (source.Evidence != ActivityEvidence.Confirmed ? "\nNot included in totals" : "");
    public string RecipientDetails => $"{Recipient} · {Relationship}{(string.IsNullOrWhiteSpace(Guild) ? "" : $"\nGuild: {Guild}")}{(string.IsNullOrWhiteSpace(Alliance) ? "" : $"\nAlliance: {Alliance}")}";
    public string ContextDetails => Location;
}

public sealed class ActivityEventRow(ActivityEvent source)
{
    public ActivityEvent SourceEvent { get; } = source;
    public Guid Id => source.Id;
    public string TimeText => UiFormat.LocalTime(source.CapturedAtUtc);
    public string Kind => source.Kind switch
    {
        ActivityEventKind.MobKilled => "Mob killed",
        ActivityEventKind.Fame => ActivityDisplay.FameKind(ActivityClassification.GetFameCategory(source)),
        ActivityEventKind.PlayerKilled => "Player killed",
        ActivityEventKind.CaptureGap => "Recording paused",
        ActivityEventKind.OutcomeEvidence => "Other activity",
        ActivityEventKind.Gathered => "Gathered",
        ActivityEventKind.FishingReward => "Fishing",
        ActivityEventKind.LegacyGatheringAggregate => "Imported gathering total",
        ActivityEventKind.Silver => "Silver picked up",
        ActivityEventKind.Loot => "Loot picked up",
        ActivityEventKind.Knockdown => "Knocked down",
        ActivityEventKind.Location => "Location changed",
        ActivityEventKind.LegacyImportMarker => "Imported gathering",
        ActivityEventKind.PartyJoined => ActivityPartyDetails.Parse(source.DetailsJson) is { Roster: true } ? "In party" : "Joined party",
        ActivityEventKind.PartyLeft => "Left party",
        ActivityEventKind.Death => ActivityDatabaseFunctions.JsonLong(source.DetailsJson, "$.Lethal") == 0 ? "Death (non-lethal)" : "Death",
        _ => source.Kind.ToString()
    };
    public string Source => string.IsNullOrWhiteSpace(source.SourceName) && source.Kind is not (ActivityEventKind.Damage or ActivityEventKind.Healing or ActivityEventKind.PlayerKilled or ActivityEventKind.Death or ActivityEventKind.Knockdown)
        ? "" : ActivityDisplay.ActorName(source.SourceName, source.SourceKind);
    public string Target => source.Kind is ActivityEventKind.Damage or ActivityEventKind.Healing or ActivityEventKind.MobKilled or ActivityEventKind.PlayerKilled or ActivityEventKind.Death or ActivityEventKind.Knockdown
        ? ActivityDisplay.ActorName(source.TargetName, source.TargetKind) : "";
    public string Location => source.LocationName;
    public string Evidence => source.Evidence == ActivityEvidence.Supplemental ? source.Kind == ActivityEventKind.Loot ? "Pending" : "Unverified" : source.Evidence.ToString();
    public string Description => source.Kind switch
    {
        ActivityEventKind.Fame when source.Evidence == ActivityEvidence.Supplemental => "Amount unknown",
        ActivityEventKind.Fame => UiFormat.Compact(ActivityProjection.Contribution(source, ActivityScope.AllObserved).Fame),
        ActivityEventKind.Damage or ActivityEventKind.Healing => UiFormat.Compact(source.Amount),
        ActivityEventKind.Silver => source.HasKnownNetSilver ? $"{source.SilverRaw / 10000d:N0} silver" : "Amount unknown",
        ActivityEventKind.Loot or ActivityEventKind.Gathered or ActivityEventKind.FishingReward or ActivityEventKind.LegacyGatheringAggregate => source.Quantity > 0 ? $"{UiFormat.Compact(source.Quantity)} × {ActivityDisplay.ItemName(source.ItemName)}" : $"Quantity unknown · {ActivityDisplay.ItemName(source.ItemName)}",
        ActivityEventKind.PartyJoined or ActivityEventKind.PartyLeft => ActivityPartyDetails.Parse(source.DetailsJson) is { PartySize: > 0 } party
            ? party.PartySize == 1 ? "Now solo" : $"Party size {party.PartySize:N0}" : "",
        _ => ""
    };
}

internal static class ActivityDisplay
{
    public static string Server(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown server" : int.TryParse(value, out var id) ? AlbionServers.Get(id)?.Name ?? value : value;
    public static string EncounterKind(ActivityEncounterKind value) => value switch
    { ActivityEncounterKind.Pve => "PvE", ActivityEncounterKind.Pvp => "PvP", _ => value.ToString() };
    public static string FameKind(ActivityFameCategory value) => value switch
    {
        ActivityFameCategory.Pve => "Combat fame",
        ActivityFameCategory.Pvp => "PvP fame",
        ActivityFameCategory.Gathering => "Gathering fame",
        ActivityFameCategory.Fishing => "Fishing fame",
        _ => "Fame"
    };
    public static string SessionTitle(ActivitySession value)
    {
        if (ActivityProjection.GetSummary(value, ActivityScope.Personal).IsLegacyAggregate) return string.IsNullOrWhiteSpace(value.Title) ? "Gathering · Imported summary" : value.Title;
        if (value.Kind == ActivitySessionKind.Roaming) return string.IsNullOrWhiteSpace(value.LocationName) ? "Activity" : $"Activity in {value.LocationName}";
        var kind = SessionKind(value.Kind);
        return string.IsNullOrWhiteSpace(value.LocationName) || string.Equals(kind, value.LocationName, StringComparison.OrdinalIgnoreCase)
            ? kind : $"{kind} · {value.LocationName}";
    }
    // Short run names for chips and play session titles: the zone for open world, the activity otherwise.
    public static string RunLabel(ActivitySession value)
    {
        if (ActivityProjection.GetSummary(value, ActivityScope.Personal).IsLegacyAggregate) return "Imported gathering";
        if (value.Kind == ActivitySessionKind.Roaming) return string.IsNullOrWhiteSpace(value.LocationName) ? "Open world" : value.LocationName;
        return value.Kind switch
        {
            ActivitySessionKind.Corrupted => "Corrupted",
            ActivitySessionKind.MistsDungeon => "Mists dungeon",
            ActivitySessionKind.StaticDungeon => "Static dungeon",
            _ => SessionKind(value.Kind)
        };
    }

    // "Wyre Forest · Mists ×2 · Dungeon", in the order first visited.
    public static string PlaySessionTitle(IReadOnlyList<ActivitySession> runs)
    {
        if (runs.Count == 0) return "Session";
        if (runs.Count == 1) return SessionTitle(runs[0]);
        var labels = runs.OrderBy(x => x.StartedAtUtc).GroupBy(RunLabel, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key).ToArray();
        return labels.Length <= 3 ? string.Join(" · ", labels) : $"{string.Join(" · ", labels.Take(3))} · +{labels.Length - 3} more";
    }

    public static string SessionKind(ActivitySessionKind value) => value switch
    {
        ActivitySessionKind.Roaming => "Open world",
        ActivitySessionKind.Corrupted => "Corrupted dungeon",
        ActivitySessionKind.MistsDungeon => "Mists dungeon",
        ActivitySessionKind.AbyssalDepths => "Abyssal Depths",
        ActivitySessionKind.DragonArea => "Dragon area",
        ActivitySessionKind.StaticDungeon => "Static dungeon",
        _ => value.ToString()
    };
    public static string Tags(ActivityTags tags) => string.Join(" · ", new[]
    {
        tags.HasFlag(ActivityTags.Gathering) ? "Gathering" : null,
        tags.HasFlag(ActivityTags.Pve) ? "PvE" : null,
        tags.HasFlag(ActivityTags.Pvp) ? "PvP" : null
    }.Where(value => value is not null));
    public static string ItemName(string? value) => string.IsNullOrWhiteSpace(value) || value == "Unset" || value.StartsWith('@') || value.Contains('_') ? "Unknown item" : value;
    public static string ActorName(string? value, ActivityActorKind kind = ActivityActorKind.Unknown)
        => kind == ActivityActorKind.Mob ? MobName(value) : string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _) ? "Unknown" : value;
    public static string MobName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Unknown" || value.StartsWith('@') || value.Contains('_')
            || value.StartsWith("Mob ", StringComparison.Ordinal) && (value[4..] == "?" || int.TryParse(value[4..], out _))) return "Unknown mob";
        return value;
    }
    public static string LootSource(string? name, string? kind)
    {
        if (string.Equals(kind, "Player", StringComparison.OrdinalIgnoreCase)) return ActorName(name);
        if (string.Equals(kind, "Chest", StringComparison.OrdinalIgnoreCase)
            || name?.Contains("CHEST", StringComparison.OrdinalIgnoreCase) == true
            || name?.Contains("TREASURE", StringComparison.OrdinalIgnoreCase) == true) return "Chest";
        if (string.Equals(kind, "Mob", StringComparison.OrdinalIgnoreCase) || name?.StartsWith("@MOB", StringComparison.OrdinalIgnoreCase) == true)
            return string.IsNullOrWhiteSpace(name) || name.StartsWith('@') || name.Contains('_') ? "Mob" : name;
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Unknown", StringComparison.OrdinalIgnoreCase))
            return string.Equals(kind, "Player", StringComparison.OrdinalIgnoreCase) ? "Player" : "Unknown";
        return name.StartsWith('@') || Guid.TryParse(name, out _) ? "Unknown" : name;
    }
    public static string NonLethal(long count) => count > 0 ? $" ({count:N0} non-lethal)" : "";
    public static string Relationship(ActorRelationship value) => value switch { ActorRelationship.Self => "You", ActorRelationship.Party => "Party", ActorRelationship.Other => "Other", _ => "Unknown" };
    public static string State(ActivityState state, ActivityEndReason reason) => state == ActivityState.Open ? "Active"
        : state == ActivityState.Interrupted ? "Interrupted" : reason == ActivityEndReason.Finished ? "Finished" : "Ended";
}

public sealed class ActivityResourceRow(ActivityResourceTotal source)
{
    public string ItemUniqueName => source.UniqueName ?? "";
    public string ItemName => ActivityDisplay.ItemName(source.Name);
    public int ImageQuality => source.Quality ?? 1;
    public string Method => source.Source switch { ActivityResourceMethod.Gathering => "Gathering", ActivityResourceMethod.Fishing => "Fishing", _ => "Gathered resources" };
    public string QuantityText => UiFormat.Compact(source.Quantity);
    public string ValueText => source.KnownValue == 0 && source.MissingPriceItems > 0 ? "Unavailable" : UiFormat.Compact(source.KnownValue);
    public bool HasMissingPrices => source.MissingPriceItems > 0;
    public string ValueLabel => "Total EMV";
    public string MissingPricesText => source.MissingPriceItems > 0 ? $"{source.MissingPriceItems:N0} without EMV" : "";
    public string Details => $"{ItemName} · {ItemQuality.Format(source.Quality)}\n{QuantityText} items · EMV {ValueText} silver" +
        (JournalValueText is { } journal ? $"\n{journal}" : "") +
        (HasMissingPrices ? $"\n{MissingPricesText}" : "");

    // A filled journal is worth the full journal minus the empty one it replaced.
    private string? JournalValueText
    {
        get
        {
            if (source.FullJournalValue <= 0) return null;
            var journals = UiFormat.Noun(source.Quantity - source.MissingPriceItems, "journal");
            return $"Full {journals} EMV {UiFormat.Compact(source.FullJournalValue)} − empty {journals} EMV {UiFormat.Compact(source.EmptyJournalValue)}" +
                $" = {UiFormat.Compact(source.KnownValue)} silver";
        }
    }
}
