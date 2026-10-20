using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Ui.Services;
using AlbionDataAvalonia.Views;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>The Activity page's tabs, in the order the view shows them.</summary>
public enum ActivityTab { Current, History, Items }

/// <summary>The tabs of a session in History, in the order the view shows them.</summary>
public enum ActivityDetailTab { Encounters, Charts, Players, KilledMobs, Items, Timeline, Maps, Party }

/// <summary>
/// The Activity page: recording state, the toolbar and the refresh loop. Each tab has its own view model
/// (<see cref="Current"/>, <see cref="History"/>, <see cref="Items"/>), and all three share
/// <see cref="Filters"/> and <see cref="Export"/>. The page refreshes only the tab on view.
/// </summary>
public partial class ActivityViewModel : ViewModelBase, IDisposable
{
    private const string RecordingStatus = "Recording";
    private const string WaitingStatus = "Waiting for character";
    private const string PausedStatus = "Recording paused";
    private const string LoadError = "Could not load activity. Try reopening this tab.";
    private const string SaveError = "Activity could not be saved. Recording will resume when storage is available.";
    private readonly DispatcherTimer refreshTimer;
    private volatile bool isActive;
    private volatile bool isDisposed;
    private bool refreshing;
    private bool switchingTab;
    private int loadingCount;
    private int statusUpdatePending;
    private Guid? statusSessionId;
    private bool statusLegacyImportWarning;

    // The tab control binds the index; code reads the tab by name.
    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusRecording), nameof(IsStatusWaiting))]
    private string statusText = WaitingStatus;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingButtonText))]
    private bool isRecordingEnabled = true;
    [ObservableProperty] private bool canStartSession;

    public ActivityViewModel() : this(null, null, null, null, null, export: null) { }

    public ActivityViewModel(ActivityTrackerService tracker, ActivityPersistence persistence, CsvExportService csvExportService,
        ActivityItemValuationService? valuations = null, PartyTrackerService? partyTracker = null,
        ActivityStreamerWindowService? streamerWindow = null, ActivityMapWindowService? mapWindow = null,
        IDialogService? dialogs = null, IFileDialogService? files = null)
        : this(tracker, persistence, valuations, mapWindow, dialogs, new ActivityLootExport(persistence, csvExportService, files))
    {
        tracker.Changed += OnActivityChanged;
        persistence.StatusChanged += OnPersistenceStatusChanged;
        persistence.DataChanged += OnCommittedDataChanged;
        Current.AttachPartyTracker(partyTracker);
        AttachStreamerWindow(streamerWindow);
        AttachMapWindow(mapWindow);
        UpdateStatus();
    }

    private ActivityViewModel(ActivityTrackerService? tracker, ActivityPersistence? persistence, ActivityItemValuationService? valuations,
        ActivityMapWindowService? mapWindow, IDialogService? dialogs, ActivityLootExport? export)
    {
        Tracker = tracker;
        Persistence = persistence;
        Valuations = valuations;
        MapWindow = mapWindow;
        Dialogs = dialogs;
        Export = export ?? new(null, null, null);
        Current = new(this);
        History = new(this);
        Items = new(this);
        Filters.ScopeChanged += OnScopeChanged;
        Filters.CategoriesChanged += OnCategoriesChanged;
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refreshTimer.Tick += OnRefreshTick;
    }

    public ActivityFilterState Filters { get; } = new();
    public ActivityLootExport Export { get; }
    public ActivityCurrentViewModel Current { get; }
    public ActivityHistoryViewModel History { get; }
    public ActivityItemsViewModel Items { get; }

    internal ActivityTrackerService? Tracker { get; }
    internal ActivityPersistence? Persistence { get; }
    internal ActivityItemValuationService? Valuations { get; }
    internal ActivityMapWindowService? MapWindow { get; }
    internal IDialogService? Dialogs { get; }
    internal bool IsActive => isActive;
    internal bool IsDisposed => isDisposed;
    // Current's loot and the Items tab both read items, so saved item changes count once for both.
    internal long ItemChangeVersion { get; private set; } = 1;

    public ActivityTab SelectedTab
    {
        get => (ActivityTab)SelectedTabIndex;
        set => SelectedTabIndex = (int)value;
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool ShowActivityScope => SelectedTab is ActivityTab.Current or ActivityTab.History;
    public string RecordingButtonText => IsRecordingEnabled ? "Disable recording" : "Enable recording";
    public bool IsStatusRecording => StatusText == RecordingStatus;
    public bool IsStatusWaiting => StatusText == WaitingStatus;
    public bool HasLegacyImportWarning => Persistence?.LastLegacyImportResult?.Errors.Count > 0;
    public string LegacyImportWarningText => "Some gathering history could not be imported. Your original records are safe.";

    internal bool IsShowing(ActivityTab tab) => !isDisposed && isActive && SelectedTab == tab;

    internal void RefreshIfShowing(ActivityTab tab)
    {
        if (IsShowing(tab)) _ = RunSafelyAsync(RefreshVisibleAsync);
    }

    // Opening a session from Current or a chart switches tabs without a second refresh.
    internal void ShowTab(ActivityTab tab)
    {
        switchingTab = true;
        try { SelectedTab = tab; }
        finally { switchingTab = false; }
    }

    public void SetActive(bool active)
    {
        if (isDisposed || isActive == active) return;
        isActive = active;
        if (active)
        {
            UpdateStatus();
            History.ResumeReads();
            Items.ResumeReads();
            refreshTimer.Start();
            _ = RunSafelyAsync(RefreshVisibleAsync);
        }
        else
        {
            refreshTimer.Stop();
            Current.CancelReads();
            History.CancelReads();
            Items.CancelReads();
        }
    }

    private void OnActivityChanged(object? sender, EventArgs args)
    {
        if (isDisposed || !isActive || Interlocked.Exchange(ref statusUpdatePending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref statusUpdatePending, 0);
            if (isDisposed || !isActive) return;
            UpdateStatus();
        });
    }

    private void OnPersistenceStatusChanged(ActivityRecordingStatus status) => OnActivityChanged(Persistence, EventArgs.Empty);

    private void OnCommittedDataChanged(ActivityDataChange change) => Dispatcher.UIThread.Post(() =>
    {
        if (isDisposed) return;
        var changedIds = change.SessionIds.ToHashSet();
        if (change.ItemsChanged) ++ItemChangeVersion;
        History.OnDataChanged(change, changedIds);
        Current.OnDataChanged(changedIds);
    });

    private async void OnRefreshTick(object? sender, EventArgs args)
    {
        if (isDisposed || !isActive || refreshing || loadingCount > 0) return;
        UpdateStatus();
        Current.Tick(DateTime.UtcNow);
        var stale = SelectedTab switch
        {
            ActivityTab.History => History.Tick(),
            ActivityTab.Items => Items.IsStale,
            _ => true
        };
        if (stale) await RunSafelyAsync(RefreshVisibleAsync);
    }

    private void UpdateStatus()
    {
        if (Tracker is null || Persistence is null) return;
        IsRecordingEnabled = Tracker.IsRecordingEnabled;
        Current.HasCurrentPlaySession = Tracker.CurrentPlaySessionId is not null;
        CanStartSession = Tracker.CanStartSession;
        var status = Persistence.Status;
        StatusText = status.IsSuspended ? PausedStatus
            : !IsRecordingEnabled ? "Recording off" : Tracker.IsAwaitingContext ? WaitingStatus
            : Tracker.IsCaptureSuspended ? PausedStatus : RecordingStatus;
        if (status.Error is { Length: > 0 }) ErrorText = SaveError;
        if (statusSessionId != Tracker.CurrentSessionId)
        {
            statusSessionId = Tracker.CurrentSessionId;
            History.UpdateDetail();
        }
        if (statusLegacyImportWarning != HasLegacyImportWarning)
        {
            statusLegacyImportWarning = HasLegacyImportWarning;
            OnPropertyChanged(nameof(HasLegacyImportWarning));
        }
    }

    private async Task RefreshVisibleAsync()
    {
        if (Persistence is null || Tracker is null || refreshing || isDisposed) return;
        refreshing = true;
        try
        {
            await (SelectedTab switch
            {
                ActivityTab.Current => Current.RefreshAsync(),
                ActivityTab.History => History.RefreshAsync(),
                ActivityTab.Items => Items.RefreshAsync(),
                _ => Task.CompletedTask
            });
        }
        finally { refreshing = false; }
    }

    internal async Task RunSafelyAsync(Func<Task> action)
    {
        if (isDisposed) return;
        loadingCount++;
        try
        {
            await action();
            // Chart and replay reads handle their own failures and recovery. A successful
            // surrounding refresh must not immediately dismiss their warning.
            if (Persistence?.Status.Error is not { Length: > 0 } && ErrorText is LoadError or SaveError)
                ErrorText = "";
        }
        catch (OperationCanceledException) { /* Superseded view requests are expected. */ }
        catch (Exception exception)
        {
            Log.Warning(exception, "Unable to load activity history");
            if (!isDisposed) ErrorText = LoadError;
        }
        finally { loadingCount--; }
    }

    private void OnScopeChanged()
    {
        ++ItemChangeVersion;
        Current.OnScopeChanged();
        History.OnScopeChanged(Filters.SelectedScope);
        Items.OnScopeChanged(Filters.SelectedScope);
        if (isActive) _ = RunSafelyAsync(RefreshVisibleAsync);
    }

    private void OnCategoriesChanged()
    {
        Current.OnCategoriesChanged();
        History.OnCategoriesChanged();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        var tab = (ActivityTab)value;
        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(ShowActivityScope));
        if (tab != ActivityTab.Current) Current.CancelReads();
        if (tab != ActivityTab.History) History.CancelReads();
        else History.ResumeReads();
        if (tab != ActivityTab.Items) Items.CancelReads();
        if (isActive && !switchingTab) _ = RunSafelyAsync(RefreshVisibleAsync);
    }

    [RelayCommand] private void ToggleRecording() { Tracker?.SetRecordingEnabled(!IsRecordingEnabled); UpdateStatus(); }

    [RelayCommand]
    private void StartSession()
    {
        if (Tracker is null || !Tracker.CanStartSession) return;
        Tracker.StartSession();
        UpdateStatus();
        Current.ResetPlaySession();
        RefreshIfShowing(ActivityTab.Current);
    }

    [RelayCommand]
    private async Task EndSession()
    {
        if (Dialogs is null || !Current.HasCurrentPlaySession) return;
        var id = Tracker?.CurrentPlaySessionId;
        if (await Dialogs.ConfirmAsync("End this session?", "It ends now and stays in History. A new one starts on your next map or activity outside town, or when you press Start session.", "End session", destructive: false)
            && Tracker?.CurrentPlaySessionId == id)
            EndCurrentSession(id);
    }

    public void EndCurrentSession(Guid? expectedPlaySessionId)
    {
        Tracker?.EndSession(expectedPlaySessionId);
        UpdateStatus();
        Current.ResetPlaySession();
        RefreshIfShowing(ActivityTab.Current);
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        Export.Dispose();
        Current.Dispose();
        History.Dispose();
        Items.Dispose();
        refreshTimer.Stop();
        refreshTimer.Tick -= OnRefreshTick;
        Filters.ScopeChanged -= OnScopeChanged;
        Filters.CategoriesChanged -= OnCategoriesChanged;
        if (Tracker is not null) Tracker.Changed -= OnActivityChanged;
        DisposeStreamerWindow();
        DisposeMapWindow();
        if (Persistence is not null)
        {
            Persistence.StatusChanged -= OnPersistenceStatusChanged;
            Persistence.DataChanged -= OnCommittedDataChanged;
        }
    }
}
