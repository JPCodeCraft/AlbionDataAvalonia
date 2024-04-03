using AlbionDataAvalonia.ViewModels;
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

        private void Binding(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e)
        {
        }
    }
}
