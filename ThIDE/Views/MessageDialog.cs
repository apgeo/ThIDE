// A minimal modal information/warning dialog with a single OK button, used where a
// flow needs to tell the user something went wrong (e.g. an output definition that
// could not be located, or a thconfig that failed to load) instead of failing silently.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace ThIDE.Views;

public sealed class MessageDialog : Window
{
    public MessageDialog(string title, string message)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = message };
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 80,
                              HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14,
            Children = { text, ok },
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
    }

    public Task ShowAsync(Window owner) => ShowDialog(owner);

    /// <summary>Shows the dialog over the application's main window, if one is available.</summary>
    public static Task ShowOverMainAsync(string title, string message)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
            return new MessageDialog(title, message).ShowAsync(main);
        return Task.CompletedTask;
    }
}
