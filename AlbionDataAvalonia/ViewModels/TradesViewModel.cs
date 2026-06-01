using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
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
    private long selectedAmountTotal;

    [ObservableProperty]
    private decimal selectedTotalSilver;

    [ObservableProperty]
    private decimal selectedAverageSilver;

    private ObservableCollection<TradeRowViewModel> trades = new();
    public ObservableCollection<TradeRowViewModel> Trades
    {
        get { return trades; }
        set { SetProperty(ref trades, value); }
    }

    private List<Trade> UnfilteredTrades { get; set; } = new();

    public List<string> Locations
    {
        get
        {
            var locations = Trades
                .Select(t => t.Location?.FriendlyName)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .OrderBy(x => x)
                .Cast<string>()
                .ToList();
            locations.Insert(0, "Any");
            return locations;
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
    partial void OnSelectedServerChanged(string? oldValue, string newValue) => QueueLoadTradesForSelectionChange(oldValue, newValue);

    public TradesViewModel()
    {
    }

    public TradesViewModel(
        SettingsManager settingsManager,
        PlayerState playerState,
        TradeService tradeService,
        CsvExportService csvExportService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        _tradeService = tradeService;
        _csvExportService = csvExportService;

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

    [RelayCommand]
    public async Task LoadTrades()
    {
        try
        {
            var filter = GetCurrentTradeFilter();

            var loadedTrades = await _tradeService.GetTrades(_settingsManager.UserSettings.TradesToShow, 0, filter.AlbionServerId, false, filter.LocationId, filter.TradeType, filter.TradeOperation);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
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
            .Select(trade => new TradeRowViewModel(trade))
            .ToList();

        Trades = new ObservableCollection<TradeRowViewModel>(rows);
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

        return new CurrentTradeFilter(server?.Id, location?.IdInt, tradeType, tradeOperation);
    }

    private readonly record struct CurrentTradeFilter(int? AlbionServerId, int? LocationId, TradeType? TradeType, TradeOperation? TradeOperation);

    public void UpdateSelectedTrades(IEnumerable<TradeRowViewModel> selected)
    {
        var selectedTrades = selected?.ToList() ?? new List<TradeRowViewModel>();
        if (selectedTrades.Count == 0)
        {
            HasSelectedRows = false;
            SelectedAmountTotal = 0;
            SelectedTotalSilver = 0;
            SelectedAverageSilver = 0;
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
        SelectedAmountTotal = amountTotal;
        SelectedTotalSilver = totalSilver;
        SelectedAverageSilver = amountTotal == 0 ? 0 : totalSilver / amountTotal;
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
    public TradeRowViewModel(Trade trade)
    {
        Source = trade;
    }

    public Trade Source { get; }
    public string PlayerName => Source.PlayerName;
    public DateTime DateTime => Source.DateTime;
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
}
