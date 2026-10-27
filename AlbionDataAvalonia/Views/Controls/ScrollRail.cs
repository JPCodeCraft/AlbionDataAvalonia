using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System;

namespace AlbionDataAvalonia.Views.Controls;

/// <summary>
/// A single line of chips that scrolls sideways instead of wrapping. The mouse wheel moves it, the edge that has
/// more content fades out, and the thin scrollbar appears on hover. Put the chip list in its content with a
/// horizontal panel.
/// </summary>
public class ScrollRail : ContentControl
{
    public static readonly DirectProperty<ScrollRail, bool> CanScrollLeftProperty =
        AvaloniaProperty.RegisterDirect<ScrollRail, bool>(nameof(CanScrollLeft), rail => rail.canScrollLeft);

    public static readonly DirectProperty<ScrollRail, bool> CanScrollRightProperty =
        AvaloniaProperty.RegisterDirect<ScrollRail, bool>(nameof(CanScrollRight), rail => rail.canScrollRight);

    private const double WheelStep = 60;
    private ScrollViewer? scroll;
    private bool canScrollLeft;
    private bool canScrollRight;

    /// <summary>True while content is hidden past the left edge.</summary>
    public bool CanScrollLeft
    {
        get => canScrollLeft;
        private set => SetAndRaise(CanScrollLeftProperty, ref canScrollLeft, value);
    }

    /// <summary>True while content is hidden past the right edge.</summary>
    public bool CanScrollRight
    {
        get => canScrollRight;
        private set => SetAndRaise(CanScrollRightProperty, ref canScrollRight, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs args)
    {
        base.OnApplyTemplate(args);
        if (scroll is not null) scroll.PropertyChanged -= OnScrollPropertyChanged;
        scroll = args.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        if (scroll is null) return;
        // Offset, extent and viewport all move the edges; a BringIntoView from inside changes the offset without a wheel.
        scroll.PropertyChanged += OnScrollPropertyChanged;
        UpdateEdges();
    }

    // A vertical wheel over the rail moves it sideways; a horizontal wheel (or a tilt) already does.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs args)
    {
        base.OnPointerWheelChanged(args);
        if (args.Handled || scroll is null || args.Delta.Y == 0 || scroll.Extent.Width <= scroll.Viewport.Width) return;
        var max = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
        scroll.Offset = new Vector(Math.Clamp(scroll.Offset.X - args.Delta.Y * WheelStep, 0, max), scroll.Offset.Y);
        args.Handled = true;
    }

    private void OnScrollPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == ScrollViewer.OffsetProperty || args.Property == ScrollViewer.ExtentProperty || args.Property == ScrollViewer.ViewportProperty) UpdateEdges();
    }

    private void UpdateEdges()
    {
        if (scroll is null) return;
        CanScrollLeft = scroll.Offset.X > 0.5;
        CanScrollRight = scroll.Offset.X + scroll.Viewport.Width < scroll.Extent.Width - 0.5;
    }
}
