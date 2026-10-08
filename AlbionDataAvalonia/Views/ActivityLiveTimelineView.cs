using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using AlbionDataAvalonia.Activity;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AlbionDataAvalonia.Views;

/// <summary>A presentation-only rolling view. Pausing this view never pauses capture.</summary>
public sealed class ActivityLiveTimelineView : UserControl
{
    public static readonly StyledProperty<ActivityLiveTimelineSnapshot?> SnapshotProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, ActivityLiveTimelineSnapshot?>(nameof(Snapshot));
    public static readonly StyledProperty<ICommand?> OpenEncounterCommandProperty =
        AvaloniaProperty.Register<ActivityLiveTimelineView, ICommand?>(nameof(OpenEncounterCommand));

    private readonly ActivityLiveTimelineCanvas canvas = new();
    private readonly Button pauseButton = SecondaryButton("Pause timeline");
    private readonly TextBlock stateText = new() { FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock inspectionText = new()
    {
        FontSize = 12,
        Opacity = 0.85,
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
        Height = 34,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly TextBlock detailTitle = new() { FontSize = 14, FontWeight = FontWeight.DemiBold };
    private readonly TextBlock detailText = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Border detailPanel;
    private readonly TextBlock damageLegend = Legend("Damage dealt", ActivityLiveTimelineCanvas.DamageColor);
    private readonly TextBlock healingLegend = Legend("Healing done", ActivityLiveTimelineCanvas.HealingColor);
    private readonly DispatcherTimer renderTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch sinceSnapshot = new();
    private ActivityLiveTimelineSnapshot? latest;
    private ActivityLiveTimelineSnapshot? displayed;
    private DateTime heldEndUtc;
    private bool pointerHolding;
    private bool manuallyPaused;

    public ActivityLiveTimelineSnapshot? Snapshot { get => GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public ICommand? OpenEncounterCommand { get => GetValue(OpenEncounterCommandProperty); set => SetValue(OpenEncounterCommandProperty, value); }

    public ActivityLiveTimelineView()
    {
        var direction = new ComboBox
        {
            ItemsSource = new[] { "Dealt", "Received" },
            SelectedIndex = 0,
            Width = 116,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#424242")),
            BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        direction.SelectionChanged += (_, _) =>
        {
            canvas.ShowReceived = direction.SelectedIndex == 1;
            damageLegend.Text = canvas.ShowReceived ? "●  Damage received" : "●  Damage dealt";
            healingLegend.Text = canvas.ShowReceived ? "●  Healing received" : "●  Healing done";
            canvas.InvalidateVisual();
        };
        pauseButton.Click += (_, _) =>
        {
            if (manuallyPaused || pointerHolding) ResumeLive();
            else { Hold(); manuallyPaused = true; UpdateState(); }
        };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        heading.Children.Add(new StackPanel
        {
            Spacing = 3,
            Children = { new TextBlock { Text = "Last five minutes", FontSize = 18, FontWeight = FontWeight.DemiBold }, stateText }
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { direction, pauseButton } };
        Grid.SetColumn(actions, 1);
        heading.Children.Add(actions);

        var legends = new WrapPanel();
        foreach (var legend in new[]
        {
            damageLegend, healingLegend, Legend("Kills", Color.Parse("#64CBDD")), Legend("Deaths / knockdowns", Color.Parse("#F47D7D")),
            Legend("Loot", Color.Parse("#BF91F0")), Legend("Your fame", Color.Parse("#86D1A2")), Legend("Silver", Color.Parse("#E7BD64"))
        }) legends.Children.Add(legend);

        var closeDetails = SecondaryButton("Close");
        var detailHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        detailHeader.Children.Add(detailTitle);
        Grid.SetColumn(closeDetails, 1);
        detailHeader.Children.Add(closeDetails);
        detailPanel = new Border
        {
            IsVisible = false,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.Parse("#424242")),
            BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { detailHeader, new ScrollViewer { MaxHeight = 170, Content = detailText, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } }
            }
        };
        closeDetails.Click += (_, _) => detailPanel.IsVisible = false;
        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#383838")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new StackPanel { Spacing = 9, Children = { heading, legends, canvas, inspectionText, detailPanel } }
        };
        canvas.HoverStarted += () => { if (!pointerHolding) { Hold(); pointerHolding = true; UpdateState(); } };
        canvas.HoverEnded += () => { pointerHolding = false; if (!manuallyPaused) ResumeLive(); else UpdateState(); };
        canvas.InspectionChanged += text => inspectionText.Text = text ?? DefaultInspection();
        canvas.MarkerSelected += marker =>
        {
            Hold(); manuallyPaused = true;
            detailTitle.Text = marker.Label + " · " + marker.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            detailText.Text = marker.Detail;
            detailPanel.IsVisible = true;
            UpdateState();
        };
        canvas.EncounterSelected += id => { if (OpenEncounterCommand?.CanExecute(id) == true) OpenEncounterCommand.Execute(id); };
        renderTimer.Tick += (_, _) => { if (IsEffectivelyVisible && !pointerHolding && !manuallyPaused) DrawLatest(); };
        AttachedToVisualTree += (_, _) => renderTimer.Start();
        DetachedFromVisualTree += (_, _) => renderTimer.Stop();
        UpdateState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SnapshotProperty) return;
        var snapshot = Snapshot;
        var contextChanged = snapshot?.SessionId != latest?.SessionId || snapshot?.Scope != latest?.Scope;
        latest = snapshot;
        sinceSnapshot.Restart();
        if (contextChanged)
        {
            pointerHolding = false;
            manuallyPaused = false;
            detailPanel.IsVisible = false;
            canvas.ClearInspection();
        }
        if (!pointerHolding && !manuallyPaused) DrawLatest();
        UpdateState();
    }

    private void Hold()
    {
        if (pointerHolding || manuallyPaused) return;
        displayed = latest;
        heldEndUtc = LiveEndUtc();
        canvas.SetSnapshot(displayed, heldEndUtc);
    }

    private void ResumeLive()
    {
        pointerHolding = false;
        manuallyPaused = false;
        detailPanel.IsVisible = false;
        canvas.ClearInspection();
        DrawLatest();
        UpdateState();
    }

    private DateTime LiveEndUtc() => latest?.WindowEndUtc.Add(sinceSnapshot.Elapsed) ?? DateTime.UtcNow;

    private void DrawLatest()
    {
        displayed = latest;
        canvas.SetSnapshot(displayed, LiveEndUtc());
    }

    private void UpdateState()
    {
        canvas.IsLive = !pointerHolding && !manuallyPaused;
        canvas.InvalidateVisual();
        pauseButton.Content = pointerHolding || manuallyPaused ? "Resume live" : "Pause timeline";
        pauseButton.IsEnabled = latest?.SessionId != null;
        stateText.Text = latest?.SessionId == null ? "Waiting for session activity" :
            (manuallyPaused ? "Timeline paused" : pointerHolding ? "Hovering · view held" : "Live") + " · " + latest.Scope;
        if (latest?.IsPartial == true) stateText.Text += " · Partial capture";
        if (latest?.IsTruncated == true) stateText.Text += " · Busy window summarized";
        if (!canvas.HasInspection) inspectionText.Text = DefaultInspection();
    }

    private static string DefaultInspection() => "Hover to inspect · Select an event or encounter for details";
    private static TextBlock Legend(string text, Color color) => new()
    {
        Text = "●  " + text,
        Foreground = new SolidColorBrush(color),
        FontSize = 12,
        Margin = new Thickness(0, 0, 18, 2)
    };
    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        Height = 32,
        Padding = new Thickness(10, 4),
        VerticalContentAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush(Color.Parse("#383838")),
        BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
        BorderThickness = new Thickness(1),
        Classes = { "ActivityAction", "ActivitySecondary" }
    };
}
