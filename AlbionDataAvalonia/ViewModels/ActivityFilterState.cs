using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>
/// The filters the Activity tabs share: whose activity counts, and which kinds the charts and timeline show.
/// The page passes <see cref="ScopeChanged"/> and <see cref="CategoriesChanged"/> on to each tab.
/// </summary>
public partial class ActivityFilterState : ObservableObject
{
    private bool changingVisibility;

    public IReadOnlyList<Option<ActivityScope>> ScopeOptions { get; } =
        [new(ActivityScope.Personal, "Personal"), new(ActivityScope.Party, "Party")];
    public IReadOnlyList<Option<ActivityScope>> LootScopeOptions { get; } =
        [new(ActivityScope.Personal, "Personal"), new(ActivityScope.Party, "Party"), new(ActivityScope.AllObserved, "Everyone")];
    public IReadOnlyList<Option<ActivityItemSources>> ItemSourceOptions { get; } =
        [new(ActivityItemSources.All, "All items"), new(ActivityItemSources.Loot, "Loot"),
         new(ActivityItemSources.Gathering, "Gathering"), new(ActivityItemSources.Fishing, "Fishing")];

    [ObservableProperty] private Option<ActivityScope> selectedScope = new(ActivityScope.Personal, "Personal");
    [ObservableProperty] private bool showAllActivities = true;
    // Gathering covers fishing too; the chart and timeline never split them.
    [ObservableProperty] private bool showGathering;
    [ObservableProperty] private bool showPve;
    [ObservableProperty] private bool showPvp;
    [ObservableProperty] private bool showOther;
    [ObservableProperty] private ActivityTimelineChartMode chartMode;

    public event Action? ScopeChanged;
    public event Action? CategoriesChanged;

    public ActivityScope Scope => SelectedScope.Value;

    public ActivityCategories VisibleCategories => ShowAllActivities ? ActivityCategories.All :
        (ShowGathering ? ActivityCategories.Gathering | ActivityCategories.Fishing : ActivityCategories.None)
        | (ShowPve ? ActivityCategories.Pve : ActivityCategories.None)
        | (ShowPvp ? ActivityCategories.Pvp : ActivityCategories.None)
        | (ShowOther ? ActivityCategories.Other : ActivityCategories.None);

    partial void OnSelectedScopeChanged(Option<ActivityScope> value) => ScopeChanged?.Invoke();
    partial void OnShowAllActivitiesChanged(bool value) => ChangeVisibility(value, true);
    partial void OnShowGatheringChanged(bool value) => ChangeVisibility(value);
    partial void OnShowPveChanged(bool value) => ChangeVisibility(value);
    partial void OnShowPvpChanged(bool value) => ChangeVisibility(value);
    partial void OnShowOtherChanged(bool value) => ChangeVisibility(value);

    // All turns the other kinds off and turns back on when none is left. Showing only gathering
    // or only combat switches the chart to match.
    private void ChangeVisibility(bool selected, bool all = false)
    {
        if (changingVisibility) return;
        changingVisibility = true;
        try
        {
            if (all && selected) ShowGathering = ShowPve = ShowPvp = ShowOther = false;
            else if (!all && selected) ShowAllActivities = false;
            if (!ShowGathering && !ShowPve && !ShowPvp && !ShowOther) ShowAllActivities = true;
            if (!ShowAllActivities && !ShowOther && ChartMode != ActivityTimelineChartMode.Fame)
            {
                if (ShowGathering && !ShowPve && !ShowPvp) ChartMode = ActivityTimelineChartMode.Gathering;
                else if ((ShowPve || ShowPvp) && !ShowGathering) ChartMode = ActivityTimelineChartMode.Combat;
            }
        }
        finally { changingVisibility = false; }
        OnPropertyChanged(nameof(VisibleCategories));
        CategoriesChanged?.Invoke();
    }
}
