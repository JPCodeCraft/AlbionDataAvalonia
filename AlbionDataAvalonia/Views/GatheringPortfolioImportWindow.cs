using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Locations.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed record GatheringPortfolioImportItemInput(
    Guid DataClientId,
    string ItemName,
    long Amount,
    int QualityIndex,
    long? EstimatedMarketValue);

public sealed record GatheringPortfolioImportSelection(
    int LocationIndex,
    IReadOnlyDictionary<Guid, double> UnitPrices);

public sealed class GatheringPortfolioImportWindow : Window
{
    private readonly ComboBox _locationComboBox;
    private readonly TextBlock _validationError;
    private readonly IReadOnlyList<GatheringPortfolioImportRow> _rows;

    private GatheringPortfolioImportSelection? _selection;

    private GatheringPortfolioImportWindow(
        IReadOnlyList<GatheringPortfolioImportItemInput> items,
        IReadOnlyList<AlbionLocation> locations)
    {
        _rows = items.Select(item => new GatheringPortfolioImportRow(item)).ToArray();

        Title = "Add gathering session to Portfolio";
        Width = 760;
        MinHeight = 360;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14
        };

        root.Children.Add(new TextBlock
        {
            Text = "Add gathering session to Portfolio",
            FontSize = 18,
            FontWeight = FontWeight.DemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = "Choose one market location for this session and review the unit price for every gathered item.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var locationPanel = new StackPanel { Spacing = 6 };
        locationPanel.Children.Add(new TextBlock
        {
            Text = "Market location",
            FontWeight = FontWeight.DemiBold
        });

        _locationComboBox = new ComboBox
        {
            ItemsSource = locations.Select(location => new MarketLocationOption(location)).ToArray(),
            SelectedIndex = -1,
            PlaceholderText = "Select a market location",
            IsTextSearchEnabled = true,
            MinWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        locationPanel.Children.Add(_locationComboBox);
        root.Children.Add(locationPanel);

        root.Children.Add(CreateItemsHeader());

        var rowsPanel = new StackPanel { Spacing = 8 };
        foreach (var row in _rows)
        {
            rowsPanel.Children.Add(CreateItemRow(row));
        }

        root.Children.Add(new ScrollViewer
        {
            Content = rowsPanel,
            MaxHeight = 400,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });

        _validationError = new TextBlock
        {
            Foreground = Brushes.IndianRed,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        root.Children.Add(_validationError);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        var continueButton = new Button { Content = "Continue" };
        continueButton.Click += ContinueButton_Click;
        buttons.Children.Add(continueButton);

        root.Children.Add(buttons);
        Content = root;
    }

    public static async Task<GatheringPortfolioImportSelection?> ShowAsync(
        Window owner,
        IReadOnlyList<GatheringPortfolioImportItemInput> items,
        IReadOnlyList<AlbionLocation> locations)
    {
        var window = new GatheringPortfolioImportWindow(items, locations);
        await window.ShowDialog(owner);
        return window._selection;
    }

    private static Control CreateItemsHeader()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,110,120,170"),
            Margin = new Avalonia.Thickness(0, 2, 0, 0)
        };

        grid.Children.Add(CreateHeaderText("Item", HorizontalAlignment.Left));

        var amount = CreateHeaderText("Amount", HorizontalAlignment.Right);
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);

        var quality = CreateHeaderText("Quality", HorizontalAlignment.Left);
        Grid.SetColumn(quality, 2);
        grid.Children.Add(quality);

        var unitPrice = CreateHeaderText("Unit price", HorizontalAlignment.Left);
        Grid.SetColumn(unitPrice, 3);
        grid.Children.Add(unitPrice);

        return grid;
    }

    private static TextBlock CreateHeaderText(string text, HorizontalAlignment alignment)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.DemiBold,
            HorizontalAlignment = alignment,
            Margin = new Avalonia.Thickness(4, 0)
        };
    }

    private static Control CreateItemRow(GatheringPortfolioImportRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,110,120,170"),
            VerticalAlignment = VerticalAlignment.Center
        };

        grid.Children.Add(new TextBlock
        {
            Text = row.Input.ItemName,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(4, 0)
        });

        var amount = new TextBlock
        {
            Text = row.Input.Amount.ToString("N0", CultureInfo.CurrentCulture),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(4, 0)
        };
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);

        var quality = new TextBlock
        {
            Text = ItemQuality.Format(row.Input.QualityIndex),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(4, 0)
        };
        Grid.SetColumn(quality, 2);
        grid.Children.Add(quality);

        Grid.SetColumn(row.UnitPriceTextBox, 3);
        grid.Children.Add(row.UnitPriceTextBox);

        return grid;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_locationComboBox.SelectedItem is not MarketLocationOption locationOption
            || locationOption.Location.IdInt is not int locationIndex
            || locationIndex < 0)
        {
            ShowValidationError("Select a market location.");
            return;
        }

        var unitPrices = new Dictionary<Guid, double>(_rows.Count);
        foreach (var row in _rows)
        {
            var rawUnitPrice = row.UnitPriceTextBox.Text;
            if (string.IsNullOrWhiteSpace(rawUnitPrice))
            {
                ShowValidationError($"Enter a unit price for {row.Input.ItemName}.");
                row.UnitPriceTextBox.Focus();
                return;
            }

            if (!double.TryParse(
                    rawUnitPrice,
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var unitPrice)
                || !double.IsFinite(unitPrice)
                || unitPrice < 0)
            {
                ShowValidationError(
                    $"Enter a valid nonnegative unit price for {row.Input.ItemName} using your current number format.");
                row.UnitPriceTextBox.Focus();
                return;
            }

            unitPrices[row.Input.DataClientId] = unitPrice;
        }

        _selection = new GatheringPortfolioImportSelection(locationIndex, unitPrices);
        Close();
    }

    private void ShowValidationError(string message)
    {
        _validationError.Text = message;
        _validationError.IsVisible = true;
    }

    private sealed class GatheringPortfolioImportRow
    {
        public GatheringPortfolioImportRow(GatheringPortfolioImportItemInput input)
        {
            Input = input;
            UnitPriceTextBox = new TextBox
            {
                Text = input.EstimatedMarketValue?.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty,
                Watermark = "Required",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Avalonia.Thickness(4, 0)
            };
        }

        public GatheringPortfolioImportItemInput Input { get; }
        public TextBox UnitPriceTextBox { get; }
    }

    private sealed class MarketLocationOption
    {
        public MarketLocationOption(AlbionLocation location)
        {
            Location = location;
        }

        public AlbionLocation Location { get; }

        public override string ToString() => Location.FriendlyName;
    }
}
