using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>A captioned card of <see cref="SettingsRow"/>s, separated by dividers.</summary>
public class SettingsGroup : HeaderedItemsControl
{
    // Rows are their own containers, so they sit directly in the panel and can style the first one.
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not Control;
    }
}
