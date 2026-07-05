// Minimal modal text-input dialog (code-only, no .axaml) used by the file-explorer
// "New File…/New Folder…" actions to collect a name. Returns the entered string, or
// null when cancelled.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using System.Threading.Tasks;
using TherionProc.Resources;

namespace TherionProc.Views;

public sealed class InputDialog : Window
{
    private readonly TextBox _box;

    /// <summary>True when the dialog was closed via the optional "See occurrences" button (rename flow):
    /// the entered name is still returned, but the caller should open the occurrences preview instead of
    /// applying directly.</summary>
    public bool SeeOccurrencesRequested { get; private set; }

    public InputDialog(string title, string prompt, string initial, string? seeOccurrencesText = null)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new TextBox { Text = initial, PlaceholderText = prompt };
        _box.AttachedToVisualTree += (_, _) => { _box.Focus(); _box.SelectAll(); };

        string? Value() => string.IsNullOrWhiteSpace(_box.Text) ? null : _box.Text!.Trim();

        var ok = new Button { Content = Tr.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = Tr.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => Close(Value());
        cancel.Click += (_, _) => Close(null);

        var rightButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, ok },
        };

        Control buttons = rightButtons;
        if (seeOccurrencesText is { Length: > 0 } seeText)
        {
            // A left-aligned "See occurrences" button: returns the name but flags the preview should open.
            var see = new Button { Content = seeText, MinWidth = 80 };
            see.Click += (_, _) => { SeeOccurrencesRequested = true; Close(Value()); };
            var bar = new DockPanel();
            DockPanel.SetDock(see, Avalonia.Controls.Dock.Left);
            bar.Children.Add(see);
            bar.Children.Add(rightButtons);
            buttons = bar;
        }

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
