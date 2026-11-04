// A minimal modal information/warning dialog with an OK button (and, for an error, Copy details), used where a
// flow needs to tell the user something went wrong (e.g. an output definition that
// could not be located, or a thconfig that failed to load) instead of failing silently.

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.Views;

public sealed class MessageDialog : Window
{
    /// <param name="details">Technical text (e.g. an exception with its stack) the user can copy for a bug report;
    /// not shown, so the dialog reads as a sentence. Adds a "Copy details" button when given.</param>
    public MessageDialog(string title, string message, string? details = null)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = message };
        // A long message scrolls inside the dialog, so the buttons stay on screen.
        var scroll = new ScrollViewer
        {
            MaxHeight = 360,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = text,
        };
        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 80,
                              HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => Close();
        AutomationProperties.SetAutomationId(this, "Message.Window");
        AutomationProperties.SetAutomationId(text, "Message.Text");
        AutomationProperties.SetAutomationId(ok, "Message.Ok");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (details is not null)
        {
            var copy = new Button { Content = Tr.Get("Dlg_CopyDetails"), MinWidth = 110 };
            copy.Click += (_, _) => ClipboardHelper.SetText(details);
            AutomationProperties.SetAutomationId(copy, "Message.CopyDetails");
            buttons.Children.Add(copy);
        }
        buttons.Children.Add(ok);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14,
            Children = { scroll, buttons },
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
