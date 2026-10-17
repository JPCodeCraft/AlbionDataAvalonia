using AlbionDataAvalonia.Views;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

// The Awakened actions: deleting rows, listing the selected item for sale and changing its listing.
public partial class LegendaryViewModel
{
    private IReadOnlyList<LegendaryItemRowViewModel> selectedItems = [];

    /// <summary>Called by the grid; more than one row can be selected for deleting.</summary>
    public void UpdateSelectedItems(IEnumerable<LegendaryItemRowViewModel> items)
    {
        selectedItems = items.ToList();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Whether the selected item's listing is marked as sold. Setting it updates AFM; when that fails the
    /// check box goes back to the stored state.
    /// </summary>
    public bool SelectedItemSold
    {
        get => SelectedItem?.IsSold == true;
        set
        {
            if (value != SelectedItemSold) _ = SetSoldAsync(value);
        }
    }

    public bool CanChangeSelectedSold => SelectedItem?.CanChangeSold == true && !IsPosting;

    private bool CanDeleteSelected => selectedItems.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelected()
    {
        try
        {
            var items = selectedItems.ToList();
            if (items.Count == 0 || !await dialogs.ConfirmDeleteAsync("awakened item", "awakened items", items.Count)) return;
            await DeleteSelectedItemsAsync(items);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not delete the selected awakened items.");
        }
    }

    [RelayCommand]
    private async Task ListForSale()
    {
        try
        {
            if (SelectedItem is not { } item) return;
            var sale = new LegendarySaleViewModel(item, DefaultInGameName, input => CreateSellOrderAsync(input.PriceSilver, input.InGameName));
            await dialogs.ShowAsync(new LegendarySaleWindow(sale));
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not open the awakened item sale window.");
        }
    }

    [RelayCommand]
    private async Task CancelListing()
    {
        if (!CanCancelSelectedListing) return;
        try
        {
            await CancelSelectedListingAsync();
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not cancel the awakened item listing.");
        }
    }

    [RelayCommand] private void OpenCalculator() => SystemLauncher.TryOpenOptionalUrl(SelectedItem?.CalculatorUrl);

    private async Task SetSoldAsync(bool sold)
    {
        try
        {
            await SetSelectedSoldAsync(sold);
        }
        catch (Exception error)
        {
            Log.Error(error, "Could not change the sold state of the awakened item.");
        }
        // On success the reload replaces the row; on failure this puts the check box back.
        OnPropertyChanged(nameof(SelectedItemSold));
    }
}
