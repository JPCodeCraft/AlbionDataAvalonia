using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;

using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System;

using System.Linq;

using System.Threading;

using System.Threading.Tasks;



namespace AlbionDataAvalonia.ViewModels;



public partial class ActivityViewModel

{

    private bool changingVisibility;
    private ActivityQuery? currentResourcesQuery;
    private long currentResourcesReadVersion;

    public IReadOnlyList<ActivityOption<ActivityItemSources>> ItemSourceOptions { get; } =

        [new(ActivityItemSources.All, "All items"), new(ActivityItemSources.Loot, "Loot"),

         new(ActivityItemSources.Gathering, "Gathering"), new(ActivityItemSources.Fishing, "Fishing")];

    [ObservableProperty] private ActivityOption<ActivityItemSources> selectedItemSource = new(ActivityItemSources.All, "All items");

    [ObservableProperty] private ActivityOption<ActivityItemSources> selectedSessionItemSource = new(ActivityItemSources.All, "All items");

    [ObservableProperty] private bool showAllActivities = true;

    [ObservableProperty] private bool showGathering;

    [ObservableProperty] private bool showFishing;

    [ObservableProperty] private bool showPve;

    [ObservableProperty] private bool showPvp;

    [ObservableProperty] private bool showOther;

    [ObservableProperty] private ActivityTimelineChartMode chartMode;
    [ObservableProperty] private ActivityLegacyImportInfo? legacyImportInfo;

    [ObservableProperty] private ObservableCollection<ActivityResourceRow> sessionResources = new();

    [ObservableProperty] private ObservableCollection<ActivityResourceRow> globalResources = new();
    [ObservableProperty] private ObservableCollection<ActivityResourceRow> currentResources = new();



    public ActivityCategories VisibleCategories => ShowAllActivities ? ActivityCategories.All :

        (ShowGathering ? ActivityCategories.Gathering : ActivityCategories.None)

        | (ShowFishing ? ActivityCategories.Fishing : ActivityCategories.None)

        | (ShowPve ? ActivityCategories.Pve : ActivityCategories.None)

        | (ShowPvp ? ActivityCategories.Pvp : ActivityCategories.None)

        | (ShowOther ? ActivityCategories.Other : ActivityCategories.None);

    public string CurrentTagsText => ActivityDisplay.Tags(CurrentSummary.Summary.Tags);

    public string DetailTagsText => IsShowingMap ? ActivityDisplay.Tags(DetailSummary.Summary.Tags)
        : SelectedSession is { } row ? ActivityDisplay.Tags(Activity.ActivityProjection.GetSummary(row.Source, SelectedScope.Value).Tags) : "";

    public bool IsImportedDetail => SelectedSession is { } row && Activity.ActivityProjection.GetSummary(row.Source, SelectedScope.Value).IsLegacyAggregate;

    public bool HasDetailedHistory => !IsImportedDetail;

    public string ImportedDetailText => IsImportedDetail ? "Imported gathering totals" : "";

    public string ImportedDetailTooltipText => IsImportedDetail

        ? "Only personal gathering totals are available. Individual pickup times and combat history were not recorded." + (LegacyImportInfo?.HasMismatch == true ? $" Saved totals: {LegacyImportInfo.OriginalTotalAmount:N0} items, value {LegacyImportInfo.OriginalTotalValue:N0}. Item rows differ from these totals." : "") : "";

    public bool HasLegacyImportWarning => persistence?.LastLegacyImportResult?.Errors.Count > 0;
    public string LegacyImportWarningText => "Some gathering history could not be imported. Your original records are safe.";
    public bool HasSessionResources => SessionResources.Count > 0;

    public bool HasGlobalResources => GlobalResources.Count > 0;
    public bool HasCurrentResources => CurrentResources.Count > 0;
    public bool ShowGlobalLootPanel => (SelectedItemSource.Value & ActivityItemSources.Loot) != 0;
    public bool ShowSessionLootPanel => (SelectedSessionItemSource.Value & ActivityItemSources.Loot) != 0 && !IsImportedDetail;



    partial void OnLegacyImportInfoChanged(ActivityLegacyImportInfo? value) => OnPropertyChanged(nameof(ImportedDetailTooltipText));
    partial void OnSelectedItemSourceChanged(ActivityOption<ActivityItemSources> value)

    { OnPropertyChanged(nameof(ShowGlobalLootPanel)); ++itemChangeVersion; ++globalLootVersion; globalLootPage = 0; if (isActive && SelectedTabIndex == 2) _ = RunSafelyAsync(() => LoadGlobalLootAsync(false)); }

    partial void OnSelectedSessionItemSourceChanged(ActivityOption<ActivityItemSources> value)

    { OnPropertyChanged(nameof(CanExportSessionLoot)); OnPropertyChanged(nameof(ShowSessionLootPanel)); ++detailChangeVersion; ++detailVersion; sessionLootPage = 0; if (isActive && SelectedTabIndex == 1 && SelectedDetailTabIndex == 4) _ = RunSafelyAsync(RefreshDetailsAsync); }

    partial void OnSessionResourcesChanged(ObservableCollection<ActivityResourceRow> value) => OnPropertyChanged(nameof(HasSessionResources));

    partial void OnGlobalResourcesChanged(ObservableCollection<ActivityResourceRow> value) => OnPropertyChanged(nameof(HasGlobalResources));
    partial void OnCurrentResourcesChanged(ObservableCollection<ActivityResourceRow> value) => OnPropertyChanged(nameof(HasCurrentResources));

    partial void OnShowAllActivitiesChanged(bool value) => ChangeVisibility(value, true);

    partial void OnShowGatheringChanged(bool value) => ChangeVisibility(value);

    partial void OnShowFishingChanged(bool value) => ChangeVisibility(value);

    partial void OnShowPveChanged(bool value) => ChangeVisibility(value);

    partial void OnShowPvpChanged(bool value) => ChangeVisibility(value);

    partial void OnShowOtherChanged(bool value) => ChangeVisibility(value);



    private void ChangeVisibility(bool selected, bool all = false)

    {

        if (changingVisibility) return;

        changingVisibility = true;

        try

        {

            if (all && selected) ShowGathering = ShowFishing = ShowPve = ShowPvp = ShowOther = false;

            else if (!all && selected) ShowAllActivities = false;

            if (!ShowGathering && !ShowFishing && !ShowPve && !ShowPvp && !ShowOther) ShowAllActivities = true;

            if (!ShowAllActivities && !ShowOther && ChartMode != ActivityTimelineChartMode.Fame)

            {

                if ((ShowGathering || ShowFishing) && !ShowPve && !ShowPvp) ChartMode = ActivityTimelineChartMode.Gathering;

                else if ((ShowPve || ShowPvp) && !ShowGathering && !ShowFishing) ChartMode = ActivityTimelineChartMode.Combat;

            }

        }

        finally { changingVisibility = false; }

        OnPropertyChanged(nameof(VisibleCategories));
        liveTimelineCancellation.Cancel();
        ++detailChangeVersion; ++detailVersion;

        if (isActive && SelectedTabIndex == 0) _ = RefreshLiveTimelineSafelyAsync();

        else if (isActive && SelectedTabIndex == 1 && SelectedDetailTabIndex is 1 or 5) _ = RunSafelyAsync(RefreshDetailsAsync);

    }



    private async Task RefreshCurrentResourcesAsync(Guid? sessionId, ActivityScope scope, CancellationToken cancellationToken)
    {
        if (persistence is null) return;
        if (sessionId is not { } id)
        {
            if (CurrentResources.Count > 0) CurrentResources = new();
            currentResourcesQuery = null;
            return;
        }

        var query = new ActivityQuery { SessionId = id, Scope = scope };
        var dataVersion = itemChangeVersion;
        if (query == currentResourcesQuery && dataVersion == currentResourcesReadVersion) return;
        if (query != currentResourcesQuery)
        {
            if (CurrentResources.Count > 0) CurrentResources = new();
            currentResourcesQuery = null;
        }
        var rows = await persistence.GetResourceTotalsAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (isDisposed || !isActive || SelectedTabIndex != 0 || tracker?.CurrentSessionId != id || SelectedScope.Value != scope) return;
        CurrentResources = new(rows.Select(row => new ActivityResourceRow(row)));
        currentResourcesQuery = query;
        currentResourcesReadVersion = dataVersion;
    }

    private async Task RefreshSessionResourcesAsync(ActivityQuery query, long version)

    {

        if (persistence is null || query.SessionId is not { } id) return;

        var rows = await persistence.GetResourceTotalsAsync(query, detailQueryCancellation.Token);

        if (IsCurrentDetail(version, id) && DetailQuery(true) == query) SessionResources = new(rows.Select(row => new ActivityResourceRow(row)));

    }

}

