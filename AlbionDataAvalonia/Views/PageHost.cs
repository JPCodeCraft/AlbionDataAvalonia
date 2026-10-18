using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Views;

/// <summary>
/// Shows the view for <see cref="Page"/>, built from the host's data templates on the page's first visit. Switching
/// pages hides the old view rather than detaching it, so returning to a page doesn't restyle every control or rebuild
/// its grids' rows, and keeps its scroll position and selection. A hidden page stays attached: a view that pauses
/// while away watches its own <see cref="Visual.IsVisible"/> (or <see cref="Visual.IsEffectivelyVisible"/> deeper in),
/// not attachment.
/// </summary>
public sealed class PageHost : Panel
{
    public static readonly StyledProperty<object?> PageProperty =
        AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

    private readonly Dictionary<object, Control> views = new();

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
        Control? shown = null;
        if (page is not null && !views.TryGetValue(page, out shown) && this.FindDataTemplate(page)?.Build(page) is { } view)
        {
            view.DataContext = page;
            views[page] = shown = view;
            Children.Add(view);
        }
        foreach (var child in Children) child.IsVisible = ReferenceEquals(child, shown);
    }
}
