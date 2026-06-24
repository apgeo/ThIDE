// Shown when "Edit with Mapiah" is clicked but the Mapiah executable can't be detected.
// Points the user at the download page and the Settings → External Tools path override.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace TherionProc.Views;

public enum MapiahNotFoundChoice { Cancel, Download, OpenSettings }

public sealed class MapiahNotFoundDialog : Window
{
    public MapiahNotFoundDialog()
    {
        Title = "Mapiah not found";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "The Mapiah sketch editor could not be found on this computer.\n\n" +
                   "Install Mapiah from its releases page, then set the path to mapiah.exe " +
                   "in Settings → External Tools (the \"mapiah\" row).",
        };

        var download = new Button { Content = "Download Mapiah…", MinWidth = 130 };
        var settings = new Button { Content = "Open Settings", MinWidth = 110 };
        var cancel = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        download.Click += (_, _) => Close(MapiahNotFoundChoice.Download);
        settings.Click += (_, _) => Close(MapiahNotFoundChoice.OpenSettings);
        cancel.Click += (_, _) => Close(MapiahNotFoundChoice.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, settings, download },
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14,
            Children = { message, buttons },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(MapiahNotFoundChoice.Cancel); };
    }

    public Task<MapiahNotFoundChoice> ShowAsync(Window owner) => ShowDialog<MapiahNotFoundChoice>(owner);
}
