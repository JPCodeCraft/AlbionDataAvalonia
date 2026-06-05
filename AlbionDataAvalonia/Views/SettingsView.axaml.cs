using AlbionDataAvalonia.ViewModels;
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

        private async void CopyUserIdButton_Click(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.CurrentFirebaseUserId)) return;

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            await clipboard.SetTextAsync(vm.CurrentFirebaseUserId);
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
