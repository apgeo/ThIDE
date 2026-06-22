// Minimal modal text-input dialog (code-only, no .axaml) used by the file-explorer
// "New File…/New Folder…" actions to collect a name. Returns the entered string, or
// null when cancelled.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using System.Threading.Tasks;

namespace TherionProc.Views;

public sealed class InputDialog : Window
{
    private readonly TextBox _box;

    public InputDialog(string title, string prompt, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new TextBox { Text = initial, PlaceholderText = prompt };
        _box.AttachedToVisualTree += (_, _) => { _box.Focus(); _box.SelectAll(); };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => Close(string.IsNullOrWhiteSpace(_box.Text) ? null : _box.Text!.Trim());
        cancel.Click += (_, _) => Close(null);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, ok },
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = prompt },
                _box,
                buttons,
            },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    }

    public Task<string?> ShowAsync(Window owner) => ShowDialog<string?>(owner);
}
