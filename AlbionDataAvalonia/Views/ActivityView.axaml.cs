using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace AlbionDataAvalonia.Views;

public partial class ActivityView : UserControl
{
    // DataGrid caps its initial rows when measured with unlimited height.
    public static readonly FuncValueConverter<int, double> HistoryTableHeightConverter = new(count => 52 + Math.Max(0, count) * 32d);
    public static readonly FuncValueConverter<int, double> HistoryMapTableHeightConverter = new(count => 52 + Math.Max(0, count) * 48d);
    public static readonly FuncValueConverter<int, double> HistoryMobTableHeightConverter = new(count => 52 + Math.Max(0, count) * 36d);
    public static readonly FuncValueConverter<int, double> HistoryLootTableHeightConverter = new(count => 52 + Math.Max(0, count) * 40d);
    public static readonly FuncValueConverter<double, double> HistoryTimelineViewportHeightConverter = new(height => Math.Clamp(height - 120, 240, 640));

    private ActivityViewModel? activeViewModel;
    private bool isAttached;
    private Window? window;
    public ActivityView()
    {
        InitializeComponent();
        HistorySessionDetailScroll.AddHandler(PointerWheelChangedEvent, HistoryTable_PointerWheelChanged, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => { isAttached = true; WatchWindow(TopLevel.GetTopLevel(this) as Window); SynchronizeViewModel(); };
        DetachedFromVisualTree += (_, _) => { isAttached = false; WatchWindow(null); activeViewModel?.SetActive(false); };
        // The main window hides a page it leaves, then drops it.
        PropertyChanged += (_, args) => { if (args.Property == IsVisibleProperty) SynchronizeViewModel(); };
        // A dropped view lets go of the view model, which would otherwise keep it alive through its event.
        DetachedFromLogicalTree += (_, _) => ReleaseViewModel();
        DataContextChanged += (_, _) => SynchronizeViewModel();
    }

    private void HistoryTable_PointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        if (sender is not ScrollViewer scroll || args.Delta.Y == 0 || args.KeyModifiers.HasFlag(KeyModifiers.Shift)
            || args.Source is not Visual source
            || (source as DataGrid ?? source.FindAncestorOfType<DataGrid>()) is not { } table) return;

        if (table.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled)
        {
            var scrollbar = table.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(bar => bar.Orientation == Orientation.Vertical);
            if (scrollbar is not null && (args.Delta.Y > 0 ? scrollbar.Value > scrollbar.Minimum : scrollbar.Value < scrollbar.Maximum)) return;
        }

        // DataGrid consumes wheel input even when disabled or at the end of its viewport.
        var offset = Math.Clamp(scroll.Offset.Y - args.Delta.Y * 50, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        scroll.Offset = new Vector(scroll.Offset.X, offset);
        args.Handled = true;
    }

    // Ctrl and Shift add sessions to the list's selection; the view model combines them.
    private void SessionList_SelectionChanged(object? sender, SelectionChangedEventArgs args) =>
        (DataContext as ActivityViewModel)?.History.SelectPlaySessions(SessionList.SelectedItem as ActivityPlaySessionRow,
            SessionList.SelectedItems?.OfType<ActivityPlaySessionRow>().ToArray() ?? []);

    private void SynchronizeViewModel()
    {
        if (activeViewModel != DataContext)
        {
            ReleaseViewModel();
            activeViewModel = DataContext as ActivityViewModel;
            if (activeViewModel is not null) activeViewModel.History.PropertyChanged += HistoryPropertyChanged;
            ApplyPlayerColumns();
        }
        activeViewModel?.SetActive(isAttached && IsVisible && IsWindowShown);
    }

    // The page reads the journal every second; nobody sees it while the window is minimized or in the tray.
    private bool IsWindowShown => window is null || window.IsVisible && window.WindowState != WindowState.Minimized;

    private void WatchWindow(Window? value)
    {
        if (window is not null) window.PropertyChanged -= WindowPropertyChanged;
        window = value;
        if (window is not null) window.PropertyChanged += WindowPropertyChanged;
    }

    private void WindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty || args.Property == Window.WindowStateProperty) SynchronizeViewModel();
    }

    private void ReleaseViewModel()
    {
        if (activeViewModel is null) return;
        activeViewModel.SetActive(false);
        activeViewModel.History.PropertyChanged -= HistoryPropertyChanged;
        activeViewModel = null;
    }

    private void HistoryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ActivityHistoryViewModel.PlayerColumns)) ApplyPlayerColumns();
    }

    // DataGrid columns are outside the visual tree, so their visibility follows the model here.
    private void ApplyPlayerColumns()
    {
        var columns = activeViewModel?.History.PlayerColumns ?? ActivityPlayerColumns.None;
        foreach (var column in HistoryPlayersTable.Columns)
            column.IsVisible = column.Tag switch
            {
                "Fame" => columns.Fame,
                "Silver" => columns.Silver,
                "Loot" => columns.Loot,
                "Gathered" => columns.Gathered,
                "Deaths" => columns.Deaths,
                _ => column.IsVisible
            };
    }
}
