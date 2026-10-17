using AlbionDataAvalonia.Network.Services;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using System.Globalization;

namespace AlbionDataAvalonia.Views.Dialogs;

/// <summary>Chooses the separator and decimal symbol for a CSV export, starting from the system's settings.</summary>
public sealed class CsvExportDialog : AppDialog<CsvExportOptions>
{
    private readonly TextBox separator;
    private readonly TextBox decimalSymbol;
    private readonly TextBlock preview;
    private readonly TextBlock error;

    public CsvExportDialog() : base("Export to CSV", 420)
    {
        var defaults = CsvExportOptions.FromCurrentCulture();
        AddHeading("Export to CSV");
        AddMessage("Choose the CSV separator and decimal symbol for this export.");

        preview = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Add(new Border
        {
            Classes = { "UiBanner", "Info" },
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = $"Detected from {CultureInfo.CurrentCulture.DisplayName}: separator '{DisplayToken(defaults.Delimiter)}', decimal '{DisplayToken(defaults.DecimalSeparator)}'."
                    },
                    preview
                }
            }
        });

        separator = Field("Separator", defaults.Delimiter, ",");
        decimalSymbol = Field("Decimal symbol", defaults.DecimalSeparator, ".");
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        fields.Children.Add(Labelled("Separator", separator));
        var right = Labelled("Decimal symbol", decimalSymbol);
        Grid.SetColumn(right, 1);
        fields.Children.Add(right);
        Add(fields);

        error = AddNote("", "UiError");
        error.IsVisible = false;

        AddCancelButton();
        AddActionButton("Continue", DialogButtonRole.Primary, Continue, isDefault: true);
        UpdatePreview();
    }

    private TextBox Field(string name, string value, string watermark)
    {
        var box = new TextBox { Text = value, MaxLength = 4, MinWidth = 120, Watermark = watermark };
        AutomationProperties.SetName(box, name);
        box.TextChanged += (_, _) =>
        {
            error.IsVisible = false;
            UpdatePreview();
        };
        return box;
    }

    private static StackPanel Labelled(string label, Control field) =>
        new() { Spacing = 4, Children = { new TextBlock { Text = label, Classes = { "UiLabel" } }, field } };

    private void Continue()
    {
        var delimiter = separator.Text ?? "";
        var decimalSeparator = decimalSymbol.Text ?? "";
        var problem = string.IsNullOrEmpty(delimiter) ? "Enter a separator."
            : string.IsNullOrEmpty(decimalSeparator) ? "Enter a decimal symbol."
            : delimiter == decimalSeparator ? "Use a different separator and decimal symbol." : null;
        if (problem is not null)
        {
            error.Text = problem;
            error.IsVisible = true;
            return;
        }

        Finish(new CsvExportOptions(delimiter, decimalSeparator));
    }

    private void UpdatePreview()
    {
        var delimiter = separator.Text;
        var decimalSeparator = decimalSymbol.Text;
        preview.Text = string.IsNullOrEmpty(delimiter) || string.IsNullOrEmpty(decimalSeparator)
            ? "Preview: enter both values to see an example."
            : $"Preview: Server{delimiter}Item{delimiter}1234{decimalSeparator}56";
    }

    private static string DisplayToken(string value) => value switch
    {
        "\t" => "\\t",
        " " => "space",
        _ => value
    };
}
