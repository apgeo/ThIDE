using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Once shown, shrink the window to fit the screen it opened on (height first) so its default
    // size never runs off the monitor. The XAML default height is intentionally generous (so the
    // taller sections don't need a scrollbar); this only ever shrinks, never grows, that default.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampToCurrentScreen();
    }

    private void ClampToCurrentScreen()
    {
        var screen = Screens?.ScreenFromWindow(this)
                     ?? Screens?.ScreenFromPoint(Position)
                     ?? Screens?.Primary;
        if (screen is null) return;

        double scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
        // WorkingArea excludes the taskbar; convert its physical pixels to the window's DIPs.
        double availW = screen.WorkingArea.Width / scale;
        double availH = screen.WorkingArea.Height / scale;
        const double margin = 24;

        double w = Math.Min(Width, Math.Max(MinWidth, availW - margin));
        double h = Math.Min(Height, Math.Max(MinHeight, availH - margin));
        if (h < Height) Height = h;   // height especially: keep it inside the screen bounds
        if (w < Width) Width = w;

        // Nudge back on-screen if centring pushed an edge past the working area.
        double left = screen.WorkingArea.X / scale, top = screen.WorkingArea.Y / scale;
        double x = Math.Max(left, Math.Min(Position.X / scale, left + availW - Width));
        double y = Math.Max(top, Math.Min(Position.Y / scale, top + availH - Height));
        Position = new Avalonia.PixelPoint((int)(x * scale), (int)(y * scale));
    }

    // Esc closes the window (Cancel). A child that consumes Esc first — e.g. the gesture-capture
    // field, which uses it to clear a binding — marks the event Handled, so it won't close then.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        (DataContext as PreferencesViewModel)?.Apply();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    // Keyboard-shortcut capture (#11): pressing a chord in a gesture field records it as the
    // new gesture for that command, instead of forcing the user to type gesture text by hand.
    private void OnGestureKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: KeyboardShortcutRow row }) return;

        // Ignore lone modifier presses — wait for the actual key in the chord.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        // Escape clears the binding; Back/Delete also clears.
        if (e.Key is Key.Escape or Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            row.Gesture = string.Empty;
            e.Handled = true;
            return;
        }

        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        row.Gesture = gesture.ToString();
        e.Handled = true;
    }
}
