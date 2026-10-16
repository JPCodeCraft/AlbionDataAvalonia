using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.ViewModels;
using AlbionDataAvalonia.Network.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

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
    private bool choosingLootExport;
    public ActivityView()
    {
        InitializeComponent();
        HistorySessionDetailScroll.AddHandler(PointerWheelChangedEvent, HistoryTable_PointerWheelChanged, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => { isAttached = true; SynchronizeViewModel(); };
        DetachedFromVisualTree += (_, _) => { isAttached = false; activeViewModel?.SetActive(false); };
        DataContextChanged += (_, _) => SynchronizeViewModel();
    }

    public ActivityView(ActivityViewModel viewModel) : this() => DataContext = viewModel;

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

    private void ExportSessionLoot_Click(object? sender, RoutedEventArgs args)
        => ExportLoot(sender, viewModel => viewModel.CaptureSessionLootExportQuery());

    private void ExportCurrentLoot_Click(object? sender, RoutedEventArgs args)
        => ExportLoot(sender, viewModel => viewModel.CaptureCurrentLootExportQuery());

    private void ExportGlobalLoot_Click(object? sender, RoutedEventArgs args)
        => ExportLoot(sender, viewModel => viewModel.CaptureGlobalLootExportQuery());

    private async void ExportLoot(object? sender, Func<ActivityViewModel, ActivityQuery?> captureQuery)
    {
        if (choosingLootExport || sender is not MenuItem { Tag: LootExportFormat format }
            || DataContext is not ActivityViewModel viewModel || TopLevel.GetTopLevel(this) is not Window owner
            || captureQuery(viewModel) is not { } query) return;
        choosingLootExport = true;
        try
        {
            CsvExportOptions? options = null;
            if (format == LootExportFormat.AfmCsv)
            {
                options = await CsvExportOptionsWindow.ShowAsync(owner);
                if (options is null) return;
            }
            var viewer = format == LootExportFormat.LootLoggerViewer;
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = viewer ? "Export loot for Loot Logger Viewer" : "Export loot for AFM Loot Logger",
                SuggestedFileName = viewer ? $"loot-events-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt" : $"afm-loot-log-{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                FileTypeChoices = [new FilePickerFileType(viewer ? "Loot Logger Files" : "CSV Files") { Patterns = [viewer ? "*.txt" : "*.csv"] }]
            });
            if (file is null) return;
            if (!viewModel.CanStartLootExport)
            {
                viewModel.LootExportStatusText = "Export canceled.";
                return;
            }
            await using var stream = await file.OpenWriteAsync();
            await viewModel.ExportLootAsync(stream, query, format, options);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not open the loot export file.");
            viewModel.LootExportStatusText = "Export failed. The file may be incomplete.";
        }
        finally { choosingLootExport = false; }
    }

    private async void NewSession_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ActivityViewModel viewModel || !viewModel.HasCurrentPlaySession || TopLevel.GetTopLevel(this) is not Window owner) return;
        var id = viewModel.CurrentPlaySessionId;
        if (await ConfirmAsync(owner, "Start a new session?", "This session ends at your last activity and stays in History. Your next activity starts the new session.", "New session")
            && viewModel.CurrentPlaySessionId == id)
            viewModel.StartNewSession(id);
    }

    private void RunChip_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ActivityViewModel viewModel && (sender as Control)?.DataContext is ActivitySessionRow run) viewModel.OpenRun(run.Id);
    }

    private async void DeleteSession_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ActivityViewModel viewModel || !viewModel.CanDeleteSelectedSession || TopLevel.GetTopLevel(this) is not Window owner) return;
        var id = (viewModel.SelectedPlaySession?.Id, viewModel.SelectedSession?.Id);
        var label = viewModel.SelectedSession is null ? "session" : "run";
        if (await ConfirmAsync(owner, $"Delete {label}?", $"Delete the {viewModel.DeleteTargetText} and all activity in it? This cannot be undone.", $"Delete {label}")
            && (viewModel.SelectedPlaySession?.Id, viewModel.SelectedSession?.Id) == id)
            await viewModel.DeleteSelectedSessionAsync();
    }

    private async void DeleteUnassignedLoot_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ActivityViewModel viewModel || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (await ConfirmAsync(owner, "Delete loot outside sessions?", "Permanently delete pickups outside sessions for all characters and servers? This ignores the current filters. Session loot is kept.", "Delete loot"))
            await viewModel.DeleteUnassignedLootAsync();
    }

    private async void CleanupActivity_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ActivityViewModel viewModel || !viewModel.CanApplyCleanup ||
            viewModel.CleanupPreview is not { } preview || TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            if (await ConfirmAsync(owner, "Delete activity?", ActivityViewModel.CleanupConfirmationText(preview), "Delete data"))
                await viewModel.ApplyCleanupAsync(preview);
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not clean up activity.");
            viewModel.CleanupStatusText = "Cleanup failed. Refresh and try again.";
        }
    }

    private static Task<bool> ConfirmAsync(Window owner, string title, string message, string action)
    {
        var cancel = new Button { Content = "Cancel" };
        var confirm = new Button { Content = action };
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.DemiBold },
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, confirm } }
                }
            }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(owner);
    }
}
