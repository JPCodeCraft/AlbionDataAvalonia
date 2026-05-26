using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Settings;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class GatheringViewModel : ViewModelBase, IDisposable
{
    private readonly GatheringTrackerService? gatheringTracker;
    private readonly GatheringSessionPersistenceService? sessionPersistence;
    private readonly SettingsManager? settingsManager;
    private readonly ItemImageService? itemImageService;
    private DispatcherTimer? elapsedTimer;
    private int selectedSessionLoadVersion;
    private int? preferredHistorySelectionIndex;

    [ObservableProperty]
    private string totalSessionValueText = "0";

    [ObservableProperty]
    private string silverPerHourText = "0";

    [ObservableProperty]
    private string totalAmountText = "0";

    [ObservableProperty]
    private string elapsedText = "00:00";

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool isGatheringTrackerDisabled;

    [ObservableProperty]
    private bool showMissingPlayerWarning = true;

    [ObservableProperty]
    private bool hasActiveSession;

    [ObservableProperty]
    private GatheringCompletedSessionRowViewModel? selectedCompletedSession;

    public bool IsGatheringTrackerEnabled => !IsGatheringTrackerDisabled;

    public bool HasSelectedCompletedSession => SelectedCompletedSession is not null;

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    public ObservableCollection<GatheringSummaryRowViewModel> SummaryRows { get; } = new();
    public ObservableCollection<GatheringBucketRowViewModel> BucketRows { get; } = new();
    public ObservableCollection<GatheringCompletedSessionRowViewModel> CompletedSessions { get; } = new();
    public ObservableCollection<GatheringHistoryItemRowViewModel> HistoryItemRows { get; } = new();

    public event Action? LiveRowsChanged;

    public GatheringViewModel()
    {
    }

    public GatheringViewModel(
        GatheringTrackerService gatheringTracker,
        GatheringSessionPersistenceService sessionPersistence,
        SettingsManager settingsManager,
        ItemImageService itemImageService)
    {
        this.gatheringTracker = gatheringTracker;
        this.sessionPersistence = sessionPersistence;
        this.settingsManager = settingsManager;
        this.itemImageService = itemImageService;
        isGatheringTrackerDisabled = settingsManager.UserSettings.DisableGatheringTracker;
        ApplySnapshot(gatheringTracker.CurrentSnapshot);
        settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        gatheringTracker.SnapshotChanged += OnSnapshotChanged;
        sessionPersistence.CompletedSessionsChanged += OnCompletedSessionsChanged;

        elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        elapsedTimer.Tick += OnElapsedTimerTick;
        elapsedTimer.Start();

        _ = RefreshHistoryAsync();
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseButtonText));
    }

    partial void OnIsGatheringTrackerDisabledChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGatheringTrackerEnabled));
    }

    partial void OnHasActiveSessionChanged(bool value)
    {
        SaveSessionCommand.NotifyCanExecuteChanged();
        DiscardSessionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCompletedSessionChanged(GatheringCompletedSessionRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCompletedSession));
        DeleteSelectedCompletedSessionCommand.NotifyCanExecuteChanged();
        _ = LoadSelectedCompletedSessionAsync(value, ++selectedSessionLoadVersion);
    }

    [RelayCommand(CanExecute = nameof(CanChangeCurrentSession))]
    private async Task SaveSession()
    {
        if (gatheringTracker is null)
        {
            return;
        }

        await gatheringTracker.CloseAndSaveCurrentSessionAsync();
        await RefreshHistoryAsync();
    }

    [RelayCommand(CanExecute = nameof(CanChangeCurrentSession))]
    private async Task DiscardSession()
    {
        if (gatheringTracker is null)
        {
            return;
        }

        await gatheringTracker.DiscardCurrentSessionAsync();
    }

    [RelayCommand]
    private async Task RefreshHistory()
    {
        await RefreshHistoryAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCompletedSession))]
    private async Task DeleteSelectedCompletedSession()
    {
        if (sessionPersistence is null || SelectedCompletedSession is null)
        {
            return;
        }

        preferredHistorySelectionIndex = CompletedSessions.IndexOf(SelectedCompletedSession);
        if (!await sessionPersistence.DeleteCompletedSessionAsync(SelectedCompletedSession.Id))
        {
            preferredHistorySelectionIndex = null;
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (gatheringTracker is null)
        {
            IsPaused = !IsPaused;
            return;
        }

        gatheringTracker.SetPaused(!IsPaused);
    }

    private bool CanChangeCurrentSession() => HasActiveSession;

    private bool CanDeleteSelectedCompletedSession() => SelectedCompletedSession is not null;

    private void OnSnapshotChanged(GatheringTrackerSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
    }

    private void OnCompletedSessionsChanged()
    {
        _ = RefreshHistoryAsync();
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserSettings.DisableGatheringTracker))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (settingsManager is not null)
            {
                IsGatheringTrackerDisabled = settingsManager.UserSettings.DisableGatheringTracker;
            }
        });
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        if (gatheringTracker is null)
        {
            return;
        }

        var snapshot = gatheringTracker.CurrentSnapshot;
        ApplyHeader(snapshot);
        UpdateSummaryRates(snapshot);
        LiveRowsChanged?.Invoke();
    }

    private void ApplySnapshot(GatheringTrackerSnapshot snapshot)
    {
        ApplyHeader(snapshot);
        SyncSummaryRows(snapshot);
        SyncBucketRows(snapshot);
        LiveRowsChanged?.Invoke();
    }

    private void ApplyHeader(GatheringTrackerSnapshot snapshot)
    {
        IsPaused = snapshot.IsPaused;
        HasActiveSession = snapshot.HasActiveSession;
        ShowMissingPlayerWarning = !snapshot.HasLocalPlayer;
        TotalSessionValueText = FormatLong(snapshot.TotalEstimatedMarketValue);
        SilverPerHourText = FormatLong(CalculateSilverPerHour(snapshot));
        TotalAmountText = FormatLong(snapshot.TotalAmount);
        ElapsedText = FormatElapsed(snapshot.ActiveElapsed);
    }

    private void SyncSummaryRows(GatheringTrackerSnapshot snapshot)
    {
        var desiredRows = snapshot.SummaryRows.ToArray();
        var desiredKeys = desiredRows
            .Select(x => new GatheringItemKey(x.ItemId, x.Quality))
            .ToHashSet();

        for (var i = SummaryRows.Count - 1; i >= 0; i--)
        {
            var row = SummaryRows[i];
            if (!desiredKeys.Contains(new GatheringItemKey(row.ItemId, row.Quality)))
            {
                SummaryRows.RemoveAt(i);
            }
        }

        for (var desiredIndex = 0; desiredIndex < desiredRows.Length; desiredIndex++)
        {
            var row = desiredRows[desiredIndex];
            var rowKey = new GatheringItemKey(row.ItemId, row.Quality);
            var existing = SummaryRows.FirstOrDefault(x => new GatheringItemKey(x.ItemId, x.Quality) == rowKey);
            if (existing is null)
            {
                existing = new GatheringSummaryRowViewModel(row);
                SummaryRows.Insert(desiredIndex, existing);
                _ = LoadItemImageAsync(existing);
                continue;
            }

            existing.Apply(row);
            var currentIndex = SummaryRows.IndexOf(existing);
            if (currentIndex != desiredIndex)
            {
                SummaryRows.Move(currentIndex, desiredIndex);
            }
        }
    }

    private void UpdateSummaryRates(GatheringTrackerSnapshot snapshot)
    {
        var ratesByItem = new Dictionary<GatheringItemKey, long?>();
        foreach (var row in snapshot.SummaryRows)
        {
            ratesByItem[new GatheringItemKey(row.ItemId, row.Quality)] = row.SilverPerHour;
        }

        foreach (var row in SummaryRows)
        {
            if (ratesByItem.TryGetValue(new GatheringItemKey(row.ItemId, row.Quality), out var silverPerHour))
            {
                row.SilverPerHour = silverPerHour;
            }
        }
    }

    private void SyncBucketRows(GatheringTrackerSnapshot snapshot)
    {
        var desiredRows = snapshot.BucketRows.ToArray();
        var desiredKeys = desiredRows
            .Select(x => x.BucketStartedAtUtc)
            .ToHashSet();

        for (var i = BucketRows.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(BucketRows[i].BucketStartedAtUtc))
            {
                BucketRows.RemoveAt(i);
            }
        }

        for (var desiredIndex = 0; desiredIndex < desiredRows.Length; desiredIndex++)
        {
            var row = desiredRows[desiredIndex];
            var existing = BucketRows.FirstOrDefault(x => x.BucketStartedAtUtc == row.BucketStartedAtUtc);
            if (existing is null)
            {
                BucketRows.Insert(desiredIndex, new GatheringBucketRowViewModel(row));
                continue;
            }

            existing.Apply(row);
            var currentIndex = BucketRows.IndexOf(existing);
            if (currentIndex != desiredIndex)
            {
                BucketRows.Move(currentIndex, desiredIndex);
            }
        }
    }

    private async Task RefreshHistoryAsync()
    {
        if (sessionPersistence is null)
        {
            return;
        }

        var sessions = await sessionPersistence.GetCompletedSessionsAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var selectedId = SelectedCompletedSession?.Id;
            var fallbackIndex = preferredHistorySelectionIndex;
            preferredHistorySelectionIndex = null;
            CompletedSessions.Clear();
            GatheringCompletedSessionRowViewModel? selected = null;
            foreach (var session in sessions)
            {
                var row = new GatheringCompletedSessionRowViewModel(session);
                CompletedSessions.Add(row);
                if (row.Id == selectedId)
                {
                    selected = row;
                }
            }

            if (selected is not null)
            {
                SelectedCompletedSession = selected;
                return;
            }

            if (CompletedSessions.Count == 0)
            {
                SelectedCompletedSession = null;
                return;
            }

            var nextIndex = fallbackIndex is null
                ? 0
                : Math.Clamp(fallbackIndex.Value, 0, CompletedSessions.Count - 1);
            SelectedCompletedSession = CompletedSessions[nextIndex];
        });
    }

    private async Task LoadSelectedCompletedSessionAsync(
        GatheringCompletedSessionRowViewModel? session,
        int loadVersion)
    {
        if (sessionPersistence is null || session is null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HistoryItemRows.Clear();
            });
            return;
        }

        var details = await sessionPersistence.GetCompletedSessionDetailsAsync(session.Id);
        if (loadVersion != selectedSessionLoadVersion)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            HistoryItemRows.Clear();

            if (details is null)
            {
                return;
            }

            foreach (var item in details.Items)
            {
                var rowViewModel = new GatheringHistoryItemRowViewModel(item);
                HistoryItemRows.Add(rowViewModel);
                _ = LoadItemImageAsync(rowViewModel);
            }
        });
    }

    private async Task LoadItemImageAsync(GatheringSummaryRowViewModel row)
    {
        if (itemImageService is null)
        {
            return;
        }

        var image = await itemImageService.GetItemImageAsync(row.ItemUniqueName, row.Quality);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (SummaryRows.Contains(row))
            {
                row.ItemImage = image;
            }
        });
    }

    private async Task LoadItemImageAsync(GatheringHistoryItemRowViewModel row)
    {
        if (itemImageService is null)
        {
            return;
        }

        var image = await itemImageService.GetItemImageAsync(row.ItemUniqueName, row.Quality);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (HistoryItemRows.Contains(row))
            {
                row.ItemImage = image;
            }
        });
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }

        return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private static string FormatLong(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static long CalculateSilverPerHour(GatheringTrackerSnapshot snapshot)
    {
        if (snapshot.TotalEstimatedMarketValue <= 0 || snapshot.ActiveElapsed.TotalSeconds <= 0)
        {
            return 0;
        }

        return (long)Math.Round(snapshot.TotalEstimatedMarketValue / snapshot.ActiveElapsed.TotalHours);
    }

    public void Dispose()
    {
        if (gatheringTracker is not null)
        {
            gatheringTracker.SnapshotChanged -= OnSnapshotChanged;
        }

        if (sessionPersistence is not null)
        {
            sessionPersistence.CompletedSessionsChanged -= OnCompletedSessionsChanged;
        }

        if (settingsManager is not null)
        {
            settingsManager.UserSettings.PropertyChanged -= OnUserSettingsPropertyChanged;
        }

        if (elapsedTimer is not null)
        {
            elapsedTimer.Stop();
            elapsedTimer.Tick -= OnElapsedTimerTick;
            elapsedTimer = null;
        }
    }
}

public sealed class GatheringSummaryRowViewModel : ObservableObject
{
    private Bitmap? itemImage;
    private long amount;
    private long? estimatedMarketValue;
    private long? totalEstimatedMarketValue;
    private double amountPerHour;
    private long? silverPerHour;

    public GatheringSummaryRowViewModel(GatheringSummaryRow row)
    {
        ItemId = row.ItemId;
        Quality = row.Quality;
        ItemUniqueName = row.ItemUniqueName;
        ItemName = row.ItemName;
        Apply(row);
    }

    public int ItemId { get; }
    public int Quality { get; }
    public string ItemUniqueName { get; }
    public string ItemName { get; }
    public long Amount
    {
        get => amount;
        private set => SetProperty(ref amount, value);
    }

    public long? EstimatedMarketValue
    {
        get => estimatedMarketValue;
        private set
        {
            if (SetProperty(ref estimatedMarketValue, value))
            {
                OnPropertyChanged(nameof(EstimatedMarketValueText));
            }
        }
    }

    public long? TotalEstimatedMarketValue
    {
        get => totalEstimatedMarketValue;
        private set
        {
            if (SetProperty(ref totalEstimatedMarketValue, value))
            {
                OnPropertyChanged(nameof(TotalEstimatedMarketValueText));
            }
        }
    }

    public double AmountPerHour
    {
        get => amountPerHour;
        private set => SetProperty(ref amountPerHour, value);
    }

    public Bitmap? ItemImage
    {
        get => itemImage;
        set => SetProperty(ref itemImage, value);
    }

    public long? SilverPerHour
    {
        get => silverPerHour;
        set
        {
            if (SetProperty(ref silverPerHour, value))
            {
                OnPropertyChanged(nameof(SilverPerHourText));
            }
        }
    }

    public string EstimatedMarketValueText => EstimatedMarketValue is null ? "-" : EstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue is null ? "-" : TotalEstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string SilverPerHourText => SilverPerHour is null ? "-" : SilverPerHour.Value.ToString("N0", CultureInfo.CurrentCulture);

    public void Apply(GatheringSummaryRow row)
    {
        Amount = row.Amount;
        EstimatedMarketValue = row.EstimatedMarketValue;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        AmountPerHour = row.AmountPerHour;
        SilverPerHour = row.SilverPerHour;
    }
}

public sealed class GatheringBucketRowViewModel : ObservableObject
{
    private long amount;
    private long? totalEstimatedMarketValue;
    private long? silverPerHour;

    public GatheringBucketRowViewModel(GatheringBucketRow row)
    {
        BucketStartedAtUtc = row.BucketStartedAtUtc;
        Apply(row);
    }

    public DateTime BucketStartedAtUtc { get; }
    public string BucketText => BucketStartedAtUtc.ToString("HH:mm", CultureInfo.CurrentCulture);
    public long Amount
    {
        get => amount;
        private set => SetProperty(ref amount, value);
    }

    public long? TotalEstimatedMarketValue
    {
        get => totalEstimatedMarketValue;
        private set
        {
            if (SetProperty(ref totalEstimatedMarketValue, value))
            {
                OnPropertyChanged(nameof(TotalEstimatedMarketValueText));
            }
        }
    }

    public long? SilverPerHour
    {
        get => silverPerHour;
        private set
        {
            if (SetProperty(ref silverPerHour, value))
            {
                OnPropertyChanged(nameof(SilverPerHourText));
            }
        }
    }

    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue is null ? "-" : TotalEstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string SilverPerHourText => SilverPerHour is null ? "-" : SilverPerHour.Value.ToString("N0", CultureInfo.CurrentCulture);

    public void Apply(GatheringBucketRow row)
    {
        Amount = row.Amount;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        SilverPerHour = row.SilverPerHour;
    }
}

public sealed class GatheringCompletedSessionRowViewModel
{
    public GatheringCompletedSessionRowViewModel(GatheringCompletedSessionSummary row)
    {
        Id = row.Id;
        StartedAtUtc = row.StartedAtUtc;
        EndedAtUtc = row.EndedAtUtc;
        LastActivityAtUtc = row.LastActivityAtUtc;
        ActiveElapsed = row.ActiveElapsed;
        TotalAmount = row.TotalAmount;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        SilverPerHour = row.SilverPerHour;
        Source = row.Source;
    }

    public Guid Id { get; }
    public DateTime StartedAtUtc { get; }
    public DateTime EndedAtUtc { get; }
    public DateTime LastActivityAtUtc { get; }
    public TimeSpan ActiveElapsed { get; }
    public long TotalAmount { get; }
    public long TotalEstimatedMarketValue { get; }
    public long SilverPerHour { get; }
    public GatheringSessionSource Source { get; }
    public string StartedText => StartedAtUtc.ToString("g", CultureInfo.CurrentCulture);
    public string EndedText => EndedAtUtc.ToString("g", CultureInfo.CurrentCulture);
    public string ActiveElapsedText => ActiveElapsed.TotalHours >= 1
        ? $"{(int)ActiveElapsed.TotalHours:00}:{ActiveElapsed.Minutes:00}:{ActiveElapsed.Seconds:00}"
        : $"{ActiveElapsed.Minutes:00}:{ActiveElapsed.Seconds:00}";
    public string TotalAmountText => TotalAmount.ToString("N0", CultureInfo.CurrentCulture);
    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue.ToString("N0", CultureInfo.CurrentCulture);
    public string SilverPerHourText => SilverPerHour.ToString("N0", CultureInfo.CurrentCulture);
    public string SourceText => Source.ToString();
}

public sealed class GatheringHistoryItemRowViewModel : ObservableObject
{
    private Bitmap? itemImage;

    public GatheringHistoryItemRowViewModel(GatheringCompletedSessionItemSnapshot row)
    {
        ItemUniqueName = row.ItemUniqueName;
        ItemName = row.ItemName;
        Quality = row.Quality;
        Amount = row.Amount;
        EstimatedMarketValue = row.EstimatedMarketValue;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        Source = row.Source;
    }

    public string ItemUniqueName { get; }
    public string ItemName { get; }
    public int Quality { get; }
    public long Amount { get; }
    public long? EstimatedMarketValue { get; }
    public long? TotalEstimatedMarketValue { get; }
    public GatheringSessionSource Source { get; }
    public Bitmap? ItemImage
    {
        get => itemImage;
        set => SetProperty(ref itemImage, value);
    }

    public string EstimatedMarketValueText => EstimatedMarketValue is null ? "-" : EstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string TotalEstimatedMarketValueText => TotalEstimatedMarketValue is null ? "-" : TotalEstimatedMarketValue.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string SourceText => Source.ToString();
}
