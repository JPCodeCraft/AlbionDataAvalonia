using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;

namespace AlbionDataAvalonia.Views;

/// <summary>
/// A compact single-choice selector with native selection and keyboard navigation.
/// </summary>
public sealed partial class ActivitySegmentedSelector : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    public ActivitySegmentedSelector()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
