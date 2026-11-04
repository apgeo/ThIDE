using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnGesturePreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Once shown, shrink the window to fit the screen it opened on (height first) so its default
    // size never runs off the monitor. The XAML default height is intentionally generous (so the
    // taller sections don't need a scrollbar); this only ever shrinks, never grows, that default.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.ClampToWorkArea();
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
    // Handled on the tunnel pass, before the DataGrid, the TextBox or the window's Esc-closes-Cancel
    // see the key: Esc in a capture field clears the field and must never close Settings.
    private void OnGesturePreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not { DataContext: KeyboardShortcutRow row } box
            || !box.Classes.Contains("gestureCapture")) return;

        // Ignore lone modifier presses — wait for the actual key in the chord.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        // Escape clears the capture box; Back/Delete also clears.
        if (e.Key is Key.Escape or Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            row.Capture(string.Empty);
            e.Handled = true;
            return;
        }

        row.Capture(new KeyGesture(e.Key, e.KeyModifiers).ToString());
        e.Handled = true;
    }
}
