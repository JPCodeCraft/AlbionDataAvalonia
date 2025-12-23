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

        private void MailsGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MailsViewModel vm) return;
            if (sender is not DataGrid grid) return;

            vm.UpdateSelectedMails(grid.SelectedItems.OfType<AlbionMail>());
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
