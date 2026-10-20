using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AlbionDataAvalonia.Views;

/// <summary>
/// Shows the view for <see cref="Page"/>, built from the host's data templates each time the page opens. A page it
/// leaves is hidden and dropped, so only the open page's controls stay in memory and in the renderer's tree; returning
/// builds it again from its view model. A view drops its own subscriptions to long-lived objects when it leaves the
/// logical tree, or the object keeps it alive.
/// </summary>
public sealed class PageHost : Control
{
    public static readonly StyledProperty<object?> PageProperty =
        AvaloniaProperty.Register<PageHost, object?>(nameof(Page));

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
        if (shown is not null)
        {
            // Hiding first lets a view that pauses while away do so before it goes.
            shown.IsVisible = false;
            VisualChildren.Remove(shown);
            LogicalChildren.Remove(shown);
            shown = null;
        }
        if (page is not null && this.FindDataTemplate(page)?.Build(page) is { } view)
        {
            view.DataContext = page;
            shown = view;
            LogicalChildren.Add(view);
            VisualChildren.Add(view);
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
