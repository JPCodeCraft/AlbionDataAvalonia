using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace AlbionDataAvalonia.Views;

public partial class GatheringView : UserControl
{
    private GatheringViewModel? subscribedViewModel;

    public GatheringView()
    {
        InitializeComponent();
        DataContextChanged += GatheringView_DataContextChanged;
        DetachedFromVisualTree += (_, _) => SubscribeToViewModel(null);
    }

    public GatheringView(GatheringViewModel gatheringViewModel)
    {
        InitializeComponent();
        DataContextChanged += GatheringView_DataContextChanged;
        DetachedFromVisualTree += (_, _) => SubscribeToViewModel(null);
        DataContext = gatheringViewModel;
    }

    private void GatheringView_DataContextChanged(object? sender, EventArgs e)
    {
        SubscribeToViewModel(DataContext as GatheringViewModel);
    }

    private void SubscribeToViewModel(GatheringViewModel? viewModel)
    {
        if (ReferenceEquals(subscribedViewModel, viewModel))
        {
            return;
        }

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.LiveRowsChanged -= RefreshLiveGridSorts;
        }

        subscribedViewModel = viewModel;

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.LiveRowsChanged += RefreshLiveGridSorts;
        }
    }

    private void RefreshLiveGridSorts()
    {
        RefreshGridSort(SummaryRowsGrid);
        RefreshGridSort(BucketRowsGrid);
    }

    private static void RefreshGridSort(DataGrid grid)
    {
        var selectedItem = grid.SelectedItem;
        grid.CollectionView?.Refresh();

        if (selectedItem is not null && grid.SelectedItem is null)
        {
            grid.SelectedItem = selectedItem;
        }
    }

    private async void SaveSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GatheringViewModel viewModel
            || !viewModel.SaveSessionCommand.CanExecute(null)
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (await ConfirmSaveGatheringSessionWindow.ShowAsync(owner))
        {
            await viewModel.SaveSessionCommand.ExecuteAsync(null);
        }
    }

    private async void DiscardSessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GatheringViewModel viewModel
            || !viewModel.DiscardSessionCommand.CanExecute(null)
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (await ConfirmDiscardGatheringSessionWindow.ShowAsync(owner))
        {
            await viewModel.DiscardSessionCommand.ExecuteAsync(null);
        }
    }

    private async void DeleteHistorySessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GatheringViewModel viewModel
            || !viewModel.DeleteSelectedCompletedSessionCommand.CanExecute(null)
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (await ConfirmDeleteGatheringSessionWindow.ShowAsync(owner))
        {
            await viewModel.DeleteSelectedCompletedSessionCommand.ExecuteAsync(null);
        }
    }
}
