using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class TradesViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly TradeService _tradeService;

    [ObservableProperty]
    private string filterText = string.Empty;

    private ObservableCollection<Trade> trades = new();
    public ObservableCollection<Trade> Trades
    {
        get { return trades; }
        set { SetProperty(ref trades, value); }
    }

    private List<Trade> UnfilteredTrades { get; set; } = new();

    public List<string> Locations { get; set; } = new();
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

    partial void OnFilterTextChanged(string? oldValue, string newValue) => FilterTrades();
    partial void OnSelectedLocationChanged(string? oldValue, string newValue) => Task.Run(() => LoadTrades());
    partial void OnSelectedOperationChanged(string? oldValue, string newValue) => Task.Run(() => LoadTrades());
    partial void OnSelectedTradeTypeChanged(string? oldValue, string newValue) => Task.Run(() => LoadTrades());
    partial void OnSelectedServerChanged(string? oldValue, string newValue) => Task.Run(() => LoadTrades());

    public TradesViewModel()
    {
    }

    public TradesViewModel(SettingsManager settingsManager, PlayerState playerState, TradeService tradeService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        _tradeService = tradeService;

        _tradeService.OnTradeAdded += HandleTradeAdded;

        Locations = AlbionLocations.GetAll().Select(x => x.FriendlyName).OrderBy(x => x).ToList();
        Locations.Insert(0, "Any");

        Servers = AlbionServers.GetAll().Select(x => x.Name).ToList();
        Servers.Insert(0, "Any");

        _playerState.OnPlayerStateChanged += (sender, args) =>
        {
            var currentServer = playerState.AlbionServer?.Name ?? "Any";
            if (SelectedServer != currentServer)
            {
                SelectedServer = currentServer;
                Task.Run(() => LoadTrades());
            }
        };
    }

    [RelayCommand]
    public async Task LoadTrades()
    {
        try
        {
            var location = AlbionLocations.Get(SelectedLocation);
            AlbionServers.TryParse(SelectedServer, out AlbionServer? server);
            TradeType? tradeType = SelectedTradeType == "Instant" ? TradeType.Instant : SelectedTradeType == "Order" ? TradeType.Order : null;
            TradeOperation? tradeOperation = SelectedOperation == "Sold" ? TradeOperation.Sell : SelectedOperation == "Bought" ? TradeOperation.Buy : null;

            UnfilteredTrades = await _tradeService.GetTrades(_settingsManager.UserSettings.TradesToShow, 0, server?.Id ?? null, false, location?.Id ?? null, tradeType, tradeOperation);
            FilterTrades();
        }
        catch
        {
            Log.Error("Failed to load trades");
        }
    }

    private async void HandleTradeAdded(Trade trade)
    {
        await LoadTrades();
    }

    private void FilterTrades()
    {
        List<Trade> filteredList;
        if (!string.IsNullOrEmpty(FilterText))
        {
            filteredList = UnfilteredTrades.Where(x => x.ItemName.Replace(" ", "").Contains(FilterText.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            filteredList = UnfilteredTrades;
        }
        Trades = new ObservableCollection<Trade>(filteredList.OrderByDescending(x => x.DateTime).Take(_settingsManager.UserSettings.TradesToShow));
    }
}
