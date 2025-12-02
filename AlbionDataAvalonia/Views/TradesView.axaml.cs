using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;

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
    }
}
