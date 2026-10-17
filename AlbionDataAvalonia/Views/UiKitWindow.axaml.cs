using AlbionDataAvalonia.Ui;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Views;

public sealed record UiKitSwatch(string Key, IBrush Brush);

/// <summary>A page of every token and shared control, for checking the look after a theme change.</summary>
public partial class UiKitWindow : Window
{
    public UiKitWindow()
    {
        InitializeComponent();
        DataContext = this;
        Swatches.ItemsSource = ColorTokens().ToList();
    }

    public IReadOnlyList<Option<int>> Choices { get; } = [new(0, "First"), new(1, "Second"), new(2, "Third")];

    // The tokens dictionary is the one that defines UiTextColor.
    private static IEnumerable<UiKitSwatch> ColorTokens()
    {
        var tokens = Application.Current?.Resources.MergedDictionaries
            .Select(provider => provider is ResourceInclude include ? include.Loaded : provider as IResourceDictionary)
            .FirstOrDefault(dictionary => dictionary?.ContainsKey("UiTextColor") == true);
        if (tokens is null) yield break;
        foreach (var key in tokens.Keys.OfType<string>().Where(key => key.StartsWith("Ui", System.StringComparison.Ordinal)).Order())
        {
            switch (tokens[key])
            {
                case Color color:
                    yield return new(key, UiPalette.Brush(color));
                    break;
                case IBrush brush:
                    yield return new(key, brush);
                    break;
            }
        }
    }
}
