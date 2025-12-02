using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Views
{
    public partial class MailsView : UserControl
    {
        public MailsView(MailsViewModel mailsViewModel)
        {
            InitializeComponent();
            this.DataContext = mailsViewModel;
        }

        private async void ExportCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MailsViewModel vm) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Mails to CSV",
                SuggestedFileName = $"mails_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
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
