using AlbionDataAvalonia.Items;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Ui;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Views.Dialogs;

public sealed record PortfolioUploadConfirmationResult(bool? PremiumOverride);

public sealed record PortfolioQualitySelectionGroup(PortfolioTradeQualityKey Key, string ItemName, string ServerName, int Count);

/// <summary>Confirms a Portfolio upload and lets the user correct the Premium status of the selected sales.</summary>
public sealed class PortfolioUploadDialog : AppDialog<PortfolioUploadConfirmationResult>
{
    public PortfolioUploadDialog(int selectedCount, IReadOnlyList<bool> sellPremiumStatuses) : base("Confirm Portfolio upload", 500)
    {
        var sellCount = sellPremiumStatuses.Count;
        var distinct = sellPremiumStatuses.Distinct().ToList();
        bool? initial = distinct.Count == 1 ? distinct[0] : null;

        AddHeading("Confirm Portfolio upload");
        AddMessage($"Upload {UiFormat.Count(selectedCount, "selected trade")} to Portfolio?");

        CheckBox? premium = null;
        if (sellCount > 0)
        {
            premium = Add(new CheckBox
            {
                Content = $"Premium active for {UiFormat.Count(sellCount, "selected sale")}",
                IsChecked = initial,
                IsThreeState = distinct.Count > 1
            });
            if (distinct.Count > 1)
            {
                AddNote("The selected sales have different Premium statuses. Checking or unchecking this overrides every selected sale; leave it mixed to keep each sale's status.", "UiWarning");
            }
            else
            {
                AddNote("Change this only if the captured Premium status was wrong. The change is saved to the selected sale records.");
            }
        }
        else
        {
            AddNote("Premium status applies only to sales; all selected trades are purchases.");
        }

        AddCancelButton();
        AddActionButton("Upload", DialogButtonRole.Primary, () =>
        {
            var chosen = premium?.IsChecked;
            Finish(new PortfolioUploadConfirmationResult(chosen != initial ? chosen : null));
        }, isDefault: true);
    }
}

/// <summary>Sets the quality of the selected trades.</summary>
public sealed class TradeQualityDialog : AppDialog<int?>
{
    public TradeQualityDialog(int selectedCount) : base("Set trade quality", 420)
    {
        AddHeading("Set trade quality");
        AddMessage($"Choose the quality to save for {UiFormat.Count(selectedCount, "selected trade")}.");
        var picker = Add(QualityPicker.Create(ItemQuality.OptionsWithUnknown, "Trade quality"));
        picker.HorizontalAlignment = HorizontalAlignment.Left;
        AddCancelButton();
        AddActionButton("Save", DialogButtonRole.Primary,
            () => Finish((picker.SelectedItem as ItemQualityOption)?.Index ?? 0), isDefault: true);
    }
}

/// <summary>Picks a Portfolio quality for each group of trades whose quality is unknown.</summary>
public sealed class PortfolioQualityDialog : AppDialog<IReadOnlyDictionary<PortfolioTradeQualityKey, int>>
{
    public PortfolioQualityDialog(IReadOnlyList<PortfolioQualitySelectionGroup> groups) : base("Select Portfolio quality", 620)
    {
        AddHeading("Select Portfolio quality");
        AddMessage("Some selected trades have Unknown quality. Choose the Portfolio quality to use for each item group.");

        var pickers = new List<(PortfolioQualitySelectionGroup Group, ComboBox Picker)>();
        var rows = new StackPanel { Spacing = 8 };
        foreach (var group in groups)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,170") };
            row.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = group.ItemName, Classes = { "UiRowTitle" } },
                    new TextBlock { Text = $"{group.ServerName} · {UiFormat.Count(group.Count, "trade")}", Classes = { "UiLabel" } }
                }
            });
            var picker = QualityPicker.Create(ItemQuality.Options, $"Quality for {group.ItemName}");
            picker.HorizontalAlignment = HorizontalAlignment.Right;
            picker.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(picker, 1);
            row.Children.Add(picker);
            rows.Children.Add(row);
            pickers.Add((group, picker));
        }
        Add(new ScrollViewer { Content = rows, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        AddCancelButton();
        AddActionButton("Continue", DialogButtonRole.Primary, () => Finish(pickers.ToDictionary(
            entry => entry.Group.Key,
            entry => (entry.Picker.SelectedItem as ItemQualityOption)?.Index ?? 1)), isDefault: true);
    }
}

internal static class QualityPicker
{
    public static ComboBox Create(IEnumerable<ItemQualityOption> options, string name)
    {
        var picker = new ComboBox { ItemsSource = options, SelectedIndex = 0, Width = 170 };
        Avalonia.Automation.AutomationProperties.SetName(picker, name);
        return picker;
    }
}
