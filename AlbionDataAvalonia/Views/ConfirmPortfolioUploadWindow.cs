using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Views;

public sealed record PortfolioUploadConfirmationResult(bool? PremiumOverride);

public sealed class ConfirmPortfolioUploadWindow : Window
{
    private readonly CheckBox? _premiumCheckBox;
    private readonly bool? _initialPremiumState;

    public PortfolioUploadConfirmationResult? Result { get; private set; }

    private ConfirmPortfolioUploadWindow(int selectedCount, IReadOnlyList<bool> sellPremiumStatuses)
    {
        var sellCount = sellPremiumStatuses.Count;
        var distinctPremiumStatuses = sellPremiumStatuses.Distinct().ToList();
        var hasMixedPremiumStatuses = distinctPremiumStatuses.Count > 1;
        _initialPremiumState = distinctPremiumStatuses.Count == 1
            ? distinctPremiumStatuses[0]
            : null;

        Title = "Confirm Portfolio upload";
        Width = 500;
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
            Text = "Confirm Portfolio upload",
            FontSize = 18,
            FontWeight = FontWeight.DemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = $"Upload {selectedCount:N0} selected trade{(selectedCount == 1 ? string.Empty : "s")} to Portfolio?",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        if (sellCount > 0)
        {
            _premiumCheckBox = new CheckBox
            {
                Content = $"Premium active for {sellCount:N0} selected sale{(sellCount == 1 ? string.Empty : "s")}",
                IsChecked = _initialPremiumState,
                IsThreeState = hasMixedPremiumStatuses
            };
            root.Children.Add(_premiumCheckBox);

            if (hasMixedPremiumStatuses)
            {
                root.Children.Add(new TextBlock
                {
                    Text = "Warning: the selected sales have different Premium statuses. Choosing checked or unchecked will override every selected sale; leave it indeterminate to preserve each trade's status.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush.Parse("#FFD180")
                });
            }
            else
            {
                root.Children.Add(new TextBlock
                {
                    Text = "Change this setting only if the captured Premium status was incorrect. A change is saved to the selected sale records.",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                });
            }
        }
        else
        {
            root.Children.Add(new TextBlock
            {
                Text = "Premium status applies only to sales; all selected trades are purchases.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelButton_Click;
        buttons.Children.Add(cancelButton);

        var uploadButton = new Button { Content = "Upload" };
        uploadButton.Click += UploadButton_Click;
        buttons.Children.Add(uploadButton);

        root.Children.Add(buttons);
        Content = root;
    }

    public static async Task<PortfolioUploadConfirmationResult?> ShowAsync(
        Window owner,
        int selectedCount,
        IReadOnlyList<bool> sellPremiumStatuses)
    {
        var window = new ConfirmPortfolioUploadWindow(selectedCount, sellPremiumStatuses);
        await window.ShowDialog(owner);
        return window.Result;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UploadButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectedPremiumState = _premiumCheckBox?.IsChecked;
        var premiumOverride = selectedPremiumState != _initialPremiumState
            ? selectedPremiumState
            : null;

        Result = new PortfolioUploadConfirmationResult(premiumOverride);
        Close();
    }
}
