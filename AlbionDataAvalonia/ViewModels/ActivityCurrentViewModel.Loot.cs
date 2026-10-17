using AlbionDataAvalonia.Activity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AlbionDataAvalonia.ViewModels.ActivityReads;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityCurrentViewModel
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
        if (Persistence is not { } persistence || Tracker is null || page.IsDisposed) return;
        var key = GetCurrentPeriodKey();
        var contextChanged = key != currentLootKey;
        if (contextChanged)
        {
            ClearCurrentLoot();
            currentLootKey = key;
        }
        if (key is null || !IsCurrentLootExpanded || !page.IsShowing(ActivityTab.Current)) return;
        if (append && !HasMoreCurrentLoot) return;

        var nowUtc = DateTime.UtcNow;
        if (!append && !contextChanged && (nowUtc - currentLootReadAtUtc < TimeSpan.FromSeconds(5) ||
            currentLootReadVersion == page.ItemChangeVersion && !IsLastHoursPeriod)) return;
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
        var dataVersion = page.ItemChangeVersion;
        var cancellationToken = BeginRead(ref currentLootCancellation);
        var pageIndex = append ? currentLootPage + 1 : currentLootPage;
        var result = await ReadEventRowsAsync(persistence, query, pageIndex, append, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!page.IsShowing(ActivityTab.Current) || !IsCurrentLootExpanded ||
            version != currentLootVersion || key != GetCurrentPeriodKey()) return;

        ApplyEventRows(CurrentLoot, result.Items.Select(value => new ActivityLootRow(value)).ToArray(), row => row.SourceEvent, append);
        page.Valuations?.RequestMissing(result.Items);
        currentLootKey = key;
        currentLootQuery = query;
        currentLootPage = pageIndex;
        currentLootReadVersion = dataVersion;
        currentLootReadAtUtc = DateTime.UtcNow;
        HasMoreCurrentLoot = result.HasMore;
        OnPropertyChanged(nameof(HasCurrentLoot));
    }

    [RelayCommand]
    private Task LoadMoreCurrentLootAsync() => page.RunSafelyAsync(() => RefreshCurrentLootAsync(true));

    [RelayCommand]
    private Task ExportCurrentLoot(LootExportFormat format) =>
        Export.ExportToFileAsync(Export.CanStart && HasCurrentLoot ? currentLootQuery : null, format);

    partial void OnIsCurrentLootExpandedChanged(bool value)
    {
        if (!value) CancelCurrentLootRead();
        else if (page.IsShowing(ActivityTab.Current)) _ = page.RunSafelyAsync(() => RefreshCurrentLootAsync());
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
