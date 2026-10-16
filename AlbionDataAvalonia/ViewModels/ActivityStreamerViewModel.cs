using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Settings;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>Current-session totals for the streamer window, read on its own timer while the window is open.</summary>
public sealed partial class ActivityStreamerViewModel : ViewModelBase, IDisposable
{
    private const int ChartWindowSeconds = 5 * 60;
    private static readonly IBrush DarkBackground = new SolidColorBrush(Color.Parse("#262626"));
    private static readonly IBrush ChromaKeyBackground = new SolidColorBrush(Color.Parse("#00B140"));
    private readonly ActivityTrackerService? tracker;
    private readonly ActivityPersistence? persistence;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource cancellation = new();
    private long dataChangeVersion = 1;
    private long combatReadVersion;
    private ActivityQuery? combatQuery;
    private double combatSeconds;
    private int tickCount;
    private bool refreshing;
    private bool readFailed;
    private bool isDisposed;

    [ObservableProperty] private bool hasSession;
    [ObservableProperty] private string titleText = "Waiting for activity";
    [ObservableProperty] private string durationText = "";
    [ObservableProperty] private string sessionTimeText = "Waiting for activity";
    [ObservableProperty] private ActivitySummaryPresentation summary = ActivitySummaryPresentation.Empty;
    [ObservableProperty] private ActivityLiveTimelineSnapshot? timeline;

    public UserSettings Settings { get; }
    public ActivityTimelineChartMode ChartMode => (ActivityTimelineChartMode)Math.Clamp(Settings.StreamerChartModeIndex, 0, 2);
    public IBrush WindowBackground => Settings.StreamerChromaKey ? ChromaKeyBackground : DarkBackground;
    public string ScopeText => Settings.StreamerPartyTotals ? "Party" : "Personal";
    public string SilverBreakdownText => $"Silver {Summary.Silver} · Loot {Summary.LootValue} · Gathering {Summary.ResourceValueText}";
    public string KillsText => $"Mobs {Summary.MobKills} · PvP {Summary.PlayerKills}" +
        (string.IsNullOrEmpty(Summary.DeathsAndKnockdowns) ? "" : $" · {Summary.DeathsAndKnockdowns}");
    // Small shows one card of headline numbers; large adds the session name and breakdown lines.
    public bool IsSmall => Settings.StreamerSizeIndex == 0;
    public bool IsLarge => Settings.StreamerSizeIndex == 2;
    public bool ShowCards => !IsSmall;
    public double WindowWidth => Settings.StreamerSizeIndex switch { 0 => 250, 1 => 330, _ => 380 };
    public string SilverPerHourShortText => PerHourShort(Summary.Rates.SilverPerHour + Summary.Rates.LootValuePerHour + Summary.Rates.ResourceValuePerHour);
    public string CombatFamePerHourShortText => PerHourShort(ActivityProjection.GetFameRate(Summary.Summary.CombatFame, Summary.Rates));
    public string DpsShortText => Summary.IsImported ? "" : $"{Summary.Rates.DamagePerSecond:N0} DPS";
    public string GatheredPerHourShortText => PerHourShort(Summary.Rates.ResourceValuePerHour);
    public bool ShowChart => Settings.StreamerShowChart && HasSession && !IsSmall;
    public bool ShowNothingSelected => !Settings.StreamerShowSilver && !Settings.StreamerShowCombat &&
        !Settings.StreamerShowGathering && !Settings.StreamerShowChart;

    public ActivityStreamerViewModel() => Settings = new UserSettings();

    public ActivityStreamerViewModel(ActivityTrackerService tracker, ActivityPersistence persistence, UserSettings settings)
    {
        this.tracker = tracker;
        this.persistence = persistence;
        Settings = settings;
        refreshTimer.Tick += OnRefreshTick;
        persistence.DataChanged += OnDataChanged;
        settings.PropertyChanged += OnSettingsChanged;
    }

    public void Start()
    {
        if (isDisposed || tracker is null) return;
        refreshTimer.Start();
        _ = RefreshSafelyAsync();
    }

    partial void OnSummaryChanged(ActivitySummaryPresentation value)
    {
        OnPropertyChanged(nameof(SilverBreakdownText));
        OnPropertyChanged(nameof(KillsText));
        OnPropertyChanged(nameof(SilverPerHourShortText));
        OnPropertyChanged(nameof(CombatFamePerHourShortText));
        OnPropertyChanged(nameof(DpsShortText));
        OnPropertyChanged(nameof(GatheredPerHourShortText));
    }

    private string PerHourShort(double rate) => Summary.HasRecordedDuration ? $"{ActivityDisplay.Number(rate)} / h" : "";

    partial void OnHasSessionChanged(bool value) => OnPropertyChanged(nameof(ShowChart));

    // Combat time only changes when the journal commits, so it is reread after a commit.
    private void OnDataChanged(ActivityDataChange change) => Interlocked.Increment(ref dataChangeVersion);

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (isDisposed || args.PropertyName is not { } name || !name.StartsWith("Streamer", StringComparison.Ordinal)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (isDisposed) return;
            OnPropertyChanged(nameof(ChartMode));
            OnPropertyChanged(nameof(WindowBackground));
            OnPropertyChanged(nameof(ScopeText));
            OnPropertyChanged(nameof(ShowChart));
            OnPropertyChanged(nameof(ShowNothingSelected));
            OnPropertyChanged(nameof(IsSmall));
            OnPropertyChanged(nameof(IsLarge));
            OnPropertyChanged(nameof(ShowCards));
            OnPropertyChanged(nameof(WindowWidth));
            if (name is nameof(UserSettings.StreamerPartyTotals) or nameof(UserSettings.StreamerShowChart) or nameof(UserSettings.StreamerSizeIndex))
            {
                if (!ShowChart) Timeline = null;
                tickCount = 0;
                _ = RefreshSafelyAsync();
            }
        });
    }

    private async void OnRefreshTick(object? sender, EventArgs args)
    {
        if (App.IsShuttingDown) { refreshTimer.Stop(); return; }
        await RefreshSafelyAsync();
    }

    private async Task RefreshSafelyAsync()
    {
        if (isDisposed || refreshing) return;
        refreshing = true;
        try
        {
            await RefreshAsync(cancellation.Token);
            readFailed = false;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (isDisposed || App.IsShuttingDown) { }
        catch (Exception exception)
        {
            if (!readFailed) Log.Warning(exception, "Unable to refresh the streamer window");
            readFailed = true;
        }
        finally { refreshing = false; }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (tracker is null || persistence is null) return;
        var nowUtc = DateTime.UtcNow;
        var context = tracker.GetCurrentContext();
        if (context.Owner is not { } owner || context.Session is not { } live)
        {
            HasSession = false;
            TitleText = SessionTimeText = tracker.IsAwaitingContext ? "Waiting for character" : "Waiting for activity";
            DurationText = "";
            Summary = ActivitySummaryPresentation.Empty;
            Timeline = null;
            return;
        }

        var scope = Settings.StreamerPartyTotals ? ActivityScope.Party : ActivityScope.Personal;
        var query = new ActivityQuery { SessionId = live.Id, Scope = scope };
        var version = Interlocked.Read(ref dataChangeVersion);
        var sessionTask = persistence.GetSessionAsync(live.Id, cancellationToken);
        var combatTask = query == combatQuery && version == combatReadVersion
            ? Task.FromResult(combatSeconds) : persistence.GetCombatDurationSecondsAsync(query, cancellationToken);
        await Task.WhenAll(sessionTask, combatTask);
        if (isDisposed || tracker.CurrentSessionId != live.Id) return;
        combatQuery = query;
        combatReadVersion = version;
        combatSeconds = combatTask.Result;

        // The tracker owns live boundaries; the journal writer owns the saved totals.
        var current = sessionTask.Result is { } saved ? live with
        {
            PersonalSummaryJson = saved.PersonalSummaryJson,
            PartySummaryJson = saved.PartySummaryJson,
            ObservedSummaryJson = saved.ObservedSummaryJson
        } : live;
        var totals = ActivityProjection.GetSummary(current, scope);
        Summary = new(totals, ActivityProjection.GetRates(totals, current, combatSeconds, nowUtc), true);
        HasSession = true;
        TitleText = ActivityDisplay.SessionTitle(current);
        SessionTimeText = $"Session {ActivityDisplay.Duration(ActivityProjection.GetDurationSeconds(current, nowUtc))}";
        DurationText = $"{SessionTimeText} · Combat {ActivityDisplay.Duration(combatSeconds)}";

        // The chart scrolls between reads, so it is read every other tick.
        if (!ShowChart || tickCount++ % 2 != 0) return;
        var timelineQuery = new ActivityQuery
        {
            SessionId = live.Id,
            Server = owner.Server,
            CharacterId = string.IsNullOrWhiteSpace(owner.CharacterId) ? null : owner.CharacterId,
            CharacterName = owner.CharacterName,
            MatchCharacterNameExactly = true,
            Scope = scope,
            FromUtc = current.StartedAtUtc,
            ToUtc = nowUtc
        };
        var snapshot = await tracker.GetLiveTimelineAsync(timelineQuery, nowUtc, cancellationToken, ChartWindowSeconds);
        if (isDisposed || tracker.CurrentSessionId != live.Id || !ShowChart) return;
        Timeline = snapshot with { ContextKey = $"{snapshot.ContextKey}|streamer|{scope}" };
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        refreshTimer.Stop();
        refreshTimer.Tick -= OnRefreshTick;
        cancellation.Cancel();
        cancellation.Dispose();
        if (persistence is not null) persistence.DataChanged -= OnDataChanged;
        Settings.PropertyChanged -= OnSettingsChanged;
    }
}
