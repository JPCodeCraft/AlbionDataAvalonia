using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Views;

public partial class LootView : UserControl
{
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

    private async void ExportCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
            Title = "Export Loot to CSV",
            SuggestedFileName = $"loot_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
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
}
