using AlbionDataAvalonia.Network.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Globalization;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public partial class CsvExportOptionsWindow : Window
{
    public CsvExportOptions? SelectedOptions { get; private set; }

    public CsvExportOptionsWindow()
    {
        InitializeComponent();

        var defaults = CsvExportOptions.FromCurrentCulture();
        SeparatorTextBox.Text = defaults.Delimiter;
        DecimalSymbolTextBox.Text = defaults.DecimalSeparator;
        DefaultsTextBlock.Text =
            $"Detected from {CultureInfo.CurrentCulture.DisplayName}: separator '{DisplayToken(defaults.Delimiter)}', decimal '{DisplayToken(defaults.DecimalSeparator)}'.";

        UpdatePreview();
    }

    public static async Task<CsvExportOptions?> ShowAsync(Window owner)
    {
        var window = new CsvExportOptionsWindow();
        await window.ShowDialog(owner);
        return window.SelectedOptions;
    }

    private void Input_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ValidationTextBlock.IsVisible = false;
        UpdatePreview();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryCreateOptions(out var options, out var errorMessage))
        {
            ValidationTextBlock.Text = errorMessage;
            ValidationTextBlock.IsVisible = true;
            return;
        }

        SelectedOptions = options;
        Close();
    }

    private bool TryCreateOptions(out CsvExportOptions? options, out string errorMessage)
    {
        var delimiter = SeparatorTextBox.Text ?? string.Empty;
        var decimalSeparator = DecimalSymbolTextBox.Text ?? string.Empty;

        if (string.IsNullOrEmpty(delimiter))
        {
            options = null;
            errorMessage = "Separator is required.";
            return false;
        }

        if (string.IsNullOrEmpty(decimalSeparator))
        {
            options = null;
            errorMessage = "Decimal symbol is required.";
            return false;
        }

        if (delimiter == decimalSeparator)
        {
            options = null;
            errorMessage = "Separator and decimal symbol must be different.";
            return false;
        }

        options = new CsvExportOptions(delimiter, decimalSeparator);
        errorMessage = string.Empty;
        return true;
    }

    private void UpdatePreview()
    {
        var delimiter = SeparatorTextBox.Text;
        var decimalSeparator = DecimalSymbolTextBox.Text;

        if (string.IsNullOrEmpty(delimiter) || string.IsNullOrEmpty(decimalSeparator))
        {
            PreviewTextBlock.Text = "Preview: enter both values to see an example.";
            return;
        }

        PreviewTextBlock.Text =
            $"Preview: Server{delimiter}Item{delimiter}1234{decimalSeparator}56";
    }

    private static string DisplayToken(string value)
    {
        return value switch
        {
            "\t" => "\\t",
            " " => "space",
            _ => value
        };
    }
}
