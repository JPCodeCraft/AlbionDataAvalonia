using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

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

            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            var exportOptions = await CsvExportOptionsWindow.ShowAsync(owner);
            if (exportOptions == null) return;

            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
            await vm.ExportToCsvAsync(stream, exportOptions);
        }

        private void TradesGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not TradesViewModel vm) return;
            if (sender is not DataGrid grid) return;

            vm.UpdateSelectedTrades(grid.SelectedItems.OfType<TradeRowViewModel>());
        }

        private async void AddToPortfolioButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not TradesViewModel vm) return;
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            var selectedRows = TradesGrid.SelectedItems.OfType<TradeRowViewModel>().ToList();
            if (selectedRows.Count == 0) return;
            if (!vm.CanAddSelectedTradesToPortfolio) return;

            if (!await vm.EnsurePortfolioSignedInAsync())
            {
                await PortfolioSignInRequiredWindow.ShowAsync(owner);
                return;
            }

            if (!await vm.RefreshPortfolioUploadedTradeIdsAsync(showStatusOnFailure: true))
            {
                return;
            }

            var alreadyUploadedCount = selectedRows.Count(row => row.UploadedToPortfolio);
            var allowReupload = false;
            if (alreadyUploadedCount > 0)
            {
                allowReupload = await ConfirmPortfolioReuploadWindow.ShowAsync(owner, alreadyUploadedCount, selectedRows.Count);
                if (!allowReupload)
                {
                    return;
                }
            }

            var qualityOverrides = await GetUnknownQualityOverridesAsync(owner, selectedRows);
            if (qualityOverrides == null)
            {
                return;
            }

            await vm.AddTradesToPortfolioAsync(selectedRows, qualityOverrides, allowReupload);
        }

        private async void SetQualityButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not TradesViewModel vm) return;
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            var selectedRows = TradesGrid.SelectedItems.OfType<TradeRowViewModel>().ToList();
            if (selectedRows.Count == 0) return;
            if (!vm.CanSetSelectedTradeQuality) return;

            var selectedQuality = await TradeQualitySelectionWindow.ShowAsync(owner, selectedRows.Count(row => !row.UploadedToPortfolio));
            if (selectedQuality == null)
            {
                return;
            }

            await vm.SetSelectedTradesQualityAsync(selectedRows, selectedQuality.Value);
        }

        private static async Task<IReadOnlyDictionary<PortfolioTradeQualityKey, int>?> GetUnknownQualityOverridesAsync(
            Window owner,
            IReadOnlyList<TradeRowViewModel> selectedRows)
        {
            var unknownQualityGroups = selectedRows
                .Where(row => row.QualityLevel == 0)
                .GroupBy(TradesViewModel.CreateQualityKey)
                .Select(group =>
                {
                    var first = group.First();
                    return new PortfolioQualitySelectionGroup(
                        group.Key,
                        first.ItemName,
                        first.Server?.Name ?? "Unknown server",
                        group.Count());
                })
                .OrderBy(group => group.ItemName)
                .ThenBy(group => group.ServerName)
                .ToList();

            if (unknownQualityGroups.Count == 0)
            {
                return new Dictionary<PortfolioTradeQualityKey, int>();
            }

            return await PortfolioQualitySelectionWindow.ShowAsync(owner, unknownQualityGroups);
        }

        private async void CopyValuePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not TextBlock textBlock) return;
            if (textBlock.Tag is not IFormattable formattable) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            string format = textBlock.Tag is decimal ? "F2" : "F0";
            var text = formattable.ToString(format, CultureInfo.InvariantCulture);
            await clipboard.SetTextAsync(text);
        }
    }
}
