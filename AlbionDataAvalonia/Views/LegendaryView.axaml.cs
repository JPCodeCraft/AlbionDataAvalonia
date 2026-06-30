using AlbionDataAvalonia.ViewModels;
using Avalonia.Controls;
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
            || !await CleanupConfirmWindow.ShowAsync(owner, "legendary items", selectedItems.Count))
        {
            return;
        }

        await viewModel.DeleteSelectedItemsAsync(selectedItems);
    }

    private async void SellOnDiscordButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LegendaryViewModel viewModel
            || viewModel.SelectedItem is null
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var eligibility = await viewModel.RefreshEligibilityAsync();
        if (!eligibility.CanPost)
        {
            await LegendarySaleWindow.ShowResultAsync(owner, eligibility.Description, null, eligibility.InviteUrl);
            return;
        }

        var saleInput = await LegendarySaleWindow.ShowAsync(
            owner,
            viewModel.SelectedItem,
            eligibility.DiscordUsername,
            viewModel.DefaultInGameName);
        if (saleInput is null)
        {
            return;
        }

        var result = await viewModel.PostToDiscordAsync(saleInput.PriceSilver, saleInput.InGameName);
        var message = result.RetryAfterSeconds is { } retryAfter
            ? $"{result.Message}\nTry again in {System.TimeSpan.FromSeconds(retryAfter):g}."
            : result.Message;
        await LegendarySaleWindow.ShowResultAsync(owner, message, result.MessageUrl, result.InviteUrl);
    }
}
