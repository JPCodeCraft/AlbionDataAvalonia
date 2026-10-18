using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (IsVisible) Shown();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            (DataContext as SettingsViewModel)?.UpdateStreamerPreview(false);
            base.OnDetachedFromVisualTree(e);
        }

        // The main window hides a page it leaves rather than detaching it.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsVisibleProperty || VisualRoot is null) return;
            if (IsVisible) Shown();
            else (DataContext as SettingsViewModel)?.UpdateStreamerPreview(false);
        }

        private void Shown()
        {
            if (DataContext is not SettingsViewModel vm) return;
            vm.UpdateStreamerPreview(vm.SelectedTabIndex == SettingsViewModel.StreamerTabIndex);
            if (vm.SelectedTabIndex == SettingsViewModel.DataTabIndex) _ = vm.RefreshStorageAsync();
        }
    }
}
