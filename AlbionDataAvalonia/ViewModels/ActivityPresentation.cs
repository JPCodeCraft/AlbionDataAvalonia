using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Network.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AlbionDataAvalonia.ViewModels;

public sealed record ActivityOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

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
        ? $"{ActivityDisplay.Number((Summary.Silver + Summary.LootValue + Summary.ResourceValue) * 3600 / RunSeconds!.Value)} / h in runs" : "";

    public ActivitySummary Summary { get; }
    public bool IsImported => Summary.IsLegacyAggregate;
    public ActivityRates Rates { get; }
    public bool IsSessionAverage { get; }
    public bool HasRecordedDuration { get; }
    public string DamageDealt => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.DamageDealt);
    public string DamageReceived => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.DamageReceived);
    public string HealingDone => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.HealingDone);
    public string HealingReceived => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.HealingReceived);
    public string Fame => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.Fame);
    public string CombatFame => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.CombatFame);
    public string GatheringFame => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.GatheringFame + Summary.FishingFame);
    public string UnknownFame => ActivityDisplay.Number(Summary.UnclassifiedFame);
    public bool HasUnknownFame => Summary.UnclassifiedFame > 0 && !Summary.IsLegacyAggregate;
    public bool HasPvpFame => Summary.PvpFame > 0;
    public string TotalSilver => ActivityDisplay.Number(Summary.Silver + Summary.LootValue + Summary.ResourceValue);
    public string TotalSilverPerHour => HasRecordedDuration ? ActivityDisplay.Number(Rates.SilverPerHour + Rates.LootValuePerHour + Rates.ResourceValuePerHour) : "Unavailable";
    public string TotalSilverDetailsText => (IsImported
        ? "Imported gathering and fishing value; silver pickups and loot were not recorded."
        : "Silver picked up + estimated loot, gathering and fishing value.")
        + (HasMissingItemPrices || HasUnknownSilver ? "\nItems without prices and silver pickups with unknown amounts are excluded." : "");
    public string Silver => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.Silver);
    public string LootValue => Summary.LootValue == 0 && Summary.MissingLootPriceCount > 0 ? "Unavailable" : ActivityDisplay.Number(Summary.LootValue);
    public string LootItemsText => $"{Summary.LootItems:N0} items picked up";
    public string MobKills => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.MobKills);
    public string PlayerKills => Summary.IsLegacyAggregate ? "Unavailable" : ActivityDisplay.Number(Summary.PlayerKills);
    public string DamageRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{Rates.DamagePerSecond:N1} DPS";
    public string DamageReceivedRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{Rates.DamageReceivedPerSecond:N1} DPS";
    public string HealingRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{Rates.HealingPerSecond:N1} HPS";
    public string HealingReceivedRate => Summary.IsLegacyAggregate ? "Not recorded" : $"{Rates.HealingReceivedPerSecond:N1} HPS";
    public string FameRate => Summary.IsLegacyAggregate || !HasRecordedDuration ? "Not recorded" : $"{Rates.FamePerHour:N0} / hour";
    public string CombatFameRate => FameRateText(Summary.CombatFame);
    public string GatheringFameRate => FameRateText(Summary.GatheringFame + Summary.FishingFame);
    public string CombatFameDetailsText => HasPvpFame ? $"PvE {ActivityDisplay.Number(Summary.PveFame)} · PvP {ActivityDisplay.Number(Summary.PvpFame)}" : "Your combat fame";
    public string GatheringFameDetailsText => Summary.FishingFame > 0 ? $"Gathering {ActivityDisplay.Number(Summary.GatheringFame)} · Fishing {ActivityDisplay.Number(Summary.FishingFame)}" : "Your gathering fame";
    public string CombatFameFooterText => (IsSessionAverage ? CombatFameRate : "")
        + (HasPvpFame ? $"{(IsSessionAverage ? " · " : "")}PvP {ActivityDisplay.Number(Summary.PvpFame)}" : "");
    public string UnknownFameText => $"Unknown fame {UnknownFame}";
    public string SilverRate => Summary.IsLegacyAggregate || !HasRecordedDuration ? "Not recorded" : $"{Rates.SilverPerHour:N0} / hour";
    public string LootValueRate => HasRecordedDuration ? ValueRateText(Summary.LootValue, Summary.MissingPriceItems, Rates.LootValuePerHour) : "Hourly rate unavailable";
    public bool HasUnknownSilver => Summary.UnknownSilverCount > 0;
    public bool HasMissingLootPrices => Summary.MissingLootPriceCount > 0;
    public string UnknownSilver => $"{Summary.UnknownSilverCount:N0} pickups with an unknown amount";
    public string SilverFooterText => (IsSessionAverage ? SilverRate : "")
        + (HasUnknownSilver ? $"{(IsSessionAverage ? " · " : "")}{Summary.UnknownSilverCount:N0} unknown" : "");
    public string SilverDetailsText => "Silver received after taxes and penalties"
        + (HasUnknownSilver ? $"\n{UnknownSilver}" : "");
    public string MissingLootPrices => $"{Summary.MissingLootPriceCount:N0} items without prices";
    // Non-lethal kills and deaths count in the totals and are labelled wherever they appear.
    public string PlayerKillsLabel => Summary.NonLethalPlayerKills <= 0 ? "PvP kills"
        : Summary.NonLethalPlayerKills == Summary.PlayerKills ? "PvP kills · non-lethal" : $"PvP kills · {Summary.NonLethalPlayerKills:N0} non-lethal";
    public string DeathsAndKnockdowns => Summary.IsLegacyAggregate ? "" : string.Join(" · ", new[]
    {
        Summary.Deaths <= 0 ? null : Summary.NonLethalDeaths == Summary.Deaths
            ? $"{Summary.Deaths:N0} non-lethal {(Summary.Deaths == 1 ? "death" : "deaths")}"
            : $"{Summary.Deaths:N0} {(Summary.Deaths == 1 ? "death" : "deaths")}{ActivityDisplay.NonLethal(Summary.NonLethalDeaths)}",
        Summary.Knockdowns > 0 ? $"{Summary.Knockdowns:N0} {(Summary.Knockdowns == 1 ? "knockdown" : "knockdowns")}" : null
    }.Where(value => value is not null));
    public string OutcomesDetailsText => $"Mob kills: {MobKills}\nPvP kills: {PlayerKills}{ActivityDisplay.NonLethal(Summary.NonLethalPlayerKills)}" +
        $"\nDeaths: {Summary.Deaths:N0}{ActivityDisplay.NonLethal(Summary.NonLethalDeaths)}\nKnockdowns: {Summary.Knockdowns:N0}" +
        (Summary.NonLethalPlayerKills + Summary.NonLethalDeaths > 0 ? "\nNon-lethal: deaths reported without the lethal flag, such as in arenas" : "");
    public bool HasResources => Summary.ResourceItems > 0;
    public string ItemsValue => LootValue;
    public bool HasMissingItemPrices => Summary.MissingPriceItems + Summary.MissingResourcePriceItems > 0;
    public string MissingItemPrices => $"{Summary.MissingPriceItems + Summary.MissingResourcePriceItems:N0} items without prices";
    public string ResourceItemsText => $"{Summary.ResourceItems:N0} items gathered";
    public string ResourceQuantityText => ActivityDisplay.Number(Summary.ResourceItems);
    public string ResourceBreakdownText => $"Gathering {Summary.GatheringItems:N0} · Fishing {Summary.FishingItems:N0}" + (Summary.UnclassifiedResourceItems > 0 ? $" · Other {Summary.UnclassifiedResourceItems:N0}" : "");
    public string ResourceValueText => Summary.ResourceValue == 0 && Summary.MissingResourcePriceItems > 0 ? "Unavailable" : ActivityDisplay.Number(Summary.ResourceValue);
    public string ResourceRateText => HasRecordedDuration ? $"{Rates.ResourceItemsPerHour:N0} items / hour · {ValueRateText(Summary.ResourceValue, Summary.MissingResourcePriceItems, Rates.ResourceValuePerHour)}" : "Hourly rate unavailable";
    public string ItemsValueRate => HasRecordedDuration ? ValueRateText(Summary.LootValue + Summary.ResourceValue, Summary.MissingPriceItems + Summary.MissingResourcePriceItems, Rates.LootValuePerHour + Rates.ResourceValuePerHour) : "Hourly rate unavailable";
    public string ItemsCountText => $"{Summary.LootItems + Summary.ResourceItems:N0} items acquired";
    public string ItemsBreakdownText => Summary.IsLegacyAggregate ? "Imported gathering" : $"Loot {LootValue} · Gathering {ResourceValueText}";
    public string ItemsEstimateText => "Loot value";
    public string ItemsFooterText => $"{Summary.LootItems:N0} items"
        + (IsSessionAverage && HasRecordedDuration && (Summary.LootValue > 0 || !HasMissingLootPrices) ? $" · {Rates.LootValuePerHour:N0} / h" : "")
        + (HasMissingLootPrices ? " *" : "");
    public string ItemsDetailsText => "Estimated value of loot only" + (HasMissingLootPrices ? $"\n{MissingLootPrices}" : "");
    public string ResourceValueDetailsText => ResourceRateText + (Summary.MissingResourcePriceItems > 0 ? $"\n{Summary.MissingResourcePriceItems:N0} items without prices" : "");
    private static string ValueRateText(long knownValue, long missingItems, double rate) => missingItems > 0 && knownValue == 0
        ? "Value / hour unavailable" : $"{rate:N0} estimated silver / hour";
    private string FameRateText(double amount) => Summary.IsLegacyAggregate || !HasRecordedDuration ? "Not recorded" : $"{ActivityProjection.GetFameRate(amount, Rates):N0} / hour";

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
        FameText = ActivityDisplay.Number(summary.Fame);
        SilverText = ActivityDisplay.Number(summary.Silver);
        LootValueText = ActivityDisplay.Number(summary.LootValue);
        OnPropertyChanged(string.Empty);
    }

    public void RefreshDuration() => OnPropertyChanged(nameof(DurationText));

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
    public string TagsText => ActivityDisplay.Tags(summary?.Tags ?? ActivityTags.None);
    public string RunLabel => ActivityDisplay.RunLabel(Source);
    public string TotalSilverText => ActivityDisplay.Number((summary?.Silver ?? 0) + (summary?.LootValue ?? 0) + (summary?.ResourceValue ?? 0));
    public string RunSummaryText => $"{DurationText} · {TotalSilverText} silver";
    public string RunDetailsText => $"{Title}\n{ActivityDisplay.LocalTime(Source.StartedAtUtc)} · {DurationText}\nSilver and items {TotalSilverText}";
    // The run the tracker has open now.
    private bool isLive;
    public bool IsLive { get => isLive; set => SetProperty(ref isLive, value); }
    private ActivityScope scope;
    private ActivitySummary? summary;
    public bool IsImported => summary?.IsLegacyAggregate == true;
    public string EarningsText
    {
        get
        {
            var value = summary ?? new ActivitySummary();
            var amounts = new ActivitySummaryPresentation(value, ActivityProjection.GetRates(value, 0));
            return IsImported ? $"{value.ResourceItems:N0} items · estimated value {amounts.ResourceValueText}"
                : $"Fame {FameText} · silver {SilverText} · item value {amounts.ItemsValue}{(amounts.HasMissingItemPrices ? " *" : "")}";
        }
    }
    public string StateText => $"{ActivityDisplay.State(Source.State, Source.EndReason)}{(Source.IsPartial && Source.State != ActivityState.Interrupted ? " · incomplete" : "")}{(IsImported ? " · imported" : "")}";

    // Party members are read separately; a changed header re-reads them.
    private ActivitySession? partySource;
    private IReadOnlyList<string> partyMembers = [];
    public bool NeedsParty => partySource != Source && !IsImported;
    public bool HasParty => partyMembers.Count > 0;
    public string PartyText => ActivityPartyText.With(partyMembers);
    public string PartyDetailsText => HasParty ? $"Party members in this session ({partyMembers.Count:N0})\n{ActivityPartyText.Members(partyMembers)}" : "";

    public void SetParty(ActivitySession source, IReadOnlyList<string> members)
    {
        partySource = source;
        if (partyMembers.SequenceEqual(members)) return;
        partyMembers = members;
        OnPropertyChanged(nameof(HasParty));
        OnPropertyChanged(nameof(PartyText));
        OnPropertyChanged(nameof(PartyDetailsText));
    }
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
        DamageText = ActivityDisplay.Number(summary.DamageDealt);
        HealingText = ActivityDisplay.Number(summary.HealingDone);
        DamageRateText = rates.DamagePerSecond.ToString("N1", CultureInfo.CurrentCulture);
        HealingRateText = rates.HealingPerSecond.ToString("N1", CultureInfo.CurrentCulture);
        OnPropertyChanged(string.Empty);
    }

    private ActivityScope scope;
    private bool hasSummary;

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
    public string Name => ActivityDisplay.ActorName(Source.Name);
    public string Relationship => ActivityDisplay.Relationship(Source.Relationship);
    public string DamageDealt => ActivityDisplay.Number(Source.DamageDealt);
    public string DamageReceived => ActivityDisplay.Number(Source.DamageReceived);
    public string HealingDone => ActivityDisplay.Number(Source.HealingDone);
    public string HealingReceived => ActivityDisplay.Number(Source.HealingReceived);
    // Earnings stay blank when nothing was recorded; the game reports fame only for you.
    public string Fame => Source.Fame > 0 ? ActivityDisplay.Number(Source.Fame) : "";
    public string Silver => Source.SilverRaw > 0 ? ActivityDisplay.Number(Source.SilverRaw / 10000d) : "";
    public string Loot => ItemsText(Source.LootItems, Source.LootValue);
    public string Gathered => ItemsText(Source.GatheredItems, Source.GatheredValue);
    public string Deaths => Source.Deaths > 0 ? Source.Deaths.ToString("N0", CultureInfo.CurrentCulture) + ActivityDisplay.NonLethal(Source.NonLethalDeaths) : "";
    private static string ItemsText(long items, long value) => items <= 0 ? ""
        : $"{(value > 0 ? ActivityDisplay.Number(value) : "–")} ({items.ToString("N0", CultureInfo.CurrentCulture)})";
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
    public string Count => ActivityDisplay.Number(source.Kills);
    public string InferredCount => ActivityDisplay.Number(source.InferredKills);
}

public sealed class ActivityLootRow(ActivityEvent source)
{
    public ActivityEvent SourceEvent { get; } = source;
    public Guid Id => source.Id;
    public bool IsImported => source.Kind == ActivityEventKind.LegacyGatheringAggregate;
    public string TimeText => IsImported ? "Session total" : ActivityDisplay.LocalTime(source.CapturedAtUtc);
    public string DateTimeText => ActivityDisplay.LocalDate(source.CapturedAtUtc) + (IsImported ? " · session total" : "");
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
    public string AmountText => source.Quantity > 0 ? ActivityDisplay.Number(source.Quantity) : source.Evidence == ActivityEvidence.Supplemental ? "Pending" : "Unknown";
    public string UnitValueText => source.EstimatedUnitValue is { } value ? ActivityDisplay.Number(value) : "Unavailable";
    public string ValueText => source.Quantity <= 0 ? source.Evidence == ActivityEvidence.Supplemental ? "Quantity pending" : "Unknown quantity" : ActivityProjection.GetLootValue(source) is { } value ? ActivityDisplay.Number(value) : "Unavailable";
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
        ? "Price unavailable" : $"Estimated value each: {UnitValueText} silver")
        + (source.EstimatedUnitValue is not null && source.Quality is null
            ? source.Kind == ActivityEventKind.Loot ? "\nAverage estimate across qualities 1-4" : "\nNormal-quality estimate" : "")
        + (source.Evidence != ActivityEvidence.Confirmed ? "\nNot included in totals" : "");
    public string RecipientDetails => $"{Recipient} · {Relationship}{(string.IsNullOrWhiteSpace(Guild) ? "" : $"\nGuild: {Guild}")}{(string.IsNullOrWhiteSpace(Alliance) ? "" : $"\nAlliance: {Alliance}")}";
    public string ContextDetails => Location;
}

public sealed class ActivityEventRow(ActivityEvent source)
{
    public ActivityEvent SourceEvent { get; } = source;
    public Guid Id => source.Id;
    public string TimeText => ActivityDisplay.LocalTime(source.CapturedAtUtc);
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
        ActivityEventKind.Damage or ActivityEventKind.Healing or ActivityEventKind.Fame => ActivityDisplay.Number(source.Amount),
        ActivityEventKind.Silver => source.HasKnownNetSilver ? $"{source.SilverRaw / 10000d:N0} silver" : "Amount unknown",
        ActivityEventKind.Loot or ActivityEventKind.Gathered or ActivityEventKind.FishingReward or ActivityEventKind.LegacyGatheringAggregate => source.Quantity > 0 ? $"{source.Quantity:N0} × {ActivityDisplay.ItemName(source.ItemName)}" : $"Quantity unknown · {ActivityDisplay.ItemName(source.ItemName)}",
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
    public static string Number(double value) => value.ToString("N0", CultureInfo.CurrentCulture);
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
    public static string LocalDate(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public static string LocalTime(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
    public static string Duration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalDays >= 1 ? $"{(int)duration.TotalDays}d {duration:hh\\:mm\\:ss}" : $"{(int)duration.TotalHours}:{duration:mm\\:ss}";
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
    public string QuantityText => ActivityDisplay.Number(source.Quantity);
    public string ValueText => source.KnownValue == 0 && source.MissingPriceItems > 0 ? "Unavailable" : ActivityDisplay.Number(source.KnownValue);
    public bool HasMissingPrices => source.MissingPriceItems > 0;
    public string ValueLabel => "Total EMV";
    public string MissingPricesText => source.MissingPriceItems > 0 ? $"{source.MissingPriceItems:N0} without prices" : "";
    public string Details => $"{ItemName} · {ItemQuality.Format(source.Quality)}\n{QuantityText} items · estimated value {ValueText} silver" +
        (HasMissingPrices ? $"\n{MissingPricesText}" : "");
}
