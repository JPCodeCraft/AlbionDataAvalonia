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

public partial class MailsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly MailService _mailService;

    [ObservableProperty]
    private string filterText = string.Empty;

    private ObservableCollection<AlbionMail> mails = new();
    public ObservableCollection<AlbionMail> Mails
    {
        get { return mails; }
        set { SetProperty(ref mails, value); }
    }

    private List<AlbionMail> UnfilteredMails { get; set; } = new();

    public List<string> Locations { get; set; } = new();
    [ObservableProperty]
    private string selectedLocation = "Any";

    public List<string> AuctionTypes { get; set; } = new() { "Any", "Bought", "Sold" };
    [ObservableProperty]
    private string selectedType = "Any";

    public List<string> Servers { get; set; } = new();
    [ObservableProperty]
    private string selectedServer = "Any";

    partial void OnFilterTextChanged(string? oldValue, string newValue) => FilterMails();
    partial void OnSelectedLocationChanged(string? oldValue, string newValue) => Task.Run(() => LoadMails());
    partial void OnSelectedTypeChanged(string? oldValue, string newValue) => Task.Run(() => LoadMails());
    partial void OnSelectedServerChanged(string? oldValue, string newValue) => Task.Run(() => LoadMails());

    public MailsViewModel()
    {
    }

    public MailsViewModel(SettingsManager settingsManager, PlayerState playerState, MailService mailService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        _mailService = mailService;

        _mailService.OnMailAdded += HandleMailAdded;
        _mailService.OnMailDataAdded += HandleMailDataAdded;

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
                Task.Run(() => LoadMails());
            }
        };
    }

    [RelayCommand]
    public async Task LoadMails()
    {
        try
        {
            var location = AlbionLocations.Get(SelectedLocation);
            AlbionServers.TryParse(SelectedServer, out AlbionServer? server);
            AuctionType? type = SelectedType == "Sold" ? AuctionType.offer : SelectedType == "Bought" ? AuctionType.request : null;

            UnfilteredMails = await _mailService.GetMails(_settingsManager.UserSettings.MailsPerPage, 0, server?.Id ?? null, false, location?.IdInt ?? null, type);
            FilterMails();
        }
        catch
        {
            Log.Error("Failed to load mails");
        }
    }

    private async void HandleMailAdded(List<AlbionMail> mails)
    {
        await LoadMails();
    }

    private async void HandleMailDataAdded(AlbionMail mail)
    {
        await LoadMails();
    }

    private void FilterMails()
    {
        List<AlbionMail> filteredList;
        if (!string.IsNullOrEmpty(FilterText))
        {
            filteredList = UnfilteredMails.Where(x => x.ItemName.Replace(" ", "").Contains(FilterText.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            filteredList = UnfilteredMails;
        }
        Mails = new ObservableCollection<AlbionMail>(filteredList.OrderByDescending(x => x.Received).Take(_settingsManager.UserSettings.MailsPerPage));
    }
}
