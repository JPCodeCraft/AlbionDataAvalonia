using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Windows.Input;

namespace AlbionDataAvalonia.Views;

/// <summary>
/// The loot export controls. Each loot list passes the command that exports its own query; the command
/// receives the chosen <see cref="LootExportFormat"/>.
/// </summary>
public partial class ActivityLootExportBar : UserControl
{
    public static readonly StyledProperty<ICommand?> ExportCommandProperty =
        AvaloniaProperty.Register<ActivityLootExportBar, ICommand?>(nameof(ExportCommand));

    public static readonly StyledProperty<bool> CanExportProperty =
        AvaloniaProperty.Register<ActivityLootExportBar, bool>(nameof(CanExport));

    public static readonly StyledProperty<bool> ShowLootLoggerLinkProperty =
        AvaloniaProperty.Register<ActivityLootExportBar, bool>(nameof(ShowLootLoggerLink));

    public ActivityLootExportBar()
    {
        InitializeComponent();
    }

    public ICommand? ExportCommand
    {
        get => GetValue(ExportCommandProperty);
        set => SetValue(ExportCommandProperty, value);
    }

    public bool CanExport
    {
        get => GetValue(CanExportProperty);
        set => SetValue(CanExportProperty, value);
    }

    /// <summary>Shows a link to the AFM Loot Logger, which reads the AFM CSV export.</summary>
    public bool ShowLootLoggerLink
    {
        get => GetValue(ShowLootLoggerLinkProperty);
        set => SetValue(ShowLootLoggerLinkProperty, value);
    }

    private void ExportAfmCsv_Click(object? sender, RoutedEventArgs e) => Export(LootExportFormat.AfmCsv);

    private void ExportLootLoggerViewer_Click(object? sender, RoutedEventArgs e) => Export(LootExportFormat.LootLoggerViewer);

    private void Export(LootExportFormat format)
    {
        if (ExportCommand?.CanExecute(format) == true) ExportCommand.Execute(format);
    }
}
