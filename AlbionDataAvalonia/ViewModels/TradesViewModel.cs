using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Locations.Models;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.Ui.Services;
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
    private readonly PortfolioImportCoordinator _portfolio = null!;
    private UiDebouncer? _filterDebounce;
    private UiDebouncer? _loadDebounce;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly LatestLoad _tradeLoads = new();
    private Task? _backgroundPortfolioRefresh;
    private bool _isPortfolioStatusLoading;

    [ObservableProperty]
    private string filterText = string.Empty;

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private int exportProgress;

    [ObservableProperty]
    private bool hasSelectedRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string loadError = string.Empty;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

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

    private List<string> _locations = [UiText.Any];
    public List<string> Locations => _locations;

    private async Task RefreshLocationOptionsAsync()
    {
        try
        {
            var selectedServer = SelectedServer.Value;
            var locationIds = await Task.Run(() => _tradeService.GetDistinctLocationIds(selectedServer));

            var locations = locationIds
                .Select(id => AlbionLocations.ResolveStoredLocation(string.Empty, id))
                .Select(location => location?.MarketLocation?.FriendlyName ?? location?.FriendlyName)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .OrderBy(x => x)
                .Cast<string>()
                .ToList();
            locations.Insert(0, UiText.Any);

            // Scoped by server only (never the location filter), so selecting a location
            // doesn't shrink the list. Only notify when the set actually changes to avoid
            // ItemsSource churn that would round-trip the selection.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (selectedServer == SelectedServer.Value && !_locations.SequenceEqual(locations))
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
    private string selectedLocation = UiText.Any;

    private static readonly Option<TradeOperation?> AnyOperation = new(null, UiText.Any);
    public IReadOnlyList<Option<TradeOperation?>> TradeOperations { get; } = [AnyOperation, new(TradeOperation.Buy, "Bought"), new(TradeOperation.Sell, "Sold")];
    [ObservableProperty]
    private Option<TradeOperation?> selectedOperation = AnyOperation;

    private static readonly Option<TradeType?> AnyTradeType = new(null, UiText.Any);
    public IReadOnlyList<Option<TradeType?>> TradeTypes { get; } = [AnyTradeType, new(TradeType.Instant, "Instant"), new(TradeType.Order, "Order")];
    [ObservableProperty]
    private Option<TradeType?> selectedTradeType = AnyTradeType;

    public IReadOnlyList<Option<int?>> Servers => UiOptions.Servers;
    [ObservableProperty]
    private Option<int?> selectedServer = UiOptions.Servers[0];

    public IReadOnlyList<Option<int>> TradesToLoadOptions => UiOptions.TradeCounts;

    public bool IsSelectedPortfolioPostLimitExceeded => PortfolioUploadService.IsPostLimitExceeded(SelectedPortfolioPostCount);

    public string PortfolioPostLimitHelperText => IsSelectedPortfolioPostLimitExceeded
        ? PortfolioUploadService.CreatePostLimitMessage(SelectedPortfolioPostCount)
        : string.Empty;

    public bool CanSetSelectedTradeQuality => HasSelectedQualityEditableRows && !IsSettingQuality && !_isPortfolioStatusLoading;

    public bool CanAddSelectedTradesToPortfolio => HasSelectedRows && !IsAddingToPortfolio && !IsSelectedPortfolioPostLimitExceeded;

    public Option<int> SelectedTradesToLoad
    {
        get => UiOptions.Nearest(UiOptions.TradeCounts, _settingsManager?.UserSettings.TradesToShow ?? UiOptions.TradeCounts[0].Value);
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
    partial void OnSelectedOperationChanged(Option<TradeOperation?>? oldValue, Option<TradeOperation?> newValue) => QueueLoadTradesForSelectionChange(oldValue, newValue);
    partial void OnSelectedTradeTypeChanged(Option<TradeType?>? oldValue, Option<TradeType?> newValue) => QueueLoadTradesForSelectionChange(oldValue, newValue);
    partial void OnSelectedServerChanged(Option<int?>? oldValue, Option<int?> newValue)
    {
        QueueLoadTradesForSelectionChange(oldValue, newValue);

        // Server scope determines which locations exist; refresh the dropdown for the new server.
        if (newValue is not null && !Equals(oldValue, newValue))
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
        PortfolioUploadService portfolioUploadService,
        IDialogService dialogs,
        IClipboardService clipboard,
        IFileDialogService files)
    {
        this.dialogs = dialogs;
        this.clipboard = clipboard;
        this.files = files;
        _settingsManager = settingsManager;
        _playerState = playerState;
        _tradeService = tradeService;
        _csvExportService = csvExportService;
        _portfolio = new PortfolioImportCoordinator(portfolioUploadService, tradeService, playerState);

        _tradeService.OnTradeAdded += HandleTradeAdded;
        _settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        NormalizeTradesToLoadSetting();

        _playerState.OnPlayerStateChanged += (_, _) => Dispatcher.UIThread.Post(FollowPlayerServer);
    }

    // Player state changes come from packet handling, so this runs posted to the UI thread.
    private void FollowPlayerServer()
    {
        var currentServer = UiOptions.Find(UiOptions.Servers, _playerState.AlbionServer?.Id);
        if (SelectedServer != currentServer)
        {
            SelectedServer = currentServer;
        }
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
        var version = _tradeLoads.Begin();
        await _loadGate.WaitAsync();
        try
        {
            if (!_tradeLoads.IsCurrent(version))
            {
                return;
            }
            var filter = GetCurrentTradeFilter();
            var count = _settingsManager.UserSettings.TradesToShow;

            // Microsoft.Data.Sqlite performs its async reads synchronously.
            var loadedTrades = await Task.Run(() => _tradeService.GetTrades(count, 0, filter.AlbionServerId, false, filter.LocationId, filter.TradeType, filter.TradeOperation));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_tradeLoads.IsCurrent(version)
                    || filter != GetCurrentTradeFilter()
                    || count != _settingsManager.UserSettings.TradesToShow)
                {
                    return;
                }
                UnfilteredTrades = loadedTrades;
                CancelPendingFilterRefresh();
                FilterTrades();
                LoadError = string.Empty;
                // Portfolio is remote enrichment; it must not delay local rows or later filter loads.
                if (_backgroundPortfolioRefresh is null || _backgroundPortfolioRefresh.IsCompleted)
                {
                    _backgroundPortfolioRefresh = RefreshPortfolioUploadedTradeIdsAsync();
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load trades");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_tradeLoads.IsCurrent(version))
                {
                    LoadError = "Could not load trades. Your current list is unchanged. Select Refresh to try again.";
                }
            });
        }
        finally
        {
            _loadGate.Release();
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
            .Select(trade => new TradeRowViewModel(trade, _portfolio.IsUploaded(trade.Id)))
            .ToList();

        Trades = new ObservableCollection<TradeRowViewModel>(rows);
    }

    public async Task<bool> RefreshPortfolioUploadedTradeIdsAsync(
        bool showStatusOnFailure = false,
        CancellationToken cancellationToken = default)
    {
        SetPortfolioStatusLoading(true);
        var refresh = await _portfolio.RefreshUploadedAsync(cancellationToken);
        if (!refresh.IsCurrent) return false;
        ApplyPortfolioUploadedTradeIdsToVisibleRows();
        SetPortfolioStatusLoading(false);
        if (!refresh.Success && refresh.ErrorMessage is { } error && showStatusOnFailure) SetPortfolioImportStatus($"Portfolio: {error}");
        return refresh.Success;
    }

    private void SetPortfolioStatusLoading(bool value)
    {
        _isPortfolioStatusLoading = value;
        OnPropertyChanged(nameof(CanSetSelectedTradeQuality));
    }

    private void ApplyPortfolioUploadedTradeIdsToVisibleRows()
    {
        foreach (var row in Trades)
        {
            row.SetUploadedToPortfolio(_portfolio.IsUploaded(row.Source.Id));
        }

        HasSelectedQualityEditableRows = SelectedTrades.Any(trade => !trade.UploadedToPortfolio);
    }

    // A combo box briefly selects nothing while its items change; that is not a new filter.
    private void QueueLoadTradesForSelectionChange(object? oldValue, object? newValue)
    {
        if (newValue is null or "" || Equals(oldValue, newValue))
        {
            return;
        }

        ScheduleLoadTrades();
    }

    // Several filters can change at once, such as the server and its locations; load once they settle.
    private void ScheduleLoadTrades() => (_loadDebounce ??= new(TimeSpan.FromMilliseconds(100), () => _ = LoadTrades())).Trigger();

    private CurrentTradeFilter GetCurrentTradeFilter()
    {
        var location = string.IsNullOrWhiteSpace(SelectedLocation) ? null : AlbionLocations.Get(SelectedLocation);
        return new CurrentTradeFilter(SelectedServer?.Value, location?.MarketLocation?.IdInt ?? location?.IdInt,
            SelectedTradeType?.Value, SelectedOperation?.Value);
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
        SelectedPortfolioPostCount = PortfolioImportCoordinator.EstimatePostCount(selectedTrades);
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
        if (await _portfolio.CanUploadAsync(cancellationToken))
        {
            return true;
        }

        SetPortfolioImportStatus("Sign in to AFM before adding trades to Portfolio.");
        return false;
    }

    public async Task<int> DeleteSelectedTradesAsync(
        IEnumerable<TradeRowViewModel> selected,
        CancellationToken cancellationToken = default)
    {
        var selectedTrades = selected?.ToList() ?? new List<TradeRowViewModel>();
        if (selectedTrades.Count == 0)
        {
            return 0;
        }

        SetPortfolioImportStatus("Deleting selected trades...");
        var deletedCount = await _tradeService.DeleteTradesAsync(
            selectedTrades.Select(row => row.Source.Id),
            cancellationToken);

        await LoadTrades();
        await RefreshLocationOptionsAsync();
        UpdateSelectedTrades([]);
        SetPortfolioImportStatus($"Delete: {Ui.UiFormat.Count(deletedCount, "trade")} deleted.");
        return deletedCount;
    }

    public async Task SetSelectedTradesQualityAsync(
        IEnumerable<TradeRowViewModel> selected,
        int qualityLevel,
        CancellationToken cancellationToken = default)
    {
        var selectedTrades = selected?.ToList() ?? new List<TradeRowViewModel>();
        var editableTrades = selectedTrades.Where(row => !_portfolio.IsUploaded(row.Source.Id)).ToList();
        if (selectedTrades.Count == 0 || editableTrades.Count == 0 || IsSettingQuality || _isPortfolioStatusLoading)
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
            var status = $"Quality: {UiFormat.Count(updatedCount, "trade")} updated to {ItemQuality.Format(qualityLevel)}.";
            var skippedCount = selectedTrades.Count - editableTrades.Count;
            if (skippedCount > 0)
            {
                status += $" {UiFormat.Count(skippedCount, "uploaded trade")} skipped.";
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
        bool? premiumOverride,
        CancellationToken cancellationToken = default)
    {
        var selectedTrades = selected?.ToList() ?? new List<TradeRowViewModel>();
        if (selectedTrades.Count == 0 || IsAddingToPortfolio)
        {
            return;
        }

        var estimatedPostCount = PortfolioImportCoordinator.EstimatePostCount(selectedTrades);
        if (PortfolioUploadService.IsPostLimitExceeded(estimatedPostCount))
        {
            SelectedPortfolioPostCount = estimatedPostCount;
            SetPortfolioImportStatus(PortfolioPostLimitHelperText);
            return;
        }

        IsAddingToPortfolio = true;
        SetPortfolioImportStatus("Adding selected trades to Portfolio…");
        try
        {
            SetPortfolioImportStatus(await _portfolio.ImportAsync(selectedTrades, qualityOverrides, allowReupload, premiumOverride, cancellationToken));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to add trades to Portfolio");
            SetPortfolioImportStatus("Portfolio upload failed. Check logs for details.");
        }
        finally
        {
            IsAddingToPortfolio = false;
            // The upload can set qualities and mark trades as uploaded, which changes what the selection can do.
            ApplyPortfolioUploadedTradeIdsToVisibleRows();
            SetPortfolioStatusLoading(false);
            UpdateSelectedTrades(selectedTrades);
        }
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
        var normalized = UiOptions.Nearest(UiOptions.TradeCounts, _settingsManager.UserSettings.TradesToShow).Value;
        if (_settingsManager.UserSettings.TradesToShow != normalized)
        {
            _settingsManager.UserSettings.TradesToShow = normalized;
        }
    }

    private void ScheduleFilterTrades() => (_filterDebounce ??= new(TimeSpan.FromMilliseconds(250), FilterTrades)).Trigger();

    private void CancelPendingFilterRefresh() => _filterDebounce?.Cancel();
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
    public string PremiumStatusFormatted => Source.HasPremium switch
    {
        true => "Yes",
        false => "No",
        null => "Unknown"
    };
    public DateTime DateTime => Source.DateTime;
    public string DateTimeUtcFormatted => FormatUtc(DateTime);
    public AlbionServer? Server => Source.Server;
    public string TradeTypeFormatted => Source.TradeTypeFormatted;
    public string TradeOperationFormatted => Source.TradeOperationFormatted;
    public string QualityLevelFormatted => Source.QualityLevelFormatted;
    public string ItemName => Source.ItemName;
    public AlbionLocation? Location => Source.Location;
    public int Amount => Source.Amount;
    public bool IsPartialOrder => Source.IsPartialOrder;
    public string OrderAmountFormatted => Source.OrderAmountFormatted;
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
