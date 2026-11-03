using System;

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
    public static double ToastBottomMargin(double windowBottom, double workAreaBottom, double statusBarHeight)
    {
        double overflow = Math.Max(0, windowBottom - workAreaBottom);
        return overflow + Math.Max(0, statusBarHeight) + 8;
    }
}
