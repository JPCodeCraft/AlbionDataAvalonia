using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Serilog;
using System;
using System.Linq;

namespace AlbionDataAvalonia.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView(LogsViewModel logsViewModel)
        {
            InitializeComponent();
            this.DataContext = logsViewModel;
        }

        private void LogMessage_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
            {
                return;
            }

            if (sender is not TextBlock textBlock)
            {
                return;
            }

            if (textBlock.DataContext is not LogEventWrapper logEventWrapper)
            {
                return;
            }

            if (!logEventWrapper.HasPublicUploadLink || string.IsNullOrWhiteSpace(logEventWrapper.PublicUploadUrl))
            {
                return;
            }

            if (!Uri.TryCreate(logEventWrapper.PublicUploadUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            OpenUrl(uri);
            e.Handled = true;
        }

        private void LogMessage_DataContextChanged(object? sender, EventArgs e)
        {
            if (sender is not TextBlock textBlock)
            {
                return;
            }

            if (textBlock.DataContext is not LogEventWrapper logEventWrapper)
            {
                textBlock.Classes.Set("link", false);
                return;
            }

            textBlock.Classes.Set("link", logEventWrapper.HasPublicUploadLink);
        }

        private void LogsGrid_CopyingRowClipboardContent(object? sender, DataGridRowClipboardEventArgs e)
        {
            if (e.IsColumnHeadersRow || e.Item is not LogEventWrapper logEventWrapper)
            {
                return;
            }

            if (sender is not DataGrid dataGrid)
            {
                return;
            }

            var messageColumn = dataGrid.Columns.FirstOrDefault(column =>
                string.Equals(column.Header?.ToString(), "Message", StringComparison.Ordinal));

            if (messageColumn is null)
            {
                return;
            }

            var rowContent = e.ClipboardRowContent;
            for (int i = 0; i < rowContent.Count; i++)
            {
                if (rowContent[i].Column == messageColumn)
                {
                    rowContent[i] = new DataGridClipboardCellContent(e.Item, messageColumn, logEventWrapper.RenderedMessage);
                    return;
                }
            }

            rowContent.Add(new DataGridClipboardCellContent(e.Item, messageColumn, logEventWrapper.RenderedMessage));
        }

        private static void OpenUrl(Uri uri) => SystemLauncher.TryOpenUrl(uri);
    }
}
