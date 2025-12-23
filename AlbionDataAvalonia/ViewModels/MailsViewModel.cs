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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public partial class MailsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly MailService _mailService;
    private readonly CsvExportService _csvExportService;

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

    private ObservableCollection<AlbionMail> mails = new();
    public ObservableCollection<AlbionMail> Mails
    {
        get { return mails; }
        set { SetProperty(ref mails, value); }
    }

    private List<AlbionMail> UnfilteredMails { get; set; } = new();

    public List<string> Locations
    {
        get
        {
            var locations = Mails
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

    public MailsViewModel(SettingsManager settingsManager, PlayerState playerState, MailService mailService, CsvExportService csvExportService)
    {
        _settingsManager = settingsManager;
        _playerState = playerState;
        _mailService = mailService;
        _csvExportService = csvExportService;

        _mailService.OnMailAdded += HandleMailAdded;
        _mailService.OnMailDataAdded += HandleMailDataAdded;

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

    public void UpdateSelectedMails(IEnumerable<AlbionMail> selected)
    {
        var selectedMails = selected?.ToList() ?? new List<AlbionMail>();
        if (selectedMails.Count == 0)
        {
            HasSelectedRows = false;
            SelectedAmountTotal = 0;
            SelectedTotalSilver = 0;
            SelectedAverageSilver = 0;
            return;
        }

        long amountTotal = 0;
        decimal totalSilver = 0;
        foreach (var mail in selectedMails)
        {
            amountTotal += mail.PartialAmount;
            totalSilver += mail.TotalSilver;
        }

        HasSelectedRows = true;
        SelectedAmountTotal = amountTotal;
        SelectedTotalSilver = totalSilver;
        SelectedAverageSilver = amountTotal == 0 ? 0 : totalSilver / amountTotal;
    }

    public async Task ExportToCsvAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        IsExporting = true;
        ExportProgress = 0;
        try
        {
            var progress = new Progress<int>(p => ExportProgress = p);
            await _csvExportService.ExportMailsToCsvAsync(stream, progress, cancellationToken);
        }
        finally
        {
            IsExporting = false;
        }
    }
}
