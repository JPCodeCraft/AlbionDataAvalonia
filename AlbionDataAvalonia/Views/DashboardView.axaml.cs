using Avalonia.Controls;
using Avalonia.Input;
using Serilog;
using System;

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

            var uri = new Uri(SystemLauncher.AfmDiscordUrl);
            OpenUrl(uri);
            e.Handled = true;
        }

        private void NpcapLink_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Left)
            {
                return;
            }

            var uri = new Uri("https://npcap.com/#download");
            OpenUrl(uri);
            e.Handled = true;
        }

        private static void OpenUrl(Uri uri) => SystemLauncher.TryOpenUrl(uri);
    }
}
