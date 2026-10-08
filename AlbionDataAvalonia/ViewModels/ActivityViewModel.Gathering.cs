using AlbionDataAvalonia.Activity.Models;

using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.Linq;

using System.Threading.Tasks;



namespace AlbionDataAvalonia.ViewModels;



public partial class ActivityViewModel

{

    private bool changingVisibility;

    public IReadOnlyList<ActivityOption<ActivityItemSources>> ItemSourceOptions { get; } =

        [new(ActivityItemSources.All, "All items"), new(ActivityItemSources.Loot, "Loot"),

         new(ActivityItemSources.Gathering, "Gathering"), new(ActivityItemSources.Fishing, "Fishing")];

    [ObservableProperty] private ActivityOption<ActivityItemSources> selectedItemSource = new(ActivityItemSources.All, "All items");

    [ObservableProperty] private ActivityOption<ActivityItemSources> selectedSessionItemSource = new(ActivityItemSources.All, "All items");

    [ObservableProperty] private bool historyGathering;

    [ObservableProperty] private bool historyPve;

    [ObservableProperty] private bool historyPvp;

    [ObservableProperty] private bool showAllActivities = true;

    [ObservableProperty] private bool showGathering;

    [ObservableProperty] private bool showFishing;

    [ObservableProperty] private bool showPve;

    [ObservableProperty] private bool showPvp;

    [ObservableProperty] private bool showOther;

    [ObservableProperty] private bool isGatheringChart;
    [ObservableProperty] private ActivityLegacyImportInfo? legacyImportInfo;

    [ObservableProperty] private ObservableCollection<ActivityResourceRow> sessionResources = new();

    [ObservableProperty] private ObservableCollection<ActivityResourceRow> globalResources = new();



    public ActivityTags HistoryTags => (HistoryGathering ? ActivityTags.Gathering : ActivityTags.None)

        | (HistoryPve ? ActivityTags.Pve : ActivityTags.None) | (HistoryPvp ? ActivityTags.Pvp : ActivityTags.None);

    public ActivityCategories VisibleCategories => ShowAllActivities ? ActivityCategories.All :

        (ShowGathering ? ActivityCategories.Gathering : ActivityCategories.None)

        | (ShowFishing ? ActivityCategories.Fishing : ActivityCategories.None)

        | (ShowPve ? ActivityCategories.Pve : ActivityCategories.None)

        | (ShowPvp ? ActivityCategories.Pvp : ActivityCategories.None)

        | (ShowOther ? ActivityCategories.Other : ActivityCategories.None);

    public string CurrentTagsText => ActivityDisplay.Tags(CurrentSummary.Summary.Tags);

    public string DetailTagsText => SelectedSession is { } row ? ActivityDisplay.Tags(Activity.ActivityProjection.GetSummary(row.Source, SelectedScope.Value).Tags) : "";

    public bool IsImportedDetail => SelectedSession is { } row && Activity.ActivityProjection.GetSummary(row.Source, SelectedScope.Value).IsLegacyAggregate;

    public bool HasDetailedHistory => !IsImportedDetail;

    public string ImportedDetailText => IsImportedDetail

        ? "Imported summary · Individual pickup times, location, combat, fame, and cash silver were not recorded. Party view contains the recorded personal activity only." + (LegacyImportInfo?.HasMismatch == true ? $" Original totals: {LegacyImportInfo.OriginalTotalAmount:N0} items, value {LegacyImportInfo.OriginalTotalValue:N0}. Some item rows differ from these totals; original data was preserved." : "") : "";

    public bool HasLegacyImportWarning => persistence?.LastLegacyImportResult?.Errors.Count > 0;
    public string LegacyImportWarningText => "Some gathering history could not be converted. Original records are intact; see Logs for details. Conversion will retry at the next start.";
    public bool HasSessionResources => SessionResources.Count > 0;

    public bool HasGlobalResources => GlobalResources.Count > 0;



    partial void OnLegacyImportInfoChanged(ActivityLegacyImportInfo? value) => OnPropertyChanged(nameof(ImportedDetailText));
    partial void OnSelectedItemSourceChanged(ActivityOption<ActivityItemSources> value)

    { ++itemChangeVersion; ++globalLootVersion; globalLootPage = 0; if (isActive && SelectedTabIndex == 2) _ = RunSafelyAsync(() => LoadGlobalLootAsync(false)); }

    partial void OnSelectedSessionItemSourceChanged(ActivityOption<ActivityItemSources> value)

    { ++detailChangeVersion; ++detailVersion; sessionLootPage = 0; if (isActive && SelectedTabIndex == 1 && SelectedDetailTabIndex == 4) _ = RunSafelyAsync(RefreshDetailsAsync); }

    partial void OnSessionResourcesChanged(ObservableCollection<ActivityResourceRow> value) => OnPropertyChanged(nameof(HasSessionResources));

    partial void OnGlobalResourcesChanged(ObservableCollection<ActivityResourceRow> value) => OnPropertyChanged(nameof(HasGlobalResources));

    partial void OnHistoryGatheringChanged(bool value) => OnPropertyChanged(nameof(HistoryTags));

    partial void OnHistoryPveChanged(bool value) => OnPropertyChanged(nameof(HistoryTags));

    partial void OnHistoryPvpChanged(bool value) => OnPropertyChanged(nameof(HistoryTags));

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

            if (!ShowAllActivities && !ShowOther)

            {

                if ((ShowGathering || ShowFishing) && !ShowPve && !ShowPvp) IsGatheringChart = true;

                else if ((ShowPve || ShowPvp) && !ShowGathering && !ShowFishing) IsGatheringChart = false;

            }

        }

        finally { changingVisibility = false; }

        OnPropertyChanged(nameof(VisibleCategories));
        liveTimelineCancellation.Cancel();
        ++detailChangeVersion; ++detailVersion;

        if (isActive && SelectedTabIndex == 0) _ = RefreshLiveTimelineSafelyAsync();

        else if (isActive && SelectedTabIndex == 1 && SelectedDetailTabIndex is 1 or 5) _ = RunSafelyAsync(RefreshDetailsAsync);

    }



    private async Task RefreshSessionResourcesAsync(ActivityQuery query, long version)

    {

        if (persistence is null || query.SessionId is not { } id) return;

        var rows = await persistence.GetResourceTotalsAsync(query, detailQueryCancellation.Token);

        if (IsCurrentDetail(version, id) && DetailQuery(true) == query) SessionResources = new(rows.Select(row => new ActivityResourceRow(row)));

    }

}

