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
    public static readonly FuncValueConverter<int, double> HistoryLootTableHeightConverter = new(count => 52 + Math.Max(0, count) * 56d);
    public static readonly FuncValueConverter<double, double> HistoryTimelineViewportHeightConverter = new(height => Math.Clamp(height - 120, 240, 640));

    private ActivityViewModel? activeViewModel;
    private bool isAttached;
    public ActivityView()
    {
        InitializeComponent();
        HistorySessionDetailScroll.AddHandler(PointerWheelChangedEvent, HistoryTable_PointerWheelChanged, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => { isAttached = true; SynchronizeViewModel(); };
        DetachedFromVisualTree += (_, _) => { isAttached = false; activeViewModel?.SetActive(false); };
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

    private void SynchronizeViewModel()
    {
        if (activeViewModel != DataContext)
        {
            activeViewModel?.SetActive(false);
            if (activeViewModel is not null) activeViewModel.PropertyChanged -= ViewModelPropertyChanged;
            activeViewModel = DataContext as ActivityViewModel;
            if (activeViewModel is not null) activeViewModel.PropertyChanged += ViewModelPropertyChanged;
            ApplyPlayerColumns();
        }
        activeViewModel?.SetActive(isAttached);
    }

    private void ViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ActivityViewModel.PlayerColumns)) ApplyPlayerColumns();
    }

    // DataGrid columns are outside the visual tree, so their visibility follows the model here.
    private void ApplyPlayerColumns()
    {
        var columns = activeViewModel?.PlayerColumns ?? ActivityPlayerColumns.None;
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
