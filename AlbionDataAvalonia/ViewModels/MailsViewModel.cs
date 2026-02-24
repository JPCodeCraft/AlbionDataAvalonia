using AlbionDataAvalonia.Locations;
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

public partial class MailsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly MailService _mailService;
    private readonly CsvExportService _csvExportService;
    private readonly TimeSpan _filterDebounceInterval = TimeSpan.FromMilliseconds(250);
    private IDisposable? _pendingFilterRefreshRegistration;
    private static readonly IReadOnlyList<NumericOption> _mailsToLoadOptions = NumericOptions.MailAndTradeLoadOptions;

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

    public IReadOnlyList<NumericOption> MailsToLoadOptions => _mailsToLoadOptions;

    public NumericOption SelectedMailsToLoad
    {
        get
        {
            var current = _settingsManager?.UserSettings.MailsPerPage ?? _mailsToLoadOptions[0].Value;
            return ResolveOption(_mailsToLoadOptions, current);
        }
        set
        {
            if (value is null || _settingsManager is null)
            {
                return;
            }

            if (_settingsManager.UserSettings.MailsPerPage != value.Value)
            {
                _settingsManager.UserSettings.MailsPerPage = value.Value;
                Task.Run(() => LoadMails());
            }
        }
    }

    partial void OnFilterTextChanged(string? oldValue, string newValue) => ScheduleFilterMails();
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
        _settingsManager.UserSettings.PropertyChanged += OnUserSettingsPropertyChanged;
        NormalizeMailsToLoadSetting();

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
            CancelPendingFilterRefresh();
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
        var normalizedFilterText = (FilterText ?? string.Empty).Replace(" ", string.Empty);
        if (!string.IsNullOrEmpty(normalizedFilterText))
        {
            filteredList = UnfilteredMails.Where(x => (x.ItemName ?? string.Empty)
                .Replace(" ", string.Empty)
                .Contains(normalizedFilterText, StringComparison.OrdinalIgnoreCase)).ToList();
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

    private void OnUserSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.MailsPerPage))
        {
            OnPropertyChanged(nameof(SelectedMailsToLoad));
        }
    }

    private void NormalizeMailsToLoadSetting()
    {
        var normalized = NormalizeOption(_mailsToLoadOptions, _settingsManager.UserSettings.MailsPerPage);
        if (_settingsManager.UserSettings.MailsPerPage != normalized)
        {
            _settingsManager.UserSettings.MailsPerPage = normalized;
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

    private void ScheduleFilterMails()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleFilterMails);
            return;
        }

        CancelPendingFilterRefresh();
        _pendingFilterRefreshRegistration = DispatcherTimer.RunOnce(() =>
        {
            _pendingFilterRefreshRegistration = null;
            FilterMails();
        }, _filterDebounceInterval);
    }

    private void CancelPendingFilterRefresh()
    {
        _pendingFilterRefreshRegistration?.Dispose();
        _pendingFilterRefreshRegistration = null;
    }
}
