using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
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

public partial class TradesViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly TradeService _tradeService;
    private readonly CsvExportService _csvExportService;
    private readonly PortfolioUploadService _portfolioUploadService;
    private readonly TimeSpan _filterDebounceInterval = TimeSpan.FromMilliseconds(250);
    private readonly TimeSpan _loadDebounceInterval = TimeSpan.FromMilliseconds(100);
    private IDisposable? _pendingFilterRefreshRegistration;
    private IDisposable? _pendingLoadTradesRegistration;
    private static readonly IReadOnlyList<NumericOption> _tradesToLoadOptions = NumericOptions.MailAndTradeLoadOptions;

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private int exportProgress;

    [ObservableProperty]
    private bool hasSelectedRows;

    [ObservableProperty]
    private bool hasSelectedQualityEditableRows;

    [ObservableProperty]
    private bool isAddingToPortfolio;

    [ObservableProperty]
    private bool isSettingQuality;

    [ObservableProperty]
    private string portfolioImportStatus = string.Empty;

    [ObservableProperty]
    private bool hasPortfolioImportStatus;

    [ObservableProperty]
    private long selectedAmountTotal;

    [ObservableProperty]
    private decimal selectedTotalSilver;

    [ObservableProperty]
    private decimal selectedAverageSilver;

    [ObservableProperty]
    private int selectedPortfolioPostCount;

    private ObservableCollection<TradeRowViewModel> trades = new();
    public ObservableCollection<TradeRowViewModel> Trades
    {
        get { return trades; }
        set { SetProperty(ref trades, value); }
    }

    private List<Trade> UnfilteredTrades { get; set; } = new();
    private List<TradeRowViewModel> SelectedTrades { get; set; } = new();
    private HashSet<Guid> PortfolioUploadedTradeIds { get; set; } = new();

    private List<string> _locations = ["Any"];
    public List<string> Locations => _locations;

    private async Task RefreshLocationOptionsAsync()
    {
        try
        {
            AlbionServers.TryParse(GetSelectedServer(), out AlbionServer? server);
            var locationIds = await _tradeService.GetDistinctLocationIds(server?.Id);

            var locations = locationIds
                .Select(id => AlbionLocations.ResolveStoredLocation(string.Empty, id))
                .Select(location => location?.MarketLocation?.FriendlyName ?? location?.FriendlyName)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .OrderBy(x => x)
                .Cast<string>()
                .ToList();
            locations.Insert(0, "Any");

            // Scoped by server only (never the location filter), so selecting a location
            // doesn't shrink the list. Only notify when the set actually changes to avoid
            // ItemsSource churn that would round-trip the selection.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_locations.SequenceEqual(locations))
                {
                    _locations = locations;
                    OnPropertyChanged(nameof(Locations));
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh location options");
        }
    }
    [ObservableProperty]
    private string selectedLocation = "Any";

    public List<string> TradeOperations { get; set; } = new() { "Any", "Bought", "Sold" };
    [ObservableProperty]
    private string selectedOperation = "Any";

    public List<string> TradeTypes { get; set; } = new() { "Any", "Instant", "Order" };
    [ObservableProperty]
    private string selectedTradeType = "Any";

    public List<string> Servers { get; set; } = new();
    [ObservableProperty]
    private string selectedServer = "Any";

    public IReadOnlyList<NumericOption> TradesToLoadOptions => _tradesToLoadOptions;

    public bool IsSelectedPortfolioPostLimitExceeded => PortfolioUploadService.IsPostLimitExceeded(SelectedPortfolioPostCount);

    public string PortfolioPostLimitHelperText => IsSelectedPortfolioPostLimitExceeded
        ? PortfolioUploadService.CreatePostLimitMessage(SelectedPortfolioPostCount)
        : string.Empty;

    public bool CanSetSelectedTradeQuality => HasSelectedQualityEditableRows && !IsSettingQuality;

    public bool CanAddSelectedTradesToPortfolio => HasSelectedRows && !IsAddingToPortfolio && !IsSelectedPortfolioPostLimitExceeded;

    public NumericOption SelectedTradesToLoad
    {
        get
        {
            var current = _settingsManager?.UserSettings.TradesToShow ?? _tradesToLoadOptions[0].Value;
            return ResolveOption(_tradesToLoadOptions, current);
        }
        set
        {
            if (value is null || _settingsManager is null)
            {
                return;
            }

            if (_settingsManager.UserSettings.TradesToShow != value.Value)
            {
                _settingsManager.UserSettings.TradesToShow = value.Value;
                ScheduleLoadTrades();
            }
        }
    }

    partial void OnFilterTextChanged(string? oldValue, string newValue) => ScheduleFilterTrades();
    partial void OnSelectedLocationChanged(string? oldValue, string newValue) => QueueLoadTradesForSelectionChange(oldValue, newValue);
    partial void OnSelectedOperationChanged(string? oldValue, string newValue) => QueueLoadTradesForSelectionChange(oldValue, newValue);
    partial void OnSelectedTradeTypeChanged(string? oldValue, string newValue) => QueueLoadTradesForSelectionChange(oldValue, newValue);
    partial void OnSelectedServerChanged(string? oldValue, string newValue)
    {
        QueueLoadTradesForSelectionChange(oldValue, newValue);

        // Server scope determines which locations exist; refresh the dropdown for the new server.
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            _ = RefreshLocationOptionsAsync();
        }
    }

    partial void OnHasSelectedRowsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAddSelectedTradesToPortfolio));
    }

    partial void OnHasSelectedQualityEditableRowsChanged(bool value) => OnPropertyChanged(nameof(CanSetSelectedTradeQuality));

    partial void OnIsAddingToPortfolioChanged(bool value) => OnPropertyChanged(nameof(CanAddSelectedTradesToPortfolio));

    partial void OnIsSettingQualityChanged(bool value) => OnPropertyChanged(nameof(CanSetSelectedTradeQuality));

    partial void OnSelectedPortfolioPostCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsSelectedPortfolioPostLimitExceeded));
        OnPropertyChanged(nameof(PortfolioPostLimitHelperText));
        OnPropertyChanged(nameof(CanAddSelectedTradesToPortfolio));
    }

    public TradesViewModel()
    {
    }

    public TradesViewModel(
        SettingsManager settingsManager,
        PlayerState playerState,
        TradeService tradeService,
        CsvExportService csvExportService,
        PortfolioUploadService portfolioUploadService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        _tradeService = tradeService;
        _csvExportService = csvExportService;
        _portfolioUploadService = portfolioUploadService;

        _tradeService.OnTradeAdded += HandleTradeAdded;
        _settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        NormalizeTradesToLoadSetting();

        Servers = AlbionServers.GetAll().Select(x => x.Name).ToList();
        Servers.Insert(0, "Any");

        _playerState.OnPlayerStateChanged += (sender, args) =>
        {
            var currentServer = playerState.AlbionServer?.Name ?? "Any";
            if (SelectedServer != currentServer)
            {
                SelectedServer = currentServer;
            }
        };
    }

    private bool _hasLoadedInitialTrades;

    public void EnsureLoaded()
    {
        if (_hasLoadedInitialTrades)
        {
            return;
        }

        _hasLoadedInitialTrades = true;
        ScheduleLoadTrades();
        _ = RefreshLocationOptionsAsync();
    }

    [RelayCommand]
    public async Task LoadTrades()
    {
        try
        {
            var filter = GetCurrentTradeFilter();

            var uploadedTradeIdsTask = LoadPortfolioUploadedTradeIdsAsync();
            var loadedTrades = await _tradeService.GetTrades(_settingsManager.UserSettings.TradesToShow, 0, filter.AlbionServerId, false, filter.LocationId, filter.TradeType, filter.TradeOperation);
            var uploadedTradeIdsResult = await uploadedTradeIdsTask;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetPortfolioUploadedTradeIds(uploadedTradeIdsResult.Success ? uploadedTradeIdsResult.TradeIds : null);
                UnfilteredTrades = loadedTrades;
                CancelPendingFilterRefresh();
                FilterTrades();
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load trades");
        }
    }

    private void HandleTradeAdded(Trade trade)
    {
        ScheduleLoadTrades();

        // A trade at a location not yet in the dropdown should make it appear there.
        var marketName = trade.Location?.MarketLocation?.FriendlyName ?? trade.Location?.FriendlyName;
        if (!string.IsNullOrEmpty(marketName) && !_locations.Contains(marketName))
        {
            _ = RefreshLocationOptionsAsync();
        }
    }

    private void FilterTrades()
    {
        List<Trade> filteredList;
        var normalizedFilterText = (FilterText ?? string.Empty).Replace(" ", string.Empty);
        if (!string.IsNullOrEmpty(normalizedFilterText))
        {
            filteredList = UnfilteredTrades.Where(x => (x.ItemName ?? string.Empty)
                .Replace(" ", string.Empty)
                .Contains(normalizedFilterText, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            filteredList = UnfilteredTrades;
        }
        var rows = filteredList
            .OrderByDescending(x => x.DateTime)
            .Take(_settingsManager.UserSettings.TradesToShow)
            .Select(trade => new TradeRowViewModel(trade, PortfolioUploadedTradeIds.Contains(trade.Id)))
            .ToList();

        Trades = new ObservableCollection<TradeRowViewModel>(rows);
    }

    private async Task<PortfolioUploadedTradeIdsResult> LoadPortfolioUploadedTradeIdsAsync(CancellationToken cancellationToken = default)
    {
        return await _portfolioUploadService.GetUploadedTradeIdsAsync(cancellationToken);
    }

    public async Task<bool> RefreshPortfolioUploadedTradeIdsAsync(
        bool showStatusOnFailure = false,
        CancellationToken cancellationToken = default)
    {
        var uploadedTradeIdsResult = await LoadPortfolioUploadedTradeIdsAsync(cancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SetPortfolioUploadedTradeIds(uploadedTradeIdsResult.Success ? uploadedTradeIdsResult.TradeIds : null);
        });

        if (uploadedTradeIdsResult.Success)
        {
            return true;
        }

        if (showStatusOnFailure)
        {
            SetPortfolioImportStatus($"Portfolio: {uploadedTradeIdsResult.ErrorMessage ?? "failed to load positions."}");
        }

        return false;
    }

    private void SetPortfolioUploadedTradeIds(HashSet<Guid>? uploadedTradeIds)
    {
        PortfolioUploadedTradeIds = uploadedTradeIds ?? new HashSet<Guid>();
        ApplyPortfolioUploadedTradeIdsToVisibleRows();
    }

    private void AddPortfolioUploadedTradeIds(IEnumerable<Guid> tradeIds)
    {
        foreach (var tradeId in tradeIds)
        {
            PortfolioUploadedTradeIds.Add(tradeId);
        }

        ApplyPortfolioUploadedTradeIdsToVisibleRows();
    }

    private void ApplyPortfolioUploadedTradeIdsToVisibleRows()
    {
        foreach (var row in Trades)
        {
            row.SetUploadedToPortfolio(PortfolioUploadedTradeIds.Contains(row.Source.Id));
        }

        HasSelectedQualityEditableRows = SelectedTrades.Any(trade => !trade.UploadedToPortfolio);
    }

    private void QueueLoadTradesForSelectionChange(string? oldValue, string? newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue) || string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        ScheduleLoadTrades();
    }

    private void ScheduleLoadTrades()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleLoadTrades);
            return;
        }

        _pendingLoadTradesRegistration?.Dispose();
        _pendingLoadTradesRegistration = DispatcherTimer.RunOnce(() =>
        {
            _pendingLoadTradesRegistration = null;
            _ = LoadTrades();
        }, _loadDebounceInterval);
    }

    private string GetSelectedLocation()
    {
        return string.IsNullOrWhiteSpace(SelectedLocation) ? "Any" : SelectedLocation;
    }

    private string GetSelectedOperation()
    {
        return string.IsNullOrWhiteSpace(SelectedOperation) ? "Any" : SelectedOperation;
    }

    private string GetSelectedTradeType()
    {
        return string.IsNullOrWhiteSpace(SelectedTradeType) ? "Any" : SelectedTradeType;
    }

    private string GetSelectedServer()
    {
        return string.IsNullOrWhiteSpace(SelectedServer) ? "Any" : SelectedServer;
    }

    private CurrentTradeFilter GetCurrentTradeFilter()
    {
        var location = AlbionLocations.Get(GetSelectedLocation());
        AlbionServers.TryParse(GetSelectedServer(), out AlbionServer? server);
        var selectedTradeType = GetSelectedTradeType();
        var selectedOperation = GetSelectedOperation();
        TradeType? tradeType = selectedTradeType == "Instant"
            ? TradeType.Instant
            : selectedTradeType == "Order"
                ? TradeType.Order
                : null;
        TradeOperation? tradeOperation = selectedOperation == "Sold"
            ? TradeOperation.Sell
            : selectedOperation == "Bought"
                ? TradeOperation.Buy
                : null;

        return new CurrentTradeFilter(server?.Id, location?.MarketLocation?.IdInt ?? location?.IdInt, tradeType, tradeOperation);
    }

    private readonly record struct CurrentTradeFilter(int? AlbionServerId, int? LocationId, TradeType? TradeType, TradeOperation? TradeOperation);

    public void UpdateSelectedTrades(IEnumerable<TradeRowViewModel> selected)
    {
        var selectedTrades = selected?.ToList() ?? new List<TradeRowViewModel>();
        SelectedTrades = selectedTrades;
        if (selectedTrades.Count == 0)
        {
            HasSelectedRows = false;
            HasSelectedQualityEditableRows = false;
            SelectedAmountTotal = 0;
            SelectedTotalSilver = 0;
            SelectedAverageSilver = 0;
            SelectedPortfolioPostCount = 0;
            return;
        }

        long amountTotal = 0;
        decimal totalSilver = 0;
        foreach (var trade in selectedTrades)
        {
            amountTotal += trade.Amount;
            totalSilver += trade.TotalSilver;
        }

        HasSelectedRows = true;
        HasSelectedQualityEditableRows = selectedTrades.Any(trade => !trade.UploadedToPortfolio);
        SelectedAmountTotal = amountTotal;
        SelectedTotalSilver = totalSilver;
        SelectedAverageSilver = amountTotal == 0 ? 0 : totalSilver / amountTotal;
        SelectedPortfolioPostCount = EstimatePortfolioPostCount(selectedTrades);
    }

    public async Task ExportToCsvAsync(Stream stream, CsvExportOptions options, CancellationToken cancellationToken = default)
    {
        IsExporting = true;
        ExportProgress = 0;
        try
        {
            var progress = new Progress<int>(p => ExportProgress = p);
            await _csvExportService.ExportTradesToCsvAsync(stream, options, progress, cancellationToken);
        }
        finally
        {
            IsExporting = false;
        }
    }

    public async Task<bool> EnsurePortfolioSignedInAsync(CancellationToken cancellationToken = default)
    {
        if (await _portfolioUploadService.CanUploadAsync(cancellationToken))
        {
            return true;
        }

        SetPortfolioImportStatus("Sign in to AFM before adding trades to Portfolio.");
        return false;
    }

    public async Task SetSelectedTradesQualityAsync(
        IEnumerable<TradeRowViewModel> selected,
        int qualityLevel,
        CancellationToken cancellationToken = default)
    {
        var selectedTrades = selected?.ToList() ?? new List<TradeRowViewModel>();
        var editableTrades = selectedTrades.Where(row => !row.UploadedToPortfolio).ToList();
        if (selectedTrades.Count == 0 || editableTrades.Count == 0 || IsSettingQuality)
        {
            return;
        }

        if (qualityLevel is < 0 or > 5)
        {
            SetPortfolioImportStatus("Quality update failed. Select a quality between Unknown and Masterpiece.");
            return;
        }

        IsSettingQuality = true;
        SetPortfolioImportStatus("Updating selected trade quality...");

        try
        {
            var updatedCount = await _tradeService.UpdateTradeQualityLevelsAsync(
                editableTrades.Select(row => row.Source.Id),
                (byte)qualityLevel);

            if (updatedCount != editableTrades.Count)
            {
                SetPortfolioImportStatus("Quality update failed. Check logs for details.");
                return;
            }

            foreach (var row in editableTrades)
            {
                row.SetQualityLevel((byte)qualityLevel);
            }

            UpdateSelectedTrades(selectedTrades);
            var status = $"Quality: {updatedCount:N0} trade{(updatedCount == 1 ? string.Empty : "s")} updated to {ItemQuality.Format(qualityLevel)}.";
            var skippedCount = selectedTrades.Count - editableTrades.Count;
            if (skippedCount > 0)
            {
                status += $" {skippedCount:N0} uploaded trade{(skippedCount == 1 ? string.Empty : "s")} skipped.";
            }

            SetPortfolioImportStatus(status);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update selected trade quality");
            SetPortfolioImportStatus("Quality update failed. Check logs for details.");
        }
        finally
        {
            IsSettingQuality = false;
        }
    }

    public async Task AddTradesToPortfolioAsync(
        IEnumerable<TradeRowViewModel> selected,
        IReadOnlyDictionary<PortfolioTradeQualityKey, int> qualityOverrides,
        bool allowReupload,
        CancellationToken cancellationToken = default)
    {
        var selectedTrades = selected?.ToList() ?? new List<TradeRowViewModel>();
        if (selectedTrades.Count == 0 || IsAddingToPortfolio)
        {
            return;
        }

        var estimatedPostCount = EstimatePortfolioPostCount(selectedTrades);
        if (PortfolioUploadService.IsPostLimitExceeded(estimatedPostCount))
        {
            SelectedPortfolioPostCount = estimatedPostCount;
            SetPortfolioImportStatus(PortfolioPostLimitHelperText);
            return;
        }

        IsAddingToPortfolio = true;
        SetPortfolioImportStatus("Adding selected trades to Portfolio...");

        try
        {
            await SavePortfolioQualityOverridesAsync(selectedTrades, qualityOverrides, cancellationToken);

            var requests = new List<PortfolioTradeImportRequest>();
            var invalidTrades = 0;

            foreach (var row in selectedTrades)
            {
                var trade = row.Source;
                var locationIndex = trade.Location?.MarketLocation?.IdInt
                    ?? trade.Location?.IdInt
                    ?? trade.LocationId;

                if (locationIndex < 0 || string.IsNullOrWhiteSpace(trade.ItemId) || trade.Amount <= 0)
                {
                    invalidTrades++;
                    Log.Warning(
                        "Portfolio import request skipped invalid trade {TradeId}. Item={ItemId} Amount={Amount} LocationIndex={LocationIndex}",
                        trade.Id,
                        trade.ItemId,
                        trade.Amount,
                        locationIndex);
                    continue;
                }

                var qualityIndex = trade.QualityLevel > 0
                    ? trade.QualityLevel
                    : qualityOverrides.TryGetValue(CreateQualityKey(row), out var selectedQuality)
                        ? selectedQuality
                        : 1;

                requests.Add(new PortfolioTradeImportRequest(
                    trade.Id,
                    trade.ItemId,
                    trade.AlbionServerId,
                    trade.Type,
                    trade.Operation,
                    trade.Amount,
                    trade.UnitSilver,
                    trade.DateTime,
                    locationIndex,
                    qualityIndex));
            }

            var result = await _portfolioUploadService.ImportTradesAsync(requests, allowReupload, cancellationToken);
            var uploadedOrAlreadyPresentIds = result.ImportedTradeIds
                .Concat(result.SkippedTradeIds)
                .Distinct()
                .ToList();
            if (uploadedOrAlreadyPresentIds.Count > 0)
            {
                AddPortfolioUploadedTradeIds(uploadedOrAlreadyPresentIds);
            }

            var failedCount = result.FailedCount + invalidTrades;
            var status = $"Portfolio: {result.ImportedCount:N0} imported";
            if (result.ReuploadedCount > 0)
            {
                status += $", {result.ReuploadedCount:N0} reuploaded";
            }
            if (result.SkippedCount > 0)
            {
                status += $", {result.SkippedCount:N0} skipped";
            }
            if (failedCount > 0)
            {
                status += $", {failedCount:N0} failed";
            }
            if (result.Errors.Count > 0)
            {
                status += $". {result.Errors[0]}";
            }
            else if (result.Warnings.Count > 0)
            {
                status += $". {result.Warnings[0]}";
            }

            SetPortfolioImportStatus(status);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to add trades to Portfolio");
            SetPortfolioImportStatus("Portfolio upload failed. Check logs for details.");
        }
        finally
        {
            IsAddingToPortfolio = false;
        }
    }

    private async Task SavePortfolioQualityOverridesAsync(
        IReadOnlyCollection<TradeRowViewModel> selectedTrades,
        IReadOnlyDictionary<PortfolioTradeQualityKey, int> qualityOverrides,
        CancellationToken cancellationToken)
    {
        var rowsToUpdate = selectedTrades
            .Where(row => row.QualityLevel == 0
                && !row.UploadedToPortfolio
                && qualityOverrides.TryGetValue(CreateQualityKey(row), out var quality)
                && quality is >= 1 and <= 5)
            .ToList();

        if (rowsToUpdate.Count == 0)
        {
            return;
        }

        foreach (var group in rowsToUpdate.GroupBy(row => qualityOverrides[CreateQualityKey(row)]))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var qualityLevel = (byte)group.Key;
            var groupRows = group.ToList();
            var updatedCount = await _tradeService.UpdateTradeQualityLevelsAsync(
                groupRows.Select(row => row.Source.Id),
                qualityLevel);

            if (updatedCount != groupRows.Count)
            {
                throw new InvalidOperationException("Failed to save selected Portfolio trade qualities.");
            }

            foreach (var row in groupRows)
            {
                row.SetQualityLevel(qualityLevel);
            }
        }

        UpdateSelectedTrades(selectedTrades);
    }

    public static PortfolioTradeQualityKey CreateQualityKey(TradeRowViewModel row)
    {
        return new PortfolioTradeQualityKey(row.ItemId, row.Source.AlbionServerId);
    }

    private static int EstimatePortfolioPostCount(IEnumerable<TradeRowViewModel> selectedTrades)
    {
        return PortfolioUploadService.EstimatePortfolioPostCount(selectedTrades.Select(CreatePortfolioPostEstimate));
    }

    private static PortfolioTradePostEstimate CreatePortfolioPostEstimate(TradeRowViewModel row)
    {
        var trade = row.Source;
        var locationIndex = trade.Location?.MarketLocation?.IdInt
            ?? trade.Location?.IdInt
            ?? trade.LocationId;

        return new PortfolioTradePostEstimate(
            trade.ItemId,
            trade.AlbionServerId,
            trade.Amount,
            locationIndex,
            trade.QualityLevel > 0 ? trade.QualityLevel : 0);
    }

    private void SetPortfolioImportStatus(string status)
    {
        PortfolioImportStatus = status;
        HasPortfolioImportStatus = !string.IsNullOrWhiteSpace(status);
    }

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.TradesToShow))
        {
            OnPropertyChanged(nameof(SelectedTradesToLoad));
        }
    }

    private void NormalizeTradesToLoadSetting()
    {
        var normalized = NormalizeOption(_tradesToLoadOptions, _settingsManager.UserSettings.TradesToShow);
        if (_settingsManager.UserSettings.TradesToShow != normalized)
        {
            _settingsManager.UserSettings.TradesToShow = normalized;
        }
    }

    private static NumericOption ResolveOption(IReadOnlyList<NumericOption> options, int value)
    {
        var normalized = NormalizeOption(options, value);
        return options.First(option => option.Value == normalized);
    }

    private static int NormalizeOption(IReadOnlyList<NumericOption> options, int value)
    {
        if (options.Count == 0)
        {
            return value;
        }

        var nearest = options.OrderBy(option => Math.Abs(option.Value - value)).First();
        return nearest.Value;
    }

    private void ScheduleFilterTrades()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleFilterTrades);
            return;
        }

        CancelPendingFilterRefresh();
        _pendingFilterRefreshRegistration = DispatcherTimer.RunOnce(() =>
        {
            _pendingFilterRefreshRegistration = null;
            FilterTrades();
        }, _filterDebounceInterval);
    }

    private void CancelPendingFilterRefresh()
    {
        _pendingFilterRefreshRegistration?.Dispose();
        _pendingFilterRefreshRegistration = null;
    }
}

public sealed class TradeRowViewModel : ObservableObject
{
    private bool uploadedToPortfolio;

    public TradeRowViewModel(Trade trade, bool uploadedToPortfolio)
    {
        Source = trade;
        this.uploadedToPortfolio = uploadedToPortfolio;
    }

    public Trade Source { get; }
    public string PlayerName => Source.PlayerName;
    public DateTime DateTime => Source.DateTime;
    public string DateTimeUtcFormatted => FormatUtc(DateTime);
    public AlbionServer? Server => Source.Server;
    public string TradeTypeFormatted => Source.TradeTypeFormatted;
    public string TradeOperationFormatted => Source.TradeOperationFormatted;
    public string QualityLevelFormatted => Source.QualityLevelFormatted;
    public string ItemName => Source.ItemName;
    public AlbionLocation? Location => Source.Location;
    public int Amount => Source.Amount;
    public string ItemId => Source.ItemId;
    public byte QualityLevel => Source.QualityLevel;
    public int ImageQuality => QualityLevel > 0 ? QualityLevel : 1;
    public double UnitSilver => Source.UnitSilver;
    public ulong TotalSilver => Source.TotalSilver;
    public bool UploadedToPortfolio
    {
        get => uploadedToPortfolio;
        private set
        {
            if (SetProperty(ref uploadedToPortfolio, value))
            {
                OnPropertyChanged(nameof(UploadedToPortfolioFormatted));
            }
        }
    }
    public string UploadedToPortfolioFormatted => UploadedToPortfolio ? "Yes" : string.Empty;

    public void SetUploadedToPortfolio(bool uploadedToPortfolio)
    {
        UploadedToPortfolio = uploadedToPortfolio;
    }

    public void SetQualityLevel(byte qualityLevel)
    {
        Source.QualityLevel = qualityLevel;
        OnPropertyChanged(nameof(QualityLevel));
        OnPropertyChanged(nameof(QualityLevelFormatted));
        OnPropertyChanged(nameof(ImageQuality));
    }

    private static string FormatUtc(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return dateTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
    }
}
