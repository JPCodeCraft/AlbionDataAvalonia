using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Views;

/// <summary>
/// Shows the view for <see cref="Page"/>, built from the host's data templates on the page's first visit. A page it
/// leaves stays a logical child, so returning to it doesn't restyle every control and keeps its scroll position and
/// selection, but it leaves the visual tree and is hidden: the renderer walks every attached visual each frame, hidden
/// or not, so keeping every visited page attached made each frame cost as much as all of them. A view that pauses
/// while away watches its own <see cref="Visual.IsVisible"/> (or <see cref="Visual.IsEffectivelyVisible"/> deeper in)
/// or its visual tree attachment.
/// </summary>
public sealed class PageHost : Control
{
    public static readonly StyledProperty<object?> PageProperty =
        AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

    private readonly Dictionary<object, Control> views = new();
    private Control? shown;

    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty) Show(change.NewValue);
    }

    private void Show(object? page)
    {
        Control? view = null;
        if (page is not null && !views.TryGetValue(page, out view) && this.FindDataTemplate(page)?.Build(page) is { } built)
        {
            built.DataContext = page;
            views[page] = view = built;
            LogicalChildren.Add(built);
        }
        if (ReferenceEquals(view, shown)) return;
        if (shown is not null)
        {
            shown.IsVisible = false;
            VisualChildren.Remove(shown);
        }
        shown = view;
        if (shown is not null)
        {
            shown.IsVisible = true;
            VisualChildren.Add(shown);
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (shown is null) return default;
        shown.Measure(availableSize);
        return shown.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        shown?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
