// TRUST-04 — a minimal OK/Cancel confirmation for destructive operations (delete, overwrite).
// Returns true only when the user explicitly confirms.

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TherionProc.Views;

public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmLabel = "OK")
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = message };
        var confirm = new Button { Content = confirmLabel, IsDefault = true, MinWidth = 90 };
        confirm.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        cancel.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14,
            Children =
            {
                text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, confirm },
                },
            },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
    }

    public Task<bool> ShowAsync(Window owner) => ShowDialog<bool>(owner);
}
