using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
