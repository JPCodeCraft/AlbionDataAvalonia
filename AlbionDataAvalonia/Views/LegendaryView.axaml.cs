using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
using Serilog;
using System;
using System.Diagnostics;
using System.Linq;

namespace AlbionDataAvalonia.Views;

public partial class LegendaryView : UserControl
{
    public LegendaryView(LegendaryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void DeleteSelectedButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not LegendaryViewModel viewModel
                || TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var selectedItems = LegendaryItemsGrid.SelectedItems.OfType<LegendaryItemRowViewModel>().ToList();
            if (selectedItems.Count == 0
                || !await CleanupConfirmWindow.ShowAsync(owner, "awakened item", "awakened items", selectedItems.Count))
            {
                return;
            }

            await viewModel.DeleteSelectedItemsAsync(selectedItems);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not delete the selected awakened items.");
        }
    }

    private async void ListForSaleButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not LegendaryViewModel viewModel
                || viewModel.SelectedItem is null
                || TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            await LegendarySaleWindow.ShowAsync(
                owner,
                viewModel.SelectedItem,
                viewModel.DefaultInGameName,
                input => viewModel.CreateSellOrderAsync(input.PriceSilver, input.InGameName));
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not open the awakened item sale window.");
        }
    }

    private async void SoldCheckBox_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox
            || DataContext is not LegendaryViewModel viewModel
            || viewModel.SelectedItem is null)
        {
            return;
        }

        checkBox.IsEnabled = false;
        try
        {
            var result = await viewModel.SetSelectedSoldAsync(checkBox.IsChecked == true);
            if (!result.Success)
            {
                checkBox.IsChecked = viewModel.SelectedItem?.IsSold == true;
            }
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not change the sold state of the awakened item.");
            checkBox.IsChecked = viewModel.SelectedItem?.IsSold == true;
        }
        finally
        {
            checkBox.IsEnabled = true;
        }
    }

    private async void CancelListingButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button
            || DataContext is not LegendaryViewModel viewModel
            || !viewModel.CanCancelSelectedListing)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            await viewModel.CancelSelectedListingAsync();
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not cancel the awakened item listing.");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void OpenDiscordPostButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void OpenCalculatorButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
