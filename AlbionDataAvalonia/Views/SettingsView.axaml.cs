using AlbionDataAvalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

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

        private async void CopyUserIdButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.CurrentFirebaseUserId)) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            await clipboard.SetTextAsync(vm.CurrentFirebaseUserId);
        }

        private async void DeleteData_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm || !vm.CanDeleteData) return;
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            if (await CleanupConfirmWindow.ShowAsync(owner, "Delete stored data?", vm.CleanupConfirmationText(), "Delete"))
            {
                await vm.ApplyCleanupAsync();
            }
        }

        private void RemoveSharedUserButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm) return;
            if (sender is not Button button) return;
            if (button.Tag is not PrivateOrderShareEntryViewModel entry) return;

            vm.RemoveSharedUserCommand.Execute(entry);
        }
    }
}
