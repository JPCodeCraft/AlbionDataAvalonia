using AlbionDataAvalonia.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Views;

/// <summary>Reopens a window where it was last closed while that spot is on a connected screen, and keeps it on screen.</summary>
internal sealed class WindowPlacementTracker
{
    // Maximizing can move and resize the window just before its state changes; those are not normal bounds.
    private static readonly TimeSpan MaximizeSettle = TimeSpan.FromMilliseconds(500);
    private readonly Window window;
    private PixelPoint position;
    private Size size;
    private (PixelPoint Position, Size Size) settled;
    private DateTime changedAt;
    private bool maximized;
    private bool opened;

    // Without its size, a placement only restores the position; the window keeps its own size.
    public WindowPlacementTracker(Window window, WindowPlacement? saved, bool restoreSize)
    {
        this.window = window;
        if (saved is not null) Apply(saved, restoreSize);
        window.Opened += (_, _) =>
        {
            opened = true;
            maximized = window.WindowState == WindowState.Maximized;
            if (window.WindowState == WindowState.Normal) Remember(window.Position, window.ClientSize);
        };
        window.PositionChanged += (_, args) => Remember(args.Point, window.ClientSize);
        window.PropertyChanged += (_, args) =>
        {
            if (args.Property == TopLevel.ClientSizeProperty) Remember(window.Position, window.ClientSize);
            else if (args.Property == Window.WindowStateProperty) StateChanged();
        };
        if (window.Screens is { } screens) screens.Changed += (_, _) => KeepOnScreen();
    }

    // Null until the window has been shown, so a window that never opened keeps its saved spot.
    public WindowPlacement? Capture()
    {
        if (!opened) return null;
        if (window.WindowState == WindowState.Normal) Remember(window.Position, window.ClientSize);
        return new(position.X, position.Y, Math.Round(size.Width), Math.Round(size.Height), maximized);
    }

    private void Apply(WindowPlacement saved, bool restoreSize)
    {
        var screens = window.Screens?.All.Select(x => new PlacementScreen(x.WorkingArea, x.Scaling, x.IsPrimary)).ToArray() ?? [];
        var current = new Size(double.IsNaN(window.Width) ? 0 : window.Width, double.IsNaN(window.Height) ? 0 : window.Height);
        var plan = Plan(saved, screens, new Size(window.MinWidth, window.MinHeight), current, restoreSize);
        if (plan.Position is { } point)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = point;
        }
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (!restoreSize) return;
        window.Width = plan.Size.Width;
        window.Height = plan.Size.Height;
        // A window reopened maximized keeps these normal bounds until it is restored.
        (position, size) = (plan.Position ?? new PixelPoint(saved.X, saved.Y), plan.Size);
        if (plan.Maximized) window.WindowState = WindowState.Maximized;
    }

    // A saved spot is reused while enough of its top strip lands on a working area; otherwise the
    // window opens centred. A restored size never exceeds the screen it lands on or drops below the minimum.
    internal static PlacementPlan Plan(WindowPlacement saved, IReadOnlyList<PlacementScreen> screens, Size minimum, Size current, bool restoreSize)
    {
        var width = restoreSize && saved.Width > 0 ? saved.Width : current.Width;
        var height = restoreSize && saved.Height > 0 ? saved.Height : current.Height;
        var point = new PixelPoint(saved.X, saved.Y);
        var screen = VisibleScreen(screens, point, width);
        var target = screen ?? screens.FirstOrDefault(x => x.IsPrimary) ?? screens.FirstOrDefault();
        if (target is not null)
        {
            width = Math.Min(width, target.WorkingArea.Width / target.Scaling);
            height = Math.Min(height, target.WorkingArea.Height / target.Scaling);
        }
        width = Math.Max(minimum.Width, width);
        height = Math.Max(minimum.Height, height);
        return new(screen is null ? null : point, new Size(width, height), restoreSize && saved.Maximized);
    }

    // The screen showing at least 100 units across and half of a window's 30-unit top strip at this spot, if any;
    // a window snapped to an edge sits a few pixels past it.
    internal static PlacementScreen? VisibleScreen(IReadOnlyList<PlacementScreen> screens, PixelPoint point, double width) =>
        screens.FirstOrDefault(screen =>
        {
            var strip = new PixelRect(point.X, point.Y, Math.Max(1, (int)(Math.Max(width, 100) * screen.Scaling)), (int)(30 * screen.Scaling));
            var visible = screen.WorkingArea.Intersect(strip);
            return visible.Width >= Math.Min(strip.Width, (int)(100 * screen.Scaling)) && visible.Height >= strip.Height / 2;
        });

    // Unplugging a monitor or changing its resolution can leave the window out of reach.
    private void KeepOnScreen()
    {
        if (!window.IsVisible || window.WindowState != WindowState.Normal || window.Screens is not { } screens) return;
        var all = screens.All.Select(x => new PlacementScreen(x.WorkingArea, x.Scaling, x.IsPrimary)).ToArray();
        if (all.Length == 0 || VisibleScreen(all, window.Position, window.ClientSize.Width) is not null) return;
        var primary = all.FirstOrDefault(x => x.IsPrimary) ?? all[0];
        var area = primary.WorkingArea;
        var pixelWidth = (int)(window.ClientSize.Width * primary.Scaling);
        var pixelHeight = (int)(window.ClientSize.Height * primary.Scaling);
        window.Position = new PixelPoint(area.X + Math.Max(0, (area.Width - pixelWidth) / 2), area.Y + Math.Max(0, (area.Height - pixelHeight) / 2));
    }

    private void Remember(PixelPoint point, Size clientSize)
    {
        if (!opened || window.WindowState != WindowState.Normal || clientSize.Width <= 0 || clientSize.Height <= 0) return;
        var now = DateTime.UtcNow;
        if (now - changedAt > MaximizeSettle) settled = (position, size);
        (position, size, changedAt) = (point, clientSize, now);
    }

    private void StateChanged()
    {
        if (window.WindowState == WindowState.Maximized)
        {
            if (DateTime.UtcNow - changedAt <= MaximizeSettle && settled.Size.Width > 0) (position, size) = settled;
            maximized = true;
        }
        else if (window.WindowState == WindowState.Normal) maximized = false;
    }
}

internal sealed record PlacementScreen(PixelRect WorkingArea, double Scaling, bool IsPrimary);
internal sealed record PlacementPlan(PixelPoint? Position, Size Size, bool Maximized);
