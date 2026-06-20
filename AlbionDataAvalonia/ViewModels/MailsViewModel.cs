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

public partial class MailsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly PlayerState _playerState;
    private readonly MailService _mailService;
    private readonly CsvExportService _csvExportService;
    private readonly TimeSpan _filterDebounceInterval = TimeSpan.FromMilliseconds(250);
    private readonly TimeSpan _loadDebounceInterval = TimeSpan.FromMilliseconds(100);
    private IDisposable? _pendingFilterRefreshRegistration;
    private IDisposable? _pendingLoadMailsRegistration;
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
    private string cleanupStatus = string.Empty;

    [ObservableProperty]
    private bool hasCleanupStatus;

    [ObservableProperty]
    private long selectedAmountTotal;

    [ObservableProperty]
    private decimal selectedTotalSilver;

    [ObservableProperty]
    private decimal selectedAverageSilver;

    private ObservableCollection<MailRowViewModel> mails = new();
    public ObservableCollection<MailRowViewModel> Mails
    {
        get { return mails; }
        set { SetProperty(ref mails, value); }
    }

    private List<AlbionMail> UnfilteredMails { get; set; } = new();

    private List<string> _locations = ["Any"];
    public List<string> Locations => _locations;

    private async Task RefreshLocationOptionsAsync()
    {
        try
        {
            AlbionServers.TryParse(GetSelectedServer(), out AlbionServer? server);
            var locationIds = await _mailService.GetDistinctLocationIds(server?.Id);

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
                ScheduleLoadMails();
            }
        }
    }

    partial void OnFilterTextChanged(string? oldValue, string newValue) => ScheduleFilterMails();
    partial void OnSelectedLocationChanged(string? oldValue, string newValue) => QueueLoadMailsForSelectionChange(oldValue, newValue);
    partial void OnSelectedTypeChanged(string? oldValue, string newValue) => QueueLoadMailsForSelectionChange(oldValue, newValue);
    partial void OnSelectedServerChanged(string? oldValue, string newValue)
    {
        QueueLoadMailsForSelectionChange(oldValue, newValue);

        // Server scope determines which locations exist; refresh the dropdown for the new server.
        if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            _ = RefreshLocationOptionsAsync();
        }
    }

    public MailsViewModel()
    {
    }

    public MailsViewModel(
        SettingsManager settingsManager,
        PlayerState playerState,
        MailService mailService,
        CsvExportService csvExportService)
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
            }
        };
    }

    private bool _hasLoadedInitialMails;

    public void EnsureLoaded()
    {
        if (_hasLoadedInitialMails)
        {
            return;
        }

        _hasLoadedInitialMails = true;
        ScheduleLoadMails();
        _ = RefreshLocationOptionsAsync();
    }

    [RelayCommand]
    public async Task LoadMails()
    {
        try
        {
            var filter = GetCurrentMailFilter();

            var loadedMails = await _mailService.GetMails(_settingsManager.UserSettings.MailsPerPage, 0, filter.AlbionServerId, false, filter.LocationId, filter.AuctionType);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UnfilteredMails = loadedMails;
                CancelPendingFilterRefresh();
                FilterMails();
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load mails");
        }
    }

    private void HandleMailAdded(List<AlbionMail> mails)
    {
        ScheduleLoadMails();

        // A mail at a location not yet in the dropdown should make it appear there.
        var hasNewLocation = mails
            .Select(m => m.Location?.MarketLocation?.FriendlyName ?? m.Location?.FriendlyName)
            .Any(name => !string.IsNullOrEmpty(name) && !_locations.Contains(name));
        if (hasNewLocation)
        {
            _ = RefreshLocationOptionsAsync();
        }
    }

    private void HandleMailDataAdded(AlbionMail mail)
    {
        ScheduleLoadMails();
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
        var rows = filteredList
            .OrderByDescending(x => x.Received)
            .Take(_settingsManager.UserSettings.MailsPerPage)
            .Select(mail => new MailRowViewModel(mail))
            .ToList();

        Mails = new ObservableCollection<MailRowViewModel>(rows);
    }

    private void QueueLoadMailsForSelectionChange(string? oldValue, string? newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue) || string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        ScheduleLoadMails();
    }

    private void ScheduleLoadMails()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ScheduleLoadMails);
            return;
        }

        _pendingLoadMailsRegistration?.Dispose();
        _pendingLoadMailsRegistration = DispatcherTimer.RunOnce(() =>
        {
            _pendingLoadMailsRegistration = null;
            _ = LoadMails();
        }, _loadDebounceInterval);
    }

    private string GetSelectedLocation()
    {
        return string.IsNullOrWhiteSpace(SelectedLocation) ? "Any" : SelectedLocation;
    }

    private string GetSelectedType()
    {
        return string.IsNullOrWhiteSpace(SelectedType) ? "Any" : SelectedType;
    }

    private string GetSelectedServer()
    {
        return string.IsNullOrWhiteSpace(SelectedServer) ? "Any" : SelectedServer;
    }

    private CurrentMailFilter GetCurrentMailFilter()
    {
        var location = AlbionLocations.Get(GetSelectedLocation());
        AlbionServers.TryParse(GetSelectedServer(), out AlbionServer? server);
        var selectedType = GetSelectedType();
        AuctionType? auctionType = selectedType == "Sold"
            ? AuctionType.offer
            : selectedType == "Bought"
                ? AuctionType.request
                : null;

        return new CurrentMailFilter(server?.Id, location?.MarketLocation?.IdInt ?? location?.IdInt, auctionType);
    }

    private readonly record struct CurrentMailFilter(int? AlbionServerId, int? LocationId, AuctionType? AuctionType);

    public void UpdateSelectedMails(IEnumerable<MailRowViewModel> selected)
    {
        var selectedMails = selected?.ToList() ?? new List<MailRowViewModel>();
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

    public async Task ExportToCsvAsync(Stream stream, CsvExportOptions options, CancellationToken cancellationToken = default)
    {
        IsExporting = true;
        ExportProgress = 0;
        try
        {
            var progress = new Progress<int>(p => ExportProgress = p);
            await _csvExportService.ExportMailsToCsvAsync(stream, options, progress, cancellationToken);
        }
        finally
        {
            IsExporting = false;
        }
    }

    public async Task<CleanupPreview> GetCleanupPreviewAsync(CancellationToken cancellationToken = default)
    {
        return await _mailService.GetCleanupPreviewAsync(cancellationToken);
    }

    public async Task<int> CleanupMailsAsync(CleanupCountOption option, CancellationToken cancellationToken = default)
    {
        SetCleanupStatus("Cleaning up mails...");
        var deletedCount = await _mailService.CleanupMailsOlderThanAsync(option.CutoffUtc, cancellationToken);
        await LoadMails();
        await RefreshLocationOptionsAsync();
        SetCleanupStatus($"Cleanup: {deletedCount:N0} mail{(deletedCount == 1 ? string.Empty : "s")} deleted.");
        return deletedCount;
    }

    public async Task<int> DeleteSelectedMailsAsync(
        IEnumerable<MailRowViewModel> selected,
        CancellationToken cancellationToken = default)
    {
        var selectedMails = selected?.ToList() ?? new List<MailRowViewModel>();
        if (selectedMails.Count == 0)
        {
            return 0;
        }

        SetCleanupStatus("Deleting selected mails...");
        var deletedCount = await _mailService.DeleteMailsAsync(
            selectedMails.Select(row => row.Source.Id),
            cancellationToken);

        await LoadMails();
        await RefreshLocationOptionsAsync();
        UpdateSelectedMails([]);
        SetCleanupStatus($"Delete: {deletedCount:N0} mail{(deletedCount == 1 ? string.Empty : "s")} deleted.");
        return deletedCount;
    }

    private void SetCleanupStatus(string status)
    {
        CleanupStatus = status;
        HasCleanupStatus = !string.IsNullOrWhiteSpace(status);
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

public sealed class MailRowViewModel : ObservableObject
{
    public MailRowViewModel(AlbionMail mail)
    {
        Source = mail;
    }

    public AlbionMail Source { get; }
    public AlbionServer? Server => Source.Server;
    public string PlayerName => Source.PlayerName;
    public DateTime Received => Source.Received;
    public string AuctionTypeFormatted => Source.AuctionTypeFormatted;
    public string ItemName => Source.ItemName;
    public AlbionLocation? Location => Source.Location;
    public int PartialAmount => Source.PartialAmount;
    public int TotalAmount => Source.TotalAmount;
    public string ItemId => Source.ItemId;
    public int ImageQuality => 1;
    public double UnitSilver => Source.UnitSilver;
    public long TotalSilver => Source.TotalSilver;
}
