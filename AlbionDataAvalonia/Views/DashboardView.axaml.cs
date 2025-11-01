using Avalonia.Controls;
using Avalonia.Input;
using Serilog;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AlbionDataAvalonia.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
        }

        private void AFMDiscordLink_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
            {
                return;
            }

            var uri = new Uri("https://discord.com/invite/BPmDE3zznH");
            OpenUrl(uri);
            e.Handled = true;
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
