using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Gathering.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class GatheringViewModel : ViewModelBase, IDisposable
{
    private readonly GatheringTrackerService? gatheringTracker;
    private readonly GatheringSessionPersistenceService? sessionPersistence;
    private readonly SettingsManager? settingsManager;
    private readonly ItemImageService? itemImageService;
    private readonly CsvExportService? csvExportService;
    private readonly PortfolioUploadService? portfolioUploadService;
    private const int ShareCardItemSlots = 20;
    private const string PreparingPortfolioImportStatus = "Preparing gathering session for Portfolio...";
    private DispatcherTimer? elapsedTimer;
    private int selectedSessionLoadVersion;
    private int? preferredHistorySelectionIndex;
    private Bitmap? shareLogo;
    private GatheringCompletedSessionDetails? selectedCompletedSessionDetails;

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

    [ObservableProperty]
    private bool isSelectedCompletedSessionLoaded;

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private int exportProgress;

    [ObservableProperty]
    private bool isAddingToPortfolio;

    [ObservableProperty]
    private int portfolioImportProgress;

    [ObservableProperty]
    private string portfolioImportStatus = string.Empty;

    [ObservableProperty]
    private bool hasPortfolioImportStatus;

    public bool IsGatheringTrackerEnabled => !IsGatheringTrackerDisabled;

    public bool HasSelectedCompletedSession => SelectedCompletedSession is not null;

    public bool CanShareSelectedCompletedSession => SelectedCompletedSession is not null && IsSelectedCompletedSessionLoaded;

    public bool CanExportSelectedCompletedSession =>
        IsSelectedCompletedSessionLoaded
        && SelectedCompletedSession is { } selectedSession
        && selectedCompletedSessionDetails is { } details
        && details.Summary.Id == selectedSession.Id
        && !IsExporting
        && !IsAddingToPortfolio;

    public bool CanAddSelectedCompletedSessionToPortfolio =>
        IsSelectedCompletedSessionLoaded
        && SelectedCompletedSession is { } selectedSession
        && selectedCompletedSessionDetails is { } details
        && details.Summary.Id == selectedSession.Id
        && details.Summary.AlbionServerId is 1 or 2 or 3
        && details.Items.Count > 0
        && details.Items.All(item => item.Amount is > 0 and <= int.MaxValue)
        && !IsExporting
        && !IsAddingToPortfolio;

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
        ItemImageService itemImageService,
        CsvExportService csvExportService,
        PortfolioUploadService portfolioUploadService)
    {
        this.gatheringTracker = gatheringTracker;
        this.sessionPersistence = sessionPersistence;
        this.settingsManager = settingsManager;
        this.itemImageService = itemImageService;
        this.csvExportService = csvExportService;
        this.portfolioUploadService = portfolioUploadService;
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
        if (!IsAddingToPortfolio)
        {
            SetPortfolioImportStatus(string.Empty);
        }

        selectedCompletedSessionDetails = null;
        IsSelectedCompletedSessionLoaded = false;
        OnPropertyChanged(nameof(HasSelectedCompletedSession));
        OnPropertyChanged(nameof(CanShareSelectedCompletedSession));
        NotifyHistoryActionAvailabilityChanged();
        DeleteSelectedCompletedSessionCommand.NotifyCanExecuteChanged();
        _ = LoadSelectedCompletedSessionAsync(value, ++selectedSessionLoadVersion);
    }

    partial void OnIsSelectedCompletedSessionLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShareSelectedCompletedSession));
        NotifyHistoryActionAvailabilityChanged();
    }

    partial void OnIsExportingChanged(bool value) => NotifyHistoryActionAvailabilityChanged();

    partial void OnIsAddingToPortfolioChanged(bool value) => NotifyHistoryActionAvailabilityChanged();

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

    public GatheringSessionShareCardViewModel? CreateSelectedSessionShareCard()
    {
        if (SelectedCompletedSession is null || !IsSelectedCompletedSessionLoaded)
        {
            return null;
        }

        var orderedItems = HistoryItemRows
            .OrderByDescending(x => x.TotalEstimatedMarketValue ?? 0)
            .ThenByDescending(x => x.Amount)
            .ToArray();

        var visibleItemCount = orderedItems.Length > ShareCardItemSlots
            ? ShareCardItemSlots - 1
            : orderedItems.Length;
        var topItems = orderedItems
            .Take(visibleItemCount)
            .Select(x => new GatheringSessionShareItemViewModel(x))
            .ToList();

        var hiddenItemCount = orderedItems.Length - visibleItemCount;
        if (hiddenItemCount > 0)
        {
            topItems.Add(GatheringSessionShareItemViewModel.CreateOverflow(hiddenItemCount));
        }

        shareLogo ??= LoadShareLogo();
        return new GatheringSessionShareCardViewModel(SelectedCompletedSession, topItems, shareLogo);
    }

    public async Task ExportSelectedCompletedSessionToCsvAsync(
        Stream stream,
        CsvExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var details = selectedCompletedSessionDetails;
        if (!IsExporting
            || csvExportService is null
            || details is null
            || SelectedCompletedSession?.Id != details.Summary.Id
            || !IsSelectedCompletedSessionLoaded)
        {
            return;
        }

        var progress = new Progress<int>(value => ExportProgress = value);
        await csvExportService.ExportGatheringSessionToCsvAsync(
            stream,
            details,
            options,
            progress,
            cancellationToken);
    }

    public bool TryBeginGatheringCsvExport()
    {
        if (!CanExportSelectedCompletedSession)
        {
            return false;
        }

        ExportProgress = 0;
        IsExporting = true;
        return true;
    }

    public void EndGatheringCsvExport()
    {
        IsExporting = false;
    }

    public bool TryBeginGatheringPortfolioImport()
    {
        if (!CanAddSelectedCompletedSessionToPortfolio)
        {
            return false;
        }

        PortfolioImportProgress = 0;
        SetPortfolioImportStatus(PreparingPortfolioImportStatus);
        IsAddingToPortfolio = true;
        return true;
    }

    public void EndGatheringPortfolioImport()
    {
        IsAddingToPortfolio = false;
        if (PortfolioImportStatus == PreparingPortfolioImportStatus)
        {
            SetPortfolioImportStatus(string.Empty);
        }
    }

    public async Task<bool> EnsurePortfolioSignedInAsync(CancellationToken cancellationToken = default)
    {
        if (portfolioUploadService is not null
            && await portfolioUploadService.CanUploadAsync(cancellationToken))
        {
            return true;
        }

        SetPortfolioImportStatus("Sign in to AFM before adding gathering data to Portfolio.");
        return false;
    }

    public async Task<HashSet<Guid>?> GetPortfolioUploadedDataIdsAsync(
        CancellationToken cancellationToken = default)
    {
        if (portfolioUploadService is null)
        {
            return null;
        }

        var result = await portfolioUploadService.GetUploadedTradeIdsAsync(cancellationToken);
        if (result.Success)
        {
            return result.TradeIds;
        }

        SetPortfolioImportStatus($"Portfolio: {result.ErrorMessage ?? "failed to load positions."}");
        return null;
    }

    public async Task AddSelectedCompletedSessionToPortfolioAsync(
        int locationIndex,
        IReadOnlyDictionary<Guid, double> unitPrices,
        bool allowReupload,
        CancellationToken cancellationToken = default)
    {
        var details = selectedCompletedSessionDetails;
        if (portfolioUploadService is null
            || details is null
            || SelectedCompletedSession?.Id != details.Summary.Id
            || !IsSelectedCompletedSessionLoaded
            || !IsAddingToPortfolio)
        {
            return;
        }

        var summary = details.Summary;
        if (summary.AlbionServerId is not (1 or 2 or 3))
        {
            SetPortfolioImportStatus("Portfolio: this gathering session does not have a supported Albion server.");
            return;
        }

        if (locationIndex < 0)
        {
            SetPortfolioImportStatus("Portfolio: select a valid market location.");
            return;
        }

        var requests = new List<PortfolioTradeImportRequest>(details.Items.Count);
        foreach (var item in details.Items)
        {
            if (item.Amount is <= 0 or > int.MaxValue)
            {
                SetPortfolioImportStatus($"Portfolio: {item.ItemName} has an amount outside the supported range.");
                return;
            }

            if (!unitPrices.TryGetValue(item.Id, out var unitPrice)
                || !double.IsFinite(unitPrice)
                || unitPrice < 0)
            {
                SetPortfolioImportStatus($"Portfolio: enter a valid unit price for {item.ItemName}.");
                return;
            }

            var qualityIndex = item.Quality is >= 1 and <= 5 ? item.Quality : 1;
            requests.Add(new PortfolioTradeImportRequest(
                item.Id,
                item.ItemUniqueName,
                summary.AlbionServerId,
                TradeType.Instant,
                TradeOperation.Buy,
                (int)item.Amount,
                unitPrice,
                summary.LastActivityAtUtc,
                locationIndex,
                qualityIndex,
                "Direct Trade"));
        }

        if (requests.Count == 0)
        {
            SetPortfolioImportStatus("Portfolio: the selected gathering session has no items to import.");
            return;
        }

        var batches = requests
            .GroupBy(request => (request.ItemId, request.AlbionServerId, request.QualityIndex))
            .Select(group => group.ToArray())
            .Chunk(PortfolioUploadService.MaxPortfolioImportPostCount)
            .Select(groups => groups.SelectMany(group => group).ToArray())
            .ToArray();

        PortfolioImportProgress = 0;
        SetPortfolioImportStatus("Adding gathering session to Portfolio...");

        try
        {
            var aggregate = new PortfolioImportResult { RequestedCount = requests.Count };
            var processed = 0;

            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await portfolioUploadService.ImportTradesAsync(batch, allowReupload, cancellationToken);
                MergePortfolioImportResult(aggregate, result);
                processed += batch.Length;
                PortfolioImportProgress = (int)Math.Round(processed * 100d / requests.Count);
            }

            SetPortfolioImportStatus(CreatePortfolioImportStatus(aggregate));
        }
        catch (OperationCanceledException)
        {
            SetPortfolioImportStatus("Portfolio import canceled.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to add gathering session to Portfolio");
            SetPortfolioImportStatus("Portfolio upload failed. Check logs for details.");
        }
    }

    private bool CanChangeCurrentSession() => HasActiveSession;

    private bool CanDeleteSelectedCompletedSession() => SelectedCompletedSession is not null;

    private void NotifyHistoryActionAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanExportSelectedCompletedSession));
        OnPropertyChanged(nameof(CanAddSelectedCompletedSessionToPortfolio));
    }

    private void SetPortfolioImportStatus(string status)
    {
        PortfolioImportStatus = status;
        HasPortfolioImportStatus = !string.IsNullOrWhiteSpace(status);
    }

    private static void MergePortfolioImportResult(
        PortfolioImportResult aggregate,
        PortfolioImportResult result)
    {
        aggregate.ImportedTradeIds.AddRange(result.ImportedTradeIds);
        aggregate.ReuploadedTradeIds.AddRange(result.ReuploadedTradeIds);
        aggregate.SkippedTradeIds.AddRange(result.SkippedTradeIds);
        aggregate.FailedTradeIds.AddRange(result.FailedTradeIds);

        foreach (var warning in result.Warnings.Where(warning => !aggregate.Warnings.Contains(warning)))
        {
            aggregate.Warnings.Add(warning);
        }

        foreach (var error in result.Errors.Where(error => !aggregate.Errors.Contains(error)))
        {
            aggregate.Errors.Add(error);
        }
    }

    private static string CreatePortfolioImportStatus(PortfolioImportResult result)
    {
        var status = $"Portfolio: {result.ImportedCount:N0} imported";
        if (result.ReuploadedCount > 0)
        {
            status += $", {result.ReuploadedCount:N0} reuploaded";
        }
        if (result.SkippedCount > 0)
        {
            status += $", {result.SkippedCount:N0} skipped";
        }
        if (result.FailedCount > 0)
        {
            status += $", {result.FailedCount:N0} failed";
        }
        if (result.Errors.Count > 0)
        {
            status += $". {result.Errors[0]}";
        }
        else if (result.Warnings.Count > 0)
        {
            status += $". {result.Warnings[0]}";
        }

        return status;
    }

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
                if (!IsCurrentCompletedSessionSelection(session, loadVersion))
                {
                    return;
                }

                selectedCompletedSessionDetails = null;
                HistoryItemRows.Clear();
                IsSelectedCompletedSessionLoaded = session is null;
                NotifyHistoryActionAvailabilityChanged();
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
            if (!IsCurrentCompletedSessionSelection(session, loadVersion))
            {
                return;
            }

            if (details?.Summary.Id != session.Id)
            {
                details = null;
            }

            selectedCompletedSessionDetails = details;
            HistoryItemRows.Clear();

            if (details is null)
            {
                IsSelectedCompletedSessionLoaded = true;
                return;
            }

            foreach (var item in details.Items)
            {
                var rowViewModel = new GatheringHistoryItemRowViewModel(item);
                HistoryItemRows.Add(rowViewModel);
                _ = LoadItemImageAsync(rowViewModel);
            }

            IsSelectedCompletedSessionLoaded = true;
            NotifyHistoryActionAvailabilityChanged();
        });
    }

    private bool IsCurrentCompletedSessionSelection(
        GatheringCompletedSessionRowViewModel? session,
        int loadVersion)
    {
        return loadVersion == selectedSessionLoadVersion
            && SelectedCompletedSession?.Id == session?.Id;
    }

    private static Bitmap? LoadShareLogo()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://AlbionDataAvalonia/Assets/afm-logo.png"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
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
        AlbionServerId = row.AlbionServerId;
        PlayerName = row.PlayerName;
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
    public int? AlbionServerId { get; }
    public string PlayerName { get; }
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

    public GatheringHistoryItemRowViewModel(GatheringCompletedSessionItemDetails row)
    {
        Id = row.Id;
        ItemId = row.ItemId;
        ItemUniqueName = row.ItemUniqueName;
        ItemName = row.ItemName;
        Quality = row.Quality;
        Amount = row.Amount;
        EstimatedMarketValue = row.EstimatedMarketValue;
        TotalEstimatedMarketValue = row.TotalEstimatedMarketValue;
        Source = row.Source;
    }

    public Guid Id { get; }
    public int ItemId { get; }
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
