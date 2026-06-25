// Help ▸ About: a small modal showing the app name, version, and the pinned Therion
// source-of-truth, plus a button to open the bundled Therion book (#1).

using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TherionProc.Services;

namespace TherionProc.Views;

public sealed class AboutWindow : Window
{
    public AboutWindow(IThbookDocumentationService? thbook = null)
    {
        Title = "About TherionProc";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        var title = new TextBlock { Text = "TherionProc", FontSize = 22, FontWeight = FontWeight.Bold };
        var ver = new TextBlock { Text = "Version " + version, Foreground = Brushes.Gray };
        var blurb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "An editor and build environment for Therion cave-survey projects.\n" +
                   "Targets Therion v6.4.0 (thbook bundled).",
        };

        var book = new Button { Content = "Open Therion Book", MinWidth = 150 };
        book.Click += (_, _) => thbook?.OpenAtPage(1);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { book, close },
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
            Children = { title, ver, blurb, buttons },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
