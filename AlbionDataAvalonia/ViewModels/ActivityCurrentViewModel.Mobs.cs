using AlbionDataAvalonia.Activity.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static AlbionDataAvalonia.ViewModels.ActivityReads;

namespace AlbionDataAvalonia.ViewModels;

public partial class ActivityCurrentViewModel
{
    private CancellationTokenSource currentMobsCancellation = new();
    private CurrentPeriodKey? currentMobsKey;
    private IReadOnlyList<ActivityMobTotal> currentMobTotals = [];
    private long currentMobsVersion;
    private long currentMobsReadVersion = -1;
    private DateTime currentMobsReadAtUtc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentMobs))]
    private ObservableCollection<ActivityMobRow> currentMobs = new();
    [ObservableProperty] private bool isCurrentMobsExpanded;

    public bool HasCurrentMobs => CurrentMobs.Count > 0;

    // The mobs killed in the period Current shows, read only while their panel is open.
    private async Task RefreshCurrentMobsAsync()
    {
        if (Persistence is not { } persistence || Tracker is not { } tracker || page.IsDisposed) return;
        var key = GetCurrentPeriodKey();
        var contextChanged = key != currentMobsKey;
        if (contextChanged)
        {
            ClearCurrentMobs();
            currentMobsKey = key;
        }
        if (key is null || !IsCurrentMobsExpanded || !page.IsShowing(ActivityTab.Current)) return;

        var nowUtc = DateTime.UtcNow;
        var dataVersion = tracker.CurrentReadVersion;
        if (!contextChanged && (nowUtc - currentMobsReadAtUtc < TimeSpan.FromSeconds(5) ||
            currentMobsReadVersion == dataVersion && !IsLastHoursPeriod)) return;
        if (CurrentPeriodQuery(nowUtc) is not { } query) return;
        // A run reads all its kills; other periods keep their time bounds, as the loot list does.
        query = query with
        {
            FromUtc = key.SessionId is not null ? null : query.FromUtc,
            CharacterName = string.IsNullOrWhiteSpace(query.CharacterId) ? query.CharacterName : null
        };
        var version = ++currentMobsVersion;
        var cancellationToken = BeginRead(ref currentMobsCancellation);
        var totals = await persistence.GetMobsAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!page.IsShowing(ActivityTab.Current) || !IsCurrentMobsExpanded ||
            version != currentMobsVersion || key != GetCurrentPeriodKey()) return;

        // Unchanged totals keep their tiles.
        if (!totals.SequenceEqual(currentMobTotals)) CurrentMobs = new(totals.Select(total => new ActivityMobRow(total)));
        currentMobTotals = totals;
        currentMobsReadVersion = dataVersion;
        currentMobsReadAtUtc = DateTime.UtcNow;
    }

    partial void OnIsCurrentMobsExpandedChanged(bool value)
    {
        if (!value) CancelCurrentMobsRead();
        else if (page.IsShowing(ActivityTab.Current)) _ = page.RunSafelyAsync(RefreshCurrentMobsAsync);
    }

    private void ClearCurrentMobs()
    {
        CancelCurrentMobsRead();
        currentMobsKey = null;
        currentMobTotals = [];
        currentMobsReadVersion = -1;
        currentMobsReadAtUtc = DateTime.MinValue;
        if (CurrentMobs.Count > 0) CurrentMobs = new();
    }

    private void CancelCurrentMobsRead()
    {
        ++currentMobsVersion;
        currentMobsCancellation.Cancel();
    }
}
