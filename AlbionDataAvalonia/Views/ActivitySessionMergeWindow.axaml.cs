using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AlbionDataAvalonia.Views;

public partial class ActivitySessionMergeWindow : Window
{
    public ActivitySessionMergeWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is ActivitySessionMergeViewModel viewModel) await viewModel.LoadAsync();
        };
        Closing += (_, args) => args.Cancel = DataContext is ActivitySessionMergeViewModel { IsMerging: true };
    }

    public ActivitySessionMergeWindow(ActivitySessionMergeViewModel viewModel) : this() => DataContext = viewModel;

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();

    private async void Merge_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is ActivitySessionMergeViewModel viewModel && await viewModel.MergeAsync()) Close();
    }
}
