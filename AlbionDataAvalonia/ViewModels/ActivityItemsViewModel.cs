using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AlbionDataAvalonia.ViewModels.ActivityReads;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>The Activity page's Items tab: every recorded pickup and gathered item, with its own scope, period and search.</summary>
public partial class ActivityItemsViewModel : ObservableObject, IDisposable
{
    private readonly ActivityViewModel page;
    private CancellationTokenSource itemsQueryCancellation = new();
    private int globalLootPage;
    private long globalLootVersion;
    private long itemReadVersion;
    private ActivityQuery? displayedGlobalLootQuery;

    [ObservableProperty] private Option<ActivityScope> selectedLootScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGlobalLootPanel), nameof(ShowGlobalLootList), nameof(GlobalLootTotalsText))]
    private Option<ActivityItemSources> selectedItemSource = new(ActivityItemSources.All, "All items");
    [ObservableProperty] private string lootSearch = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalLootTotalsText))]
    private ActivitySummaryPresentation globalLootSummary = ActivitySummaryPresentation.Empty;
    [ObservableProperty] private ObservableCollection<ActivityLootRow> globalLoot = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGlobalResources))]
    private ObservableCollection<ActivityResourceRow> globalResources = new();
    [ObservableProperty] private bool hasMoreGlobalLoot;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGlobalLootList))]
    private bool hasGlobalLoot;
    [ObservableProperty] private bool hasNoItems;

    internal ActivityItemsViewModel(ActivityViewModel page)
    {
        this.page = page;
        Time.Changed += ReloadIfShowing;
    }

    public ActivityFilterState Filters => page.Filters;
    public ActivityLootExport Export => page.Export;
    public ActivityTimeFilter Time { get; } = new();
    public bool HasGlobalResources => GlobalResources.Count > 0;
    public bool ShowGlobalLootPanel => (SelectedItemSource.Value & ActivityItemSources.Loot) != 0;
    // Like Gathered, the loot list hides when nothing matches.
    public bool ShowGlobalLootList => ShowGlobalLootPanel && HasGlobalLoot;
    public string GlobalLootTotalsText => string.Join(" · ", new[]
    {
        ShowGlobalLootPanel ? $"Loot {UiFormat.Compact(GlobalLootSummary.Summary.LootItems)} {UiFormat.Noun(GlobalLootSummary.Summary.LootItems, "item")} · EMV {GlobalLootSummary.LootValue}" : null,
        SelectedItemSource.Value != ActivityItemSources.Loot ? $"Gathered {GlobalLootSummary.ResourceQuantityText} · EMV {GlobalLootSummary.ResourceValueText}" : null
    }.Where(value => value is not null));
    public string GlobalLootDetailsText => "Loot uses the Black Market EMV when available";

    // A period moves on at midnight, so the list reloads then even when no new items arrive.
    internal bool IsStale => itemReadVersion != page.ItemChangeVersion ||
        displayedGlobalLootQuery is { } shown && Time.Window(DateTime.UtcNow) != (shown.FromUtc, shown.ToUtc);

    internal Task RefreshAsync() => IsStale ? LoadGlobalLootAsync(false, true) : Task.CompletedTask;

    internal void OnScopeChanged(Option<ActivityScope> scope)
    {
        ++globalLootVersion;
        if (SelectedLootScope.Value != ActivityScope.AllObserved) SelectedLootScope = scope;
    }

    internal void ResumeReads()
    {
        if (itemsQueryCancellation.IsCancellationRequested) BeginRead(ref itemsQueryCancellation);
    }

    internal void CancelReads() => itemsQueryCancellation.Cancel();

    private async Task LoadGlobalLootAsync(bool append, bool refresh = false)
    {
        if (page.Persistence is not { } persistence) return;
        var version = ++globalLootVersion;
        var dataVersion = page.ItemChangeVersion;
        var cancellationToken = BeginRead(ref itemsQueryCancellation);
        var pageIndex = append ? globalLootPage + 1 : refresh ? globalLootPage : 0;
        var query = GlobalItemsQuery();
        var result = await ReadEventRowsAsync(persistence, query with { LootOnly = true }, pageIndex, append, cancellationToken);
        var summary = append ? null : await persistence.GetSummaryAsync(query, cancellationToken);
        var resources = append ? null : await persistence.GetResourceTotalsAsync(query, cancellationToken);
        if (page.IsDisposed || version != globalLootVersion || GlobalItemsQuery() != query) return;
        ApplyEventRows(GlobalLoot, result.Items.Select(observation => new ActivityLootRow(observation)).ToArray(), row => row.SourceEvent, append);
        displayedGlobalLootQuery = query with { LootOnly = true };
        page.Valuations?.RequestMissing(result.Items);
        if (resources is not null) GlobalResources = new(resources.Select(row => new ActivityResourceRow(row)));
        globalLootPage = pageIndex;
        HasMoreGlobalLoot = result.HasMore;
        if (summary is not null) GlobalLootSummary = new(summary, ActivityProjection.GetRates(summary, 0));
        if (!append)
        {
            itemReadVersion = dataVersion;
            HasGlobalLoot = GlobalLoot.Count > 0;
            HasNoItems = !HasGlobalLoot && GlobalResources.Count == 0;
        }
    }

    // The export reads the query the list last showed, so it keeps the same period and filters.
    private ActivityQuery GlobalItemsQuery()
    {
        var (fromUtc, toUtc) = Time.Window(DateTime.UtcNow);
        return new()
        {
            Scope = SelectedLootScope.Value,
            ItemsOnly = true,
            ItemSources = SelectedItemSource.Value,
            Search = EmptyToNull(LootSearch),
            FromUtc = fromUtc,
            ToUtc = toUtc
        };
    }

    private void ReloadIfShowing()
    {
        itemReadVersion = -1;
        ++globalLootVersion;
        globalLootPage = 0;
        if (page.IsShowing(ActivityTab.Items)) _ = page.RunSafelyAsync(() => LoadGlobalLootAsync(false));
    }

    partial void OnSelectedLootScopeChanged(Option<ActivityScope> value) => ReloadIfShowing();
    partial void OnSelectedItemSourceChanged(Option<ActivityItemSources> value) => ReloadIfShowing();

    [RelayCommand] private Task ApplyLootFiltersAsync() => page.RunSafelyAsync(() => LoadGlobalLootAsync(false));
    [RelayCommand] private Task LoadMoreGlobalLootAsync() => page.RunSafelyAsync(() => LoadGlobalLootAsync(true));
    [RelayCommand]
    private Task ExportGlobalLoot(LootExportFormat format) =>
        Export.ExportToFileAsync(Export.CanStart && ShowGlobalLootPanel ? displayedGlobalLootQuery : null, format);

    public void Dispose()
    {
        Time.Changed -= ReloadIfShowing;
        itemsQueryCancellation.Cancel();
        itemsQueryCancellation.Dispose();
    }
}
