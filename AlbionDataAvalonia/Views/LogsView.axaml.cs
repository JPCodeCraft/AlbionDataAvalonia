using AlbionDataAvalonia.Logging;
using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Serilog;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

        private void OpenUrl(Uri uri)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.ToString(),
                        UseShellExecute = true
                    });

                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("x-www-browser", uri.ToString());
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", uri.ToString());
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to open URL {Url}", uri);
            }
        }
    }
}
