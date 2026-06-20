using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Items;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed record PortfolioQualitySelectionGroup(
    PortfolioTradeQualityKey Key,
    string ItemName,
    string ServerName,
    int Count);

public sealed class PortfolioQualitySelectionWindow : Window
{
    private readonly List<PortfolioQualitySelectionRow> _rows;

    public IReadOnlyDictionary<PortfolioTradeQualityKey, int>? SelectedQualities { get; private set; }

    private PortfolioQualitySelectionWindow(IReadOnlyList<PortfolioQualitySelectionGroup> groups)
    {
        _rows = groups.Select(group => new PortfolioQualitySelectionRow(group)).ToList();

        Title = "Select portfolio quality";
        Width = 620;
        MinHeight = 240;
        MaxHeight = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14
        };

        root.Children.Add(new TextBlock
        {
            Text = "Select portfolio quality",
            FontSize = 18,
            FontWeight = FontWeight.DemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = "Some selected trades have Unknown quality. Choose the portfolio quality to use for each item group.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var rowsPanel = new StackPanel { Spacing = 8 };
        foreach (var row in _rows)
        {
            rowsPanel.Children.Add(CreateRow(row));
        }

        root.Children.Add(new ScrollViewer
        {
            Content = rowsPanel,
            MaxHeight = 420
        });

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

    public static async Task<IReadOnlyDictionary<PortfolioTradeQualityKey, int>?> ShowAsync(
        Window owner,
        IReadOnlyList<PortfolioQualitySelectionGroup> groups)
    {
        var window = new PortfolioQualitySelectionWindow(groups);
        await window.ShowDialog(owner);
        return window.SelectedQualities;
    }

    private static Control CreateRow(PortfolioQualitySelectionRow row)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,170"),
            Margin = new Avalonia.Thickness(0, 0, 0, 4)
        };

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = row.Label,
            FontWeight = FontWeight.DemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = row.Detail,
            FontSize = 12,
            Opacity = 0.7
        });
        grid.Children.Add(text);

        var comboBox = new ComboBox
        {
            ItemsSource = ItemQuality.Options,
            SelectedIndex = 0,
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ItemQualityOption quality)
            {
                row.SelectedQuality = quality.Index;
            }
        };

        Grid.SetColumn(comboBox, 1);
        grid.Children.Add(comboBox);

        return grid;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedQualities = _rows.ToDictionary(row => row.Group.Key, row => row.SelectedQuality);
        Close();
    }

    private sealed class PortfolioQualitySelectionRow
    {
        public PortfolioQualitySelectionRow(PortfolioQualitySelectionGroup group)
        {
            Group = group;
        }

        public PortfolioQualitySelectionGroup Group { get; }
        public int SelectedQuality { get; set; } = 1;
        public string Label => Group.ItemName;
        public string Detail => $"{Group.ServerName} - {Group.Count:N0} trade{(Group.Count == 1 ? string.Empty : "s")}";
    }
}
