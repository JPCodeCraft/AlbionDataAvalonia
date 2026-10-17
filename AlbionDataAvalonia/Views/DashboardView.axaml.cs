using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

        private async void CopyDiagnostics_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            try
            {
                if (clipboard is null)
                {
                    viewModel.ShowDiagnosticsCopied(false);
                    return;
                }

                await clipboard.SetTextAsync(viewModel.BuildDiagnosticsText());
                viewModel.ShowDiagnosticsCopied(true);
            }
            catch (Exception error)
            {
                Log.Warning(error, "Could not copy diagnostics to the clipboard.");
                viewModel.ShowDiagnosticsCopied(false);
            }
        }
    }
}
