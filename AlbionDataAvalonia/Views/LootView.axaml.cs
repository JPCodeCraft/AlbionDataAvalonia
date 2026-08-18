using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia.Views;

public partial class LootView : UserControl
{
    private static readonly Uri AfmLootLoggerUri = new("https://albionfreemarket.com/loot-logger");

    public LootView()
    {
        InitializeComponent();
    }

    public LootView(LootViewModel lootViewModel)
    {
        InitializeComponent();
        DataContext = lootViewModel;
    }

    private async void ClearButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LootViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner
            || !await ConfirmClearLootWindow.ShowAsync(owner))
        {
            return;
        }

        viewModel.Clear();
    }

    private async void ExportLootLogsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LootViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var exportOptions = await CsvExportOptionsWindow.ShowAsync(owner);
        if (exportOptions is null)
        {
            return;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Loot Logs for AFM Loot Logger",
            SuggestedFileName = $"afm-loot-log-{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("CSV Files") { Patterns = new[] { "*.csv" } }
            }
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await viewModel.ExportToCsvAsync(stream, exportOptions);
    }

    private void OpenAfmLootLoggerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AfmLootLoggerUri.AbsoluteUri,
                    UseShellExecute = true
                });
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", AfmLootLoggerUri.AbsoluteUri);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", AfmLootLoggerUri.AbsoluteUri);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open AFM Loot Logger URL {Url}", AfmLootLoggerUri);
        }
    }
}
