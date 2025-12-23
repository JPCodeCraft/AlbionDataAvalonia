using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Views
{
    public partial class TradesView : UserControl
    {
        public TradesView(TradesViewModel tradesViewModel)
        {
            InitializeComponent();
            this.DataContext = tradesViewModel;
        }

        private async void ExportCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not TradesViewModel vm) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Trades to CSV",
                SuggestedFileName = $"trades_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } }
                }
            });

            if (file == null) return;

            await using var stream = await file.OpenWriteAsync();
            await vm.ExportToCsvAsync(stream);
        }

        private void TradesGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not TradesViewModel vm) return;
            if (sender is not DataGrid grid) return;

            vm.UpdateSelectedTrades(grid.SelectedItems.OfType<Trade>());
        }

        private async void CopyValuePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not TextBlock textBlock) return;
            if (string.IsNullOrWhiteSpace(textBlock.Text)) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            await clipboard.SetTextAsync(textBlock.Text);
        }
    }
}
