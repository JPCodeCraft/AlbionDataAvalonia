using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace AlbionDataAvalonia.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView(SettingsViewModel settingsViewModel)
        {
            InitializeComponent();
            this.DataContext = settingsViewModel;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (DataContext is SettingsViewModel vm)
            {
                vm.UpdateStreamerPreview(vm.SelectedTabIndex == SettingsViewModel.StreamerTabIndex);
                if (vm.SelectedTabIndex == SettingsViewModel.DataTabIndex) _ = vm.RefreshStorageAsync();
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            (DataContext as SettingsViewModel)?.UpdateStreamerPreview(false);
            base.OnDetachedFromVisualTree(e);
        }
    }
}
