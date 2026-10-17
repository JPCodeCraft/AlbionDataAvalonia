using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.ViewModels;
using AlbionDataAvalonia.Views.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AlbionDataAvalonia.Views;

public sealed record ActivityTimelineReplayRequest(string ContextKey, DateTime WindowStartUtc, DateTime WindowEndUtc);

/// <summary>A presentation-only rolling view with event tooltips and optional history replay.</summary>
public sealed class ActivityLiveTimelineView : UserControl
{
    public static readonly StyledProperty<ActivityLiveTimelineSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, ActivityLiveTimelineSnapshot?>(nameof(Snapshot));
    public static readonly StyledProperty<ICommand?> OpenEncounterCommandProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, ICommand?>(nameof(OpenEncounterCommand));
    public static readonly StyledProperty<ActivityTimelineChartMode> ChartModeProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, ActivityTimelineChartMode>(nameof(ChartMode));
    public static readonly StyledProperty<int> WindowMinutesProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, int>(nameof(WindowMinutes), 5);
    public static readonly StyledProperty<bool> IsRollingProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, bool>(nameof(IsRolling), true);
    public static readonly StyledProperty<bool> ShowWindowSelectorProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, bool>(nameof(ShowWindowSelector), true);
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, string>(nameof(Title), "");
    public static readonly StyledProperty<bool> ShowReplayControlsProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, bool>(nameof(ShowReplayControls));
    public static readonly StyledProperty<ActivityLiveTimelineSnapshot?> ReplaySnapshotProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, ActivityLiveTimelineSnapshot?>(nameof(ReplaySnapshot));
    public static readonly StyledProperty<ICommand?> ReplayWindowCommandProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, ICommand?>(nameof(ReplayWindowCommand));
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, bool>(nameof(IsCompact));
    private readonly ActivityLiveTimelineCanvas canvas = new();
    private readonly Canvas tooltipLayer = new() { IsHitTestVisible = false, ClipToBounds = false };
    private readonly Border tooltipPanel = new()
    {
        IsVisible = false,
        IsHitTestVisible = false,
        Padding = new Thickness(10, 8),
        CornerRadius = new CornerRadius(4),
        Background = new SolidColorBrush(Color.Parse("#262626")),
        BorderBrush = new SolidColorBrush(Color.Parse("#626262")),
        BorderThickness = new Thickness(1)
    };
    private readonly SegmentedSelector mode = new()
    {
        ItemsSource = new[] { new ActivityOption<ActivityTimelineChartMode>(ActivityTimelineChartMode.Combat, "Combat"),
            new(ActivityTimelineChartMode.Gathering, "Gathering"), new(ActivityTimelineChartMode.Fame, "Fame") },
        SelectedIndex = 0
    };
    private readonly SegmentedSelector direction = new()
    {
        ItemsSource = new[] { new ActivityOption<bool>(false, "Dealt"), new ActivityOption<bool>(true, "Received") },
        SelectedIndex = 0
    };
    private readonly ComboBox window = new()
    {
        Name = "ActivityTimelineWindow",
        ItemsSource = new[] { new ActivityOption<int>(5, "5 min"), new ActivityOption<int>(15, "15 min"),
            new ActivityOption<int>(30, "30 min"), new ActivityOption<int>(60, "1 h"), new ActivityOption<int>(120, "2 h") },
        SelectedIndex = 0,
        Width = 108,
        Height = 32,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top
    };
    private readonly TextBlock titleText = new() { FontSize = 18, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock stateText = new()
    {
        FontSize = 12,
        Opacity = 0.8,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 200,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly WrapPanel legends = new();
    private readonly DispatcherTimer renderTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch sinceSnapshot = new();
    private readonly Stopwatch replayClock = new();
    private readonly Stopwatch sinceReplayRequest = new();
    private readonly Button playReplay = ReplayButton("Replay", "ActivityReplayPlay");
    private readonly Button overviewReplay = ReplayButton("Overview", "ActivityReplayOverview");
    private readonly Button restartReplay = ReplayButton("Restart", "ActivityReplayRestart");
    private readonly ComboBox replaySpeedSelector = new()
    {
        Name = "ActivityReplaySpeed",
        ItemsSource = new[] { new ActivityOption<double>(1, "1×"), new ActivityOption<double>(2, "2×"), new ActivityOption<double>(4, "4×"), new ActivityOption<double>(8, "8×") },
        SelectedIndex = 0,
        Width = 74,
        Height = 32,
        Margin = new Thickness(6, 0, 0, 0)
    };
    private readonly Slider replaySeek = new() { Name = "ActivityReplaySeek", Minimum = 0, Maximum = 1, IsEnabled = false, MinWidth = 120 };
    private readonly TextBlock replayPosition = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 105, TextAlignment = TextAlignment.Right };
    private readonly Grid replaySeekRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
    private readonly StackPanel replayControls = new() { Spacing = 5, IsVisible = false };
    private readonly Grid heading = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
    private readonly Border frame = new()
    {
        Background = new SolidColorBrush(Color.Parse("#383838")),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12)
    };
    private ActivityLiveTimelineSnapshot? latest;
    private ActivityLiveTimelineSnapshot? replayFrame;
    private ActivityTimelineReplayRequest? lastReplayRequest;
    private DateTime replayCursorUtc;
    private double replaySpeed = 1;
    private bool replayMode;
    private bool replayPlaying;
    private bool replayScrubbing;
    private bool updatingReplayPosition;

    public ActivityLiveTimelineSnapshot? Snapshot { get => GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public ICommand? OpenEncounterCommand { get => GetValue(OpenEncounterCommandProperty); set => SetValue(OpenEncounterCommandProperty, value); }
    public ActivityTimelineChartMode ChartMode { get => GetValue(ChartModeProperty); set => SetValue(ChartModeProperty, value); }
    public int WindowMinutes { get => GetValue(WindowMinutesProperty); set => SetValue(WindowMinutesProperty, value); }
    public bool IsRolling { get => GetValue(IsRollingProperty); set => SetValue(IsRollingProperty, value); }
    public bool ShowWindowSelector { get => GetValue(ShowWindowSelectorProperty); set => SetValue(ShowWindowSelectorProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool ShowReplayControls { get => GetValue(ShowReplayControlsProperty); set => SetValue(ShowReplayControlsProperty, value); }
    public ActivityLiveTimelineSnapshot? ReplaySnapshot { get => GetValue(ReplaySnapshotProperty); set => SetValue(ReplaySnapshotProperty, value); }
    public ICommand? ReplayWindowCommand { get => GetValue(ReplayWindowCommandProperty); set => SetValue(ReplayWindowCommandProperty, value); }
    // Compact charts drop the heading controls, as in the streamer window. The plot keeps its fixed geometry.
    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }

    public ActivityLiveTimelineView()
    {
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is ActivityOption<ActivityTimelineChartMode> option) SetCurrentValue(ChartModeProperty, option.Value);
        };
        direction.SelectionChanged += (_, _) =>
        {
            canvas.ShowReceived = direction.SelectedIndex == 1;
            UpdateMode();
        };
        window.SelectionChanged += (_, _) =>
        {
            if (window.SelectedItem is ActivityOption<int> option) SetCurrentValue(WindowMinutesProperty, option.Value);
        };
        ToolTip.SetTip(window, "Chart time range");
        playReplay.Click += (_, _) => ToggleReplay();
        overviewReplay.Click += (_, _) => ResetReplay();
        restartReplay.Click += (_, _) => StartReplay();
        replaySpeedSelector.SelectionChanged += (_, _) =>
        {
            HoldReplayCursor();
            replaySpeed = (replaySpeedSelector.SelectedItem as ActivityOption<double>)?.Value ?? 1;
            UpdateReplayClock();
        };
        ToolTip.SetTip(replaySpeedSelector, "Playback speed");
        replaySeek.PropertyChanged += (_, change) =>
        {
            if (change.Property != Slider.ValueProperty || updatingReplayPosition || !replayMode || latest == null) return;
            SetReplayCursor(latest.WindowStartUtc.AddSeconds(replaySeek.Value));
            RequestReplayWindow(false);
            DrawLatest();
            UpdateState();
        };
        replaySeek.AddHandler(PointerPressedEvent, (_, _) =>
        {
            HoldReplayCursor();
            replayScrubbing = true;
            UpdateReplayClock();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        replaySeek.AddHandler(PointerReleasedEvent, (_, _) => FinishSeeking(), RoutingStrategies.Bubble, handledEventsToo: true);
        replaySeek.PointerCaptureLost += (_, _) => FinishSeeking();
        replaySeek.KeyUp += (_, _) => RequestReplayWindow(true);
        var replayActions = new WrapPanel { Orientation = Orientation.Horizontal };
        replayActions.Children.Add(playReplay);
        replayActions.Children.Add(overviewReplay);
        replayActions.Children.Add(restartReplay);
        replayActions.Children.Add(replaySpeedSelector);
        replaySeekRow.Children.Add(replaySeek);
        replaySeekRow.Children.Add(replayPosition);
        Grid.SetColumn(replayPosition, 1);
        replayControls.Children.Add(replayActions);
        replayControls.Children.Add(replaySeekRow);
        var headingContent = new WrapPanel { Orientation = Orientation.Horizontal };
        headingContent.Children.Add(new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(0, 0, 24, 8),
            Width = 160,
            Children = { titleText, stateText }
        });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        mode.Margin = new Thickness(0, 0, 8, 8);
        direction.Margin = new Thickness(0, 0, 12, 8);
        window.Margin = new Thickness(12, 0, 0, 8);
        actions.Children.Add(mode);
        actions.Children.Add(direction);
        headingContent.Children.Add(actions);
        heading.Children.Add(headingContent);
        heading.Children.Add(window);
        Grid.SetColumn(window, 1);

        // The tooltip shares the chart's host rather than its clipped drawing surface. Keeping
        // the entire overlay out of hit testing lets pointer movement continue through it.
        tooltipLayer.Children.Add(tooltipPanel);
        var chartHost = new Grid { ClipToBounds = false, Children = { canvas, tooltipLayer } };
        frame.Child = new StackPanel { Spacing = 6, Children = { heading, replayControls, legends, chartHost } };
        Content = frame;
        canvas.InspectionChanged += ShowTooltip;
        canvas.EncounterSelected += id => { if (OpenEncounterCommand?.CanExecute(id) == true) OpenEncounterCommand.Execute(id); };
        renderTimer.Tick += (_, _) => RenderTick();
        AttachedToVisualTree += (_, _) => { renderTimer.Start(); UpdateReplayClock(); };
        DetachedFromVisualTree += (_, _) =>
        {
            HoldReplayCursor();
            replayClock.Stop();
            renderTimer.Stop();
            canvas.ClearInspection();
        };
        UpdateMode();
    }

    private void RenderTick()
    {
        UpdateReplayClock();
        if (!IsEffectivelyVisible) return;
        if (replayMode)
        {
            if (replayPlaying && ReplayCursorUtc() >= latest!.WindowEndUtc)
            {
                HoldReplayCursor();
                replayPlaying = false;
                UpdateReplayClock();
                RequestReplayWindow(true);
            }
            else RequestReplayWindow(false);
            DrawLatest();
            UpdateState();
        }
        else if (IsRolling) DrawLatest();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChartModeProperty) { UpdateMode(); return; }
        if (change.Property == IsCompactProperty)
        {
            heading.IsVisible = !IsCompact;
            frame.Padding = new Thickness(IsCompact ? 8 : 12);
            return;
        }
        if (change.Property == ReplaySnapshotProperty)
        {
            var value = ReplaySnapshot;
            if (!replayMode || value == null || lastReplayRequest == null ||
                value.ContextKey != lastReplayRequest.ContextKey || value.WindowStartUtc != lastReplayRequest.WindowStartUtc ||
                value.WindowEndUtc != lastReplayRequest.WindowEndUtc || value.WindowEndUtc > ReplayCursorUtc()) return;
            replayFrame = value;
            DrawLatest();
            return;
        }
        if (change.Property == ReplayWindowCommandProperty) { RequestReplayWindow(true); UpdateState(); return; }
        if (change.Property == ShowReplayControlsProperty)
        {
            if (!ShowReplayControls) ResetReplay();
            UpdateState();
            return;
        }
        if (change.Property == WindowMinutesProperty || change.Property == IsRollingProperty ||
            change.Property == ShowWindowSelectorProperty || change.Property == TitleProperty)
        {
            HoldReplayCursor();
            window.SelectedIndex = WindowMinutes switch { 15 => 1, 30 => 2, 60 => 3, 120 => 4, _ => 0 };
            if (IsRolling && replayMode) ResetReplay();
            RefreshDisplay();
            if (change.Property == WindowMinutesProperty) RequestReplayWindow(true);
            return;
        }
        if (change.Property != SnapshotProperty) return;
        var snapshot = Snapshot;
        var contextChanged = snapshot?.ContextKey != latest?.ContextKey || snapshot?.HasContext != latest?.HasContext ||
            snapshot?.SessionId != latest?.SessionId || snapshot?.Scope != latest?.Scope || snapshot?.Categories != latest?.Categories;
        latest = snapshot;
        sinceSnapshot.Restart();
        if (contextChanged)
        {
            canvas.ClearInspection();
            ResetReplay();
        }
        else if (replayMode) SetReplayCursor(ReplayCursorUtc());
        DrawLatest();
        UpdateState();
    }

    private void UpdateMode()
    {
        canvas.ChartMode = ChartMode;
        mode.SelectedIndex = (int)ChartMode;
        direction.IsVisible = ChartMode == ActivityTimelineChartMode.Combat;
        legends.Children.Clear();
        foreach (var series in canvas.VisibleSeries)
            legends.Children.Add(Legend(series.Label + (ChartMode == ActivityTimelineChartMode.Gathering ? " / sec" : ""), series.Color));
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        canvas.ClearInspection();
        UpdateReplayClock();
        DrawLatest();
        UpdateState();
    }

    private bool CanReplay => ShowReplayControls && !IsRolling && latest?.HasContext == true &&
        latest.WindowEndUtc > latest.WindowStartUtc && ReplayWindowCommand != null &&
        (latest.Markers.Count > 0 || latest.Encounters.Count > 0 || latest.Samples.Any(sample =>
            sample.DamageDealt > 0 || sample.DamageReceived > 0 || sample.HealingDone > 0 || sample.HealingReceived > 0 ||
            sample.GatheringQuantity > 0 || sample.FishingQuantity > 0 || sample.PveFame > 0 || sample.PvpFame > 0 ||
            sample.GatheringFame > 0 || sample.FishingFame > 0 || sample.UnknownFame > 0));

    private void ToggleReplay()
    {
        if (!CanReplay) return;
        if (!replayMode || ReplayCursorUtc() >= latest!.WindowEndUtc) { StartReplay(); return; }
        HoldReplayCursor();
        replayPlaying = !replayPlaying;
        UpdateReplayClock();
        RequestReplayWindow(true);
        DrawLatest();
        UpdateState();
    }

    private void StartReplay()
    {
        if (!CanReplay) return;
        replayClock.Reset();
        replayMode = true;
        replayPlaying = true;
        replayScrubbing = false;
        replayCursorUtc = latest!.WindowStartUtc;
        replayFrame = null;
        lastReplayRequest = null;
        sinceReplayRequest.Reset();
        canvas.ClearInspection();
        UpdateReplayClock();
        RequestReplayWindow(true);
        DrawLatest();
        UpdateState();
    }

    private void ResetReplay()
    {
        replayClock.Reset();
        replayMode = false;
        replayPlaying = false;
        replayScrubbing = false;
        replayFrame = null;
        lastReplayRequest = null;
        sinceReplayRequest.Reset();
        replayCursorUtc = latest?.WindowStartUtc ?? default;
        canvas.ClearInspection();
        DrawLatest();
        UpdateState();
    }

    private DateTime ReplayCursorUtc()
    {
        if (latest == null) return replayCursorUtc;
        var remaining = Math.Max(0, latest.WindowEndUtc.Ticks - replayCursorUtc.Ticks);
        var elapsed = Math.Min(remaining, replayClock.Elapsed.Ticks * replaySpeed);
        return new DateTime(Math.Clamp(replayCursorUtc.Ticks + (long)elapsed,
            latest.WindowStartUtc.Ticks, latest.WindowEndUtc.Ticks), DateTimeKind.Utc);
    }

    private void HoldReplayCursor()
    {
        if (!replayMode) return;
        replayCursorUtc = ReplayCursorUtc();
        replayClock.Reset();
    }

    private void SetReplayCursor(DateTime value)
    {
        if (latest == null) return;
        replayClock.Reset();
        replayCursorUtc = new DateTime(Math.Clamp(value.Ticks, latest.WindowStartUtc.Ticks, latest.WindowEndUtc.Ticks), DateTimeKind.Utc);
        // A grouped marker can include several later rewards. A backwards seek must wait for
        // evidence bounded to the new cursor rather than display amounts from that future group.
        if (replayFrame?.WindowEndUtc > replayCursorUtc) replayFrame = null;
        UpdateReplayClock();
    }

    private void UpdateReplayClock()
    {
        var shouldRun = replayMode && replayPlaying && !replayScrubbing &&
            IsEffectivelyVisible && renderTimer.IsEnabled;
        if (shouldRun && !replayClock.IsRunning) replayClock.Start();
        else if (!shouldRun && replayClock.IsRunning) HoldReplayCursor();
    }

    private void RequestReplayWindow(bool force)
    {
        if (!replayMode || latest == null || !CanReplay || (!force && sinceReplayRequest.IsRunning && sinceReplayRequest.Elapsed.TotalSeconds < 1)) return;
        var end = new DateTime(ReplayCursorUtc().Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var start = new DateTime(Math.Max(DateTime.MinValue.Ticks, end.Ticks - ReplayWindowMinutes * TimeSpan.TicksPerMinute), DateTimeKind.Utc);
        var request = new ActivityTimelineReplayRequest(latest.ContextKey, start, end);
        if (!force && request == lastReplayRequest) return;
        if (ReplayWindowCommand?.CanExecute(request) != true) return;
        lastReplayRequest = request;
        sinceReplayRequest.Restart();
        ReplayWindowCommand.Execute(request);
    }

    private int ReplayWindowMinutes => WindowMinutes is 15 or 30 or 60 or 120 ? WindowMinutes : 5;

    private void FinishSeeking()
    {
        if (!replayScrubbing) return;
        replayScrubbing = false;
        UpdateReplayClock();
        RequestReplayWindow(true);
    }

    private ActivityLiveTimelineSnapshot? ReplayDisplay()
    {
        if (latest == null) return null;
        var cursor = ReplayCursorUtc();
        var start = new DateTime(Math.Max(DateTime.MinValue.Ticks, cursor.Ticks - ReplayWindowMinutes * TimeSpan.TicksPerMinute), DateTimeKind.Utc);
        return replayFrame is { } frame ? frame with
        {
            WindowStartUtc = start,
            WindowEndUtc = cursor,
            Encounters = frame.Encounters.Where(encounter => encounter.StartedAtUtc < frame.WindowEndUtc)
                .Select(encounter => encounter.LastCombatAtUtc <= frame.WindowEndUtc ? encounter :
                    encounter with { LastCombatAtUtc = frame.WindowEndUtc, State = ActivityState.Open }).ToArray()
        } :
            latest with { WindowStartUtc = start, WindowEndUtc = cursor, Samples = [], Markers = [], Encounters = [], Gaps = [] };
    }

    private DateTime LiveEndUtc()
    {
        if (latest is null) return DateTime.UtcNow;
        if (!IsRolling) return latest.WindowEndUtc;
        return new DateTime(Math.Min(DateTime.MaxValue.Ticks,
            latest.WindowEndUtc.Ticks + sinceSnapshot.Elapsed.Ticks), DateTimeKind.Utc);
    }

    private void ShowTooltip(ActivityLiveTimelineTooltip? tooltip, Point pointer)
    {
        if (tooltip is null)
        {
            tooltipPanel.IsVisible = false;
            return;
        }
        var availableWidth = Math.Max(1, canvas.Bounds.Width - 16);
        tooltipPanel.MaxWidth = Math.Min(384, availableWidth);
        var header = new WrapPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = tooltip.Title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrEmpty(tooltip.TimeText)) header.Children.Add(new TextBlock { Text = tooltip.TimeText, FontSize = 12, Opacity = 0.75 });
        var content = new StackPanel { Spacing = 4, IsHitTestVisible = false };
        content.Children.Add(header);
        if (!string.IsNullOrEmpty(tooltip.ContextText)) content.Children.Add(TooltipLine(tooltip.ContextText, true));
        foreach (var line in tooltip.Lines) content.Children.Add(TooltipLine(line));
        tooltipPanel.Child = content;
        tooltipPanel.IsVisible = true;
        tooltipPanel.Measure(new Size(availableWidth, double.PositiveInfinity));
        var size = tooltipPanel.DesiredSize;
        var left = Math.Clamp(pointer.X + 12, 8, Math.Max(8, canvas.Bounds.Width - size.Width - 8));
        var top = pointer.Y + 16;
        if (top + size.Height > canvas.Bounds.Height - 4) top = Math.Max(4, pointer.Y - size.Height - 12);
        Canvas.SetLeft(tooltipPanel, left);
        Canvas.SetTop(tooltipPanel, top);
    }

    private static TextBlock TooltipLine(string text, bool muted = false) => new()
    {
        Text = text,
        FontSize = 12,
        Opacity = muted ? 0.75 : 1,
        TextWrapping = TextWrapping.Wrap
    };

    private void DrawLatest()
    {
        var display = replayMode ? ReplayDisplay() : latest;
        canvas.SetSnapshot(display, replayMode ? display?.WindowEndUtc ?? DateTime.UtcNow : LiveEndUtc());
    }

    private void UpdateState()
    {
        canvas.IsLive = IsRolling;
        canvas.IsReplay = replayMode;
        canvas.InvalidateVisual();
        window.IsVisible = IsRolling && ShowWindowSelector || replayMode && ShowReplayControls;
        replayControls.IsVisible = ShowReplayControls && !IsRolling;
        playReplay.IsEnabled = CanReplay;
        overviewReplay.IsVisible = replayMode;
        overviewReplay.IsEnabled = replayMode;
        restartReplay.IsVisible = replayMode;
        restartReplay.IsEnabled = replayMode && CanReplay;
        replaySeekRow.IsVisible = replayMode;
        replaySpeedSelector.IsEnabled = CanReplay;
        replaySeek.IsEnabled = replayMode && CanReplay;
        var cursor = replayMode ? ReplayCursorUtc() : latest?.WindowStartUtc ?? default;
        var finished = replayMode && cursor >= latest!.WindowEndUtc;
        playReplay.Content = !replayMode || finished ? "Replay" : replayPlaying ? "Pause" : "Play";
        updatingReplayPosition = true;
        try
        {
            var total = latest == null ? 0 : Math.Max(0, (latest.WindowEndUtc - latest.WindowStartUtc).TotalSeconds);
            var elapsed = latest == null ? 0 : Math.Max(0, (cursor - latest.WindowStartUtc).TotalSeconds);
            replaySeek.Maximum = Math.Max(1, total);
            replaySeek.Value = Math.Min(total, elapsed);
            replayPosition.Text = ActivityDisplay.Duration(elapsed) + " / " + ActivityDisplay.Duration(total);
        }
        finally { updatingReplayPosition = false; }
        var windowTitle = WindowMinutes switch { 60 => "Last hour", 120 => "Last 2 hours", _ => $"Last {WindowMinutes} minutes" };
        titleText.Text = replayMode ? "Session replay" : string.IsNullOrWhiteSpace(Title) ? IsRolling ? windowTitle : "Activity over time" : Title;
        stateText.Text = latest?.HasContext != true ? "Waiting for activity" : IsRolling
            ? "Live"
            : replayMode ? $"{cursor.ToLocalTime():HH:mm:ss} · " + (finished ? "Finished" : replayPlaying ? "Playing" : "Paused")
            : $"{latest.WindowStartUtc.ToLocalTime():HH:mm:ss} – {latest.WindowEndUtc.ToLocalTime():HH:mm:ss}";
        if (latest?.IsPartial == true && IsRolling) stateText.Text += " · Some activity missing";
        var warnings = new List<string>();
        if (latest?.IsPartial == true) warnings.Add("Some activity may be missing.");
        if (latest?.IsTruncated == true) warnings.Add("Some events are hidden when the chart is busy.");
        ToolTip.SetTip(stateText, warnings.Count > 0 ? string.Join("\n", warnings) : null);
    }

    private static TextBlock Legend(string text, Color color) => new()
    {
        Text = "●  " + text,
        Foreground = new SolidColorBrush(color),
        FontSize = 12,
        Margin = new Thickness(0, 0, 18, 2)
    };

    private static Button ReplayButton(string label, string name) => new()
    {
        Name = name,
        Content = label,
        Height = 32,
        Margin = new Thickness(0, 0, 6, 0)
    };

}
