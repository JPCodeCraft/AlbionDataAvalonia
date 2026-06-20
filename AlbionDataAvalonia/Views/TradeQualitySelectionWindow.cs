using AlbionDataAvalonia.Items;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class TradeQualitySelectionWindow : Window
{
    private int _selectedQuality = 0;

    public int? SelectedQuality { get; private set; }

    private TradeQualitySelectionWindow(int selectedCount)
    {
        Title = "Set trade quality";
        Width = 420;
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
            Text = "Set trade quality",
            FontSize = 18,
            FontWeight = FontWeight.DemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = $"Choose the quality to save for {selectedCount:N0} selected trade{(selectedCount == 1 ? string.Empty : "s")}.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var comboBox = new ComboBox
        {
            ItemsSource = ItemQuality.OptionsWithUnknown,
            SelectedIndex = 0,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ItemQualityOption quality)
            {
                _selectedQuality = quality.Index;
            }
        };
        root.Children.Add(comboBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        var saveButton = new Button { Content = "Save" };
        saveButton.Click += SaveButton_Click;
        buttons.Children.Add(saveButton);

        root.Children.Add(buttons);
        Content = root;
    }

    public static async Task<int?> ShowAsync(Window owner, int selectedCount)
    {
        var window = new TradeQualitySelectionWindow(selectedCount);
        await window.ShowDialog(owner);
        return window.SelectedQuality;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedQuality = _selectedQuality;
        Close();
    }
}
