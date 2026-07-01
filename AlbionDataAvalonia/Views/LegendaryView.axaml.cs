using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
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
        if (DataContext is not LegendaryViewModel viewModel
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var selectedItems = LegendaryItemsGrid.SelectedItems.OfType<LegendaryItemRowViewModel>().ToList();
        if (selectedItems.Count == 0
            || !await CleanupConfirmWindow.ShowAsync(owner, "awakened items", selectedItems.Count))
        {
            return;
        }

        await viewModel.DeleteSelectedItemsAsync(selectedItems);
    }

    private async void ListForSaleButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LegendaryViewModel viewModel
            || viewModel.SelectedItem is null
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var saleInput = await LegendarySaleWindow.ShowAsync(
            owner,
            viewModel.SelectedItem,
            "The listing will also be announced on Discord when your AFM account is linked and Discord delivery is available.",
            viewModel.DefaultInGameName);
        if (saleInput is null)
        {
            return;
        }

        var result = await viewModel.CreateSellOrderAsync(saleInput.PriceSilver, saleInput.InGameName);
        var message = result.RetryAfterSeconds is { } retryAfter
            ? $"{result.Message}\nDiscord can be tried again in {TimeSpan.FromSeconds(retryAfter):g}."
            : result.Message;
        await LegendarySaleWindow.ShowResultAsync(owner, message, result.MessageUrl);
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
        var result = await viewModel.SetSelectedSoldAsync(checkBox.IsChecked == true);
        if (!result.Success)
        {
            checkBox.IsChecked = viewModel.SelectedItem?.IsSold == true;
        }
        checkBox.IsEnabled = true;
    }

    private void OpenDiscordPostButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
