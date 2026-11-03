using System;
using Avalonia;
using Avalonia.Controls;
using ThIDE.Services;

namespace ThIDE.Views;

/// <summary>View glue for <see cref="WindowFit"/>: asks the window's screen for its work area.</summary>
public static class WindowFitting
{
    /// <summary>
    /// Shrinks <paramref name="window"/> to fit the screen it opened on and nudges it back inside the
    /// work area, so its default size never runs off a small monitor. Only ever shrinks.
    /// </summary>
    public static void ClampToWorkArea(this Window window)
    {
        var screen = window.Screens?.ScreenFromWindow(window)
                     ?? window.Screens?.ScreenFromPoint(window.Position)
                     ?? window.Screens?.Primary;
        if (screen is null || double.IsNaN(window.Width) || double.IsNaN(window.Height)) return;

        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        // WorkingArea excludes the taskbar; convert its physical pixels to the window's DIPs.
        double availW = screen.WorkingArea.Width / scale;
        double availH = screen.WorkingArea.Height / scale;

        double chrome = ChromeHeight(window);
        var (w, h) = WindowFit.Clamp(window.Width, window.Height, window.MinWidth, window.MinHeight, availW, availH, chrome);
        if (h < window.Height) window.Height = h;
        if (w < window.Width) window.Width = w;

        // Nudge back on-screen if centring pushed an edge past the working area. Position is the
        // client area's corner, so the title bar sits above it.
        var (x, y) = WindowFit.Nudge(
            window.Position.X / scale, window.Position.Y / scale - chrome, window.Width, window.Height + chrome,
            screen.WorkingArea.X / scale, screen.WorkingArea.Y / scale, availW, availH);
        window.Position = new PixelPoint((int)(x * scale), (int)((y + chrome) * scale));
    }

    /// <summary>
    /// Height of the title bar and borders around the window's client area: measured from the frame
    /// when the platform reports it, otherwise a typical title bar.
    /// </summary>
    public static double ChromeHeight(Window window)
    {
        const double typicalTitleBar = 32;
        if (window.FrameSize is { } frame && window.Bounds.Height > 0)
        {
            double measured = frame.Height - window.Bounds.Height;
            if (measured >= 0 && measured < 120) return measured;
        }
        return typicalTitleBar;
    }

    /// <summary>Clamps the window to the screen once it is shown.</summary>
    public static void ClampOnOpen(this Window window) => window.Opened += (_, _) => window.ClampToWorkArea();

    /// <summary>Moves keyboard focus to the named control when the window opens, so typing works at once.</summary>
    public static void FocusOnOpen(this Window window, string controlName) =>
        window.Opened += (_, _) => window.FindControl<Control>(controlName)?.Focus();
}
