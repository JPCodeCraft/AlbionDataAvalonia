using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;

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

    private async void ExportHistorySessionCsvButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GatheringViewModel viewModel
            || !viewModel.CanExportSelectedCompletedSession
            || viewModel.SelectedCompletedSession is not { } selectedSession
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (!viewModel.TryBeginGatheringCsvExport())
        {
            return;
        }

        try
        {
            var exportOptions = await CsvExportOptionsWindow.ShowAsync(owner);
            if (exportOptions is null)
            {
                return;
            }

            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Gathering Session to CSV",
                SuggestedFileName = $"gathering_session_{selectedSession.StartedAtUtc:yyyyMMdd_HHmmss}.csv",
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new("CSV Files") { Patterns = ["*.csv"] }
                }
            });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            await viewModel.ExportSelectedCompletedSessionToCsvAsync(stream, exportOptions);
        }
        finally
        {
            viewModel.EndGatheringCsvExport();
        }
    }

    private async void AddHistorySessionToPortfolioButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GatheringViewModel viewModel
            || !viewModel.CanAddSelectedCompletedSessionToPortfolio
            || viewModel.SelectedCompletedSession is not { } selectedSession
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        if (!viewModel.TryBeginGatheringPortfolioImport())
        {
            return;
        }

        var selectedSessionId = selectedSession.Id;
        try
        {
            if (!await viewModel.EnsurePortfolioSignedInAsync())
            {
                await PortfolioSignInRequiredWindow.ShowAsync(owner, "gathering data");
                return;
            }

            var uploadedDataIds = await viewModel.GetPortfolioUploadedDataIdsAsync();
            if (uploadedDataIds is null
                || viewModel.SelectedCompletedSession?.Id != selectedSessionId
                || !viewModel.IsSelectedCompletedSessionLoaded)
            {
                return;
            }

            var itemRows = viewModel.HistoryItemRows.ToArray();
            var alreadyUploadedCount = itemRows.Count(row => uploadedDataIds.Contains(row.Id));
            var allowReupload = false;
            if (alreadyUploadedCount > 0)
            {
                allowReupload = await ConfirmPortfolioReuploadWindow.ShowAsync(
                    owner,
                    alreadyUploadedCount,
                    itemRows.Length,
                    "gathering item");
                if (!allowReupload)
                {
                    return;
                }
            }

            var popupItems = itemRows
                .Select(row => new GatheringPortfolioImportItemInput(
                    row.Id,
                    row.ItemName,
                    row.Amount,
                    row.Quality is >= 1 and <= 5 ? row.Quality : 1,
                    row.EstimatedMarketValue))
                .ToArray();
            var selection = await GatheringPortfolioImportWindow.ShowAsync(
                owner,
                popupItems,
                AlbionLocations.GetMarketLocations());
            if (selection is null
                || viewModel.SelectedCompletedSession?.Id != selectedSessionId
                || !viewModel.IsSelectedCompletedSessionLoaded)
            {
                return;
            }

            await viewModel.AddSelectedCompletedSessionToPortfolioAsync(
                selection.LocationIndex,
                selection.UnitPrices,
                allowReupload);
        }
        finally
        {
            viewModel.EndGatheringPortfolioImport();
        }
    }

    private async void ShareHistorySessionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GatheringViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var shareCard = viewModel.CreateSelectedSessionShareCard();
        if (shareCard is null)
        {
            return;
        }

        await GatheringSessionShareWindow.ShowAsync(owner, shareCard);
    }
}
