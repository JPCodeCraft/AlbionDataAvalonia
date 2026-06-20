using AlbionDataAvalonia.Network.Models;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed class CleanupSelectionWindow : Window
{
    private readonly ComboBox _comboBox;
    private readonly Button _continueButton;
    private bool _confirmed;

    public CleanupCountOption? SelectedOption { get; private set; }

    private CleanupSelectionWindow(string itemNamePlural, CleanupPreview preview)
    {
        Title = $"Cleanup {itemNamePlural}";
        Width = 460;
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
            Text = $"Cleanup {itemNamePlural}",
            FontSize = 18,
            FontWeight = FontWeight.DemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = $"You have {preview.TotalCount:N0} active {itemNamePlural}. Choose which old {itemNamePlural} to delete.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        _comboBox = new ComboBox
        {
            ItemsSource = preview.Options,
            SelectedIndex = preview.Options.Count > 0 ? 0 : -1,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _comboBox.SelectionChanged += ComboBox_SelectionChanged;
        root.Children.Add(_comboBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        _continueButton = new Button { Content = "Continue" };
        _continueButton.Click += ContinueButton_Click;
        buttons.Children.Add(_continueButton);

        root.Children.Add(buttons);
        Content = root;

        SelectedOption = preview.Options.FirstOrDefault();
        UpdateContinueButton();
    }

    public static async Task<CleanupCountOption?> ShowAsync(
        Window owner,
        string itemNamePlural,
        CleanupPreview preview)
    {
        var window = new CleanupSelectionWindow(itemNamePlural, preview);
        await window.ShowDialog(owner);
        return window._confirmed ? window.SelectedOption : null;
    }

    private void ComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SelectedOption = _comboBox.SelectedItem as CleanupCountOption;
        UpdateContinueButton();
    }

    private void UpdateContinueButton()
    {
        _continueButton.IsEnabled = SelectedOption?.Count > 0;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }
}
