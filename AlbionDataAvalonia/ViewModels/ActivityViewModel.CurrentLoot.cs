using AlbionDataAvalonia.Activity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityViewModel
{
    private CancellationTokenSource currentLootCancellation = new();
    private CurrentPeriodKey? currentLootKey;
    private ActivityQuery? currentLootQuery;
    private long currentLootVersion;
    private long currentLootReadVersion = -1;
    private DateTime currentLootReadAtUtc;
    private int currentLootPage;

    [ObservableProperty] private ObservableCollection<ActivityLootRow> currentLoot = new();
    [ObservableProperty] private bool isCurrentLootExpanded;
    [ObservableProperty] private bool hasMoreCurrentLoot;

    public bool HasCurrentLoot => CurrentLoot.Count > 0;

    private async Task RefreshCurrentLootAsync(bool append = false)
    {
        if (persistence is null || tracker is null || isDisposed) return;
        var key = GetCurrentPeriodKey();
        var contextChanged = key != currentLootKey;
        if (contextChanged)
        {
            ClearCurrentLoot();
            currentLootKey = key;
        }
        if (key is null || !IsCurrentLootExpanded || !isActive || SelectedTabIndex != 0) return;
        if (append && !HasMoreCurrentLoot) return;

        var nowUtc = DateTime.UtcNow;
        if (!append && !contextChanged && (nowUtc - currentLootReadAtUtc < TimeSpan.FromSeconds(5) ||
            currentLootReadVersion == itemChangeVersion && !IsLastHoursPeriod)) return;
        var query = append ? currentLootQuery : CurrentPeriodQuery(nowUtc);
        if (query is null) return;
        // A session ID selects all its pickups, including when a new session's live
        // metadata has not reached Current yet. Other periods retain their time bounds.
        query = query with
        {
            LootOnly = true,
            FromUtc = key.SessionId is not null ? null : query.FromUtc,
            CharacterName = string.IsNullOrWhiteSpace(query.CharacterId) ? query.CharacterName : null
        };
        var version = ++currentLootVersion;
        var dataVersion = itemChangeVersion;
        var cancellationToken = BeginRead(ref currentLootCancellation);
        var page = append ? currentLootPage + 1 : currentLootPage;
        var result = await ReadEventRowsAsync(query, page, append, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (isDisposed || !isActive || SelectedTabIndex != 0 || !IsCurrentLootExpanded ||
            version != currentLootVersion || key != GetCurrentPeriodKey()) return;

        ApplyEventRows(CurrentLoot, result.Items.Select(value => new ActivityLootRow(value)).ToArray(), row => row.SourceEvent, append);
        valuations?.RequestMissing(result.Items);
        currentLootKey = key;
        currentLootQuery = query;
        currentLootPage = page;
        currentLootReadVersion = dataVersion;
        currentLootReadAtUtc = DateTime.UtcNow;
        HasMoreCurrentLoot = result.HasMore;
        OnPropertyChanged(nameof(HasCurrentLoot));
    }

    [RelayCommand]
    private Task LoadMoreCurrentLootAsync() => RunSafelyAsync(() => RefreshCurrentLootAsync(true));

    partial void OnIsCurrentLootExpandedChanged(bool value)
    {
        if (!value) CancelCurrentLootRead();
        else if (isActive && SelectedTabIndex == 0) _ = RunSafelyAsync(() => RefreshCurrentLootAsync());
    }

    partial void OnCurrentLootChanged(ObservableCollection<ActivityLootRow> value) => OnPropertyChanged(nameof(HasCurrentLoot));

    private void ClearCurrentLoot()
    {
        CancelCurrentLootRead();
        currentLootKey = null;
        currentLootQuery = null;
        currentLootReadVersion = -1;
        currentLootReadAtUtc = DateTime.MinValue;
        currentLootPage = 0;
        CurrentLoot.Clear();
        HasMoreCurrentLoot = false;
        OnPropertyChanged(nameof(HasCurrentLoot));
    }

    private void CancelCurrentLootRead()
    {
        ++currentLootVersion;
        currentLootCancellation.Cancel();
    }
}
