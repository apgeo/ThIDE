using System;
using System.Collections.Generic;
using Avalonia;

namespace ThIDE.Services;

/// <summary>
/// Window-size arithmetic for small screens (1366x768 laptops): pure functions over device-independent
/// pixels, so the view helper stays a thin wrapper and the rules are unit-testable.
/// </summary>
public static class WindowFit
{
    /// <summary>Space left between a clamped window and the edge of the work area.</summary>
    public const double Margin = 24;

    /// <summary>Share of the work area the main window takes on its very first start.</summary>
    public const double FirstRunFraction = 0.85;

    /// <summary>
    /// Shrinks a window so it fits the work area (never grows it). A window never goes below its own
    /// minimum, even when the work area is smaller than that. <paramref name="chromeHeight"/> is the
    /// title bar and borders around the client area, which count against the work area too.
    /// </summary>
    public static (double Width, double Height) Clamp(
        double width, double height, double minWidth, double minHeight, double availWidth, double availHeight,
        double chromeHeight = 0)
    {
        double w = Math.Min(width, Math.Max(minWidth, availWidth - Margin));
        double h = Math.Min(height, Math.Max(minHeight, availHeight - chromeHeight - Margin));
        return (w, h);
    }

    /// <summary>
    /// Moves a window of the given size back inside the work area (left/top are the work area origin).
    /// Returns the new top-left corner.
    /// </summary>
    public static (double X, double Y) Nudge(
        double x, double y, double width, double height,
        double left, double top, double availWidth, double availHeight)
    {
        double nx = Math.Max(left, Math.Min(x, left + availWidth - width));
        double ny = Math.Max(top, Math.Min(y, top + availHeight - height));
        return (nx, ny);
    }

    /// <summary>The size of the main window on a first start: most of the work area, never under its minimum.</summary>
    public static (double Width, double Height) FirstRunSize(
        double minWidth, double minHeight, double availWidth, double availHeight, double chromeHeight = 0)
        => (Math.Max(minWidth, Math.Floor(availWidth * FirstRunFraction)),
            Math.Max(minHeight, Math.Floor(availHeight * FirstRunFraction) - chromeHeight));

    /// <summary>
    /// True when a (restored or about-to-be-saved) window size is plausible: finite and not under the
    /// window's minimum. A degenerate size such as 325x50 (seen after repeated full-screen toggles)
    /// must neither be applied nor written back to the layout.
    /// </summary>
    public static bool IsUsableSize(double width, double height, double minWidth, double minHeight)
        => double.IsFinite(width) && double.IsFinite(height) && width >= minWidth && height >= minHeight;

    /// <summary>
    /// Bottom margin for the toast host so toasts sit above the status bar and inside the visible
    /// work area, even when the window extends past the bottom edge of the screen.
    /// </summary>
    /// <param name="windowBottom">Bottom edge of the window in screen DIPs.</param>
    /// <param name="workAreaBottom">Bottom edge of the work area in screen DIPs.</param>
    /// <param name="statusBarHeight">Height of the status bar the toasts must clear.</param>
    /// <param name="tabStripClearance">
    /// Distance from the bottom of the window to the top of the tool tab strip the toasts must clear
    /// (see <see cref="TabStripClearance"/>); it already includes the status bar, so the larger of the two wins.
    /// </param>
    public static double ToastBottomMargin(double windowBottom, double workAreaBottom, double statusBarHeight,
                                           double tabStripClearance = 0)
    {
        double overflow = Math.Max(0, windowBottom - workAreaBottom);
        return overflow + Math.Max(Math.Max(0, statusBarHeight), Math.Max(0, tabStripClearance)) + 8;
    }

    /// <summary>
    /// How far up from the bottom of a window of height <paramref name="hostHeight"/> the toasts must start
    /// to stay clear of the tool tab strips under them: the distance to the top of the lowest strip that
    /// reaches into the toast column (its right edge is past <paramref name="toastLeft"/>) and sits in the
    /// lower half of the window. Strips are in window coordinates. 0 when none is in the way.
    /// </summary>
    public static double TabStripClearance(double hostHeight, double toastLeft, IEnumerable<Rect> strips)
    {
        Rect? lowest = null;
        foreach (var s in strips)
        {
            if (s.Width <= 0 || s.Height <= 0 || s.Right <= toastLeft || s.Bottom <= hostHeight / 2) continue;
            if (lowest is null || s.Bottom > lowest.Value.Bottom) lowest = s;
        }
        return lowest is { } l ? Math.Max(0, hostHeight - l.Top) : 0;
    }

    /// <summary>
    /// Right and bottom margins for the toast column (<paramref name="toastWidth"/> wide, the lowest
    /// <paramref name="toastBand"/> of it checked) so that no native view covers it. A native view (the
    /// 3D viewer's web view) is drawn above every Avalonia control, toasts included, so a toast under one
    /// could not be read. The column first moves left past the views in its way (a viewer docked on the
    /// right); when that leaves no room in the window it stays at the right and rises above them
    /// instead; when neither fits, the margins are returned unchanged. All in window coordinates.
    /// </summary>
    public static (double Right, double Bottom) ToastClearOfNativeViews(
        Size host, double toastWidth, double toastBand, double right, double bottom, IEnumerable<Rect> nativeViews)
    {
        const double gap = 8;
        var views = new List<Rect>();
        foreach (var v in nativeViews) if (v.Width > 0 && v.Height > 0) views.Add(v);

        bool Covered(double r, double b, out Rect hit)
        {
            double left = host.Width - r - toastWidth, top = host.Height - b - toastBand;
            foreach (var v in views)
                if (v.Right > left && v.Left < host.Width - r && v.Bottom > top && v.Top < host.Height - b) { hit = v; return true; }
            hit = default;
            return false;
        }

        // Each step clears one more view, so this ends after at most one step per view.
        double toLeft = right;
        while (Covered(toLeft, bottom, out var v) && host.Width - toLeft - toastWidth >= 0)
            toLeft = host.Width - v.Left + gap;
        if (host.Width - toLeft - toastWidth >= 0) return (toLeft, bottom);

        double up = bottom;
        while (Covered(right, up, out var v) && host.Height - up - toastBand >= 0)
            up = host.Height - v.Top + gap;
        return host.Height - up - toastBand >= 0 ? (right, up) : (right, bottom);
    }
}
