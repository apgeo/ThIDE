// Help ▸ About (#1): app name/version, the source repository, bundled-component versions
// (CaveView.js), detected external-tool versions (Therion / Survex / Mapiah) with links to their
// pages (shown even when a tool isn't installed), the local OS context, and a Copy-info button.
// Environment/tool data comes from AppEnvironmentInfo (no personal identifiers).

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Therion.Processing.Abstractions;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.Views;

public sealed class AboutWindow : Window
{
    private readonly Dictionary<string, TextBlock> _toolStatus = new(StringComparer.OrdinalIgnoreCase);
    private string _reportText = string.Empty;

    public AboutWindow(IThbookDocumentationService? thbook = null)
    {
        Title = Tr.Get("About_Title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var title = new TextBlock { Text = "ThIDE", FontSize = 22, FontWeight = FontWeight.Bold };
        var ver = new TextBlock { Text = Tr.Get("About_VersionPrefix") + AppEnvironmentInfo.AppVersion(), Foreground = Brushes.Gray };
        var blurb = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = Tr.Get("About_Blurb") };

        var repoRow = LabeledLink(Tr.Get("About_GitHub"), "github.com/apgeo/ThIDE", AppEnvironmentInfo.RepositoryUrl);
        var projectByRow = LabeledLink(Tr.Get("About_ProjectByPrefix"), "SpeoSilex", "https://speosilex.ro/");

        // Bundled components.
        var bundledHeader = Header(Tr.Get("About_Bundled"));
        var caveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        caveRow.Children.Add(new TextBlock { Text = $"CaveView.js  v{AppEnvironmentInfo.CaveViewVersion()}", VerticalAlignment = VerticalAlignment.Center });
        caveRow.Children.Add(Link("↗", AppEnvironmentInfo.CaveViewUrl));

        // External tools — one row each, always with a link, status filled in asynchronously.
        var toolsHeader = Header(Tr.Get("About_ExternalTools"));
        var toolsPanel = new StackPanel { Spacing = 3 };
        foreach (var (id, name, url) in AppEnvironmentInfo.KnownTools)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Link(name, url));
            var status = new TextBlock { Text = Tr.Get("About_Checking"), Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
            _toolStatus[id] = status;
            row.Children.Add(status);
            toolsPanel.Children.Add(row);
        }

        // Local OS context (compact — the full detail is in the Copy-info blob / Debug Info window).
        var sysHeader = Header(Tr.Get("About_System"));
        var sysLines = AppEnvironmentInfo.SystemLines();
        var sysPanel = new StackPanel { Spacing = 1 };
        foreach (var line in new[] { sysLines[0], sysLines[3], sysLines[4] })
            sysPanel.Children.Add(new TextBlock { Text = line, Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap });

        var book = new Button { Content = Tr.Get("About_OpenBook"), MinWidth = 150 };
        book.Click += (_, _) => thbook?.OpenAtPage(1);
        var copy = new Button { Content = Tr.Get("About_CopyInfo"), MinWidth = 100 };
        copy.Click += async (_, _) => { if (Clipboard is { } cb && _reportText.Length > 0) await cb.SetTextAsync(_reportText); };
        var close = new Button { Content = Tr.Get("Common_Close"), IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { book, copy, close },
        };

        Content = new ScrollViewer
        {
            MaxHeight = 640,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 10,
                Children =
                {
                    title, ver, blurb, repoRow, projectByRow,
                    bundledHeader, caveRow,
                    toolsHeader, toolsPanel,
                    sysHeader, sysPanel,
                    buttons,
                },
            },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += async (_, _) => await PopulateAsync();
    }

    private async System.Threading.Tasks.Task PopulateAsync()
    {
        IExternalToolLocator? locator = null;
        try { locator = AppServices.Provider.GetService<IExternalToolLocator>(); } catch { /* design-time */ }
        var tools = await AppEnvironmentInfo.DetectToolsAsync(locator).ConfigureAwait(true);
        foreach (var t in tools)
        {
            if (!_toolStatus.TryGetValue(t.Id, out var label)) continue;
            label.Text = t.Version is { Length: > 0 } v ? v
                : t.Detected ? Tr.Get("About_Detected")
                : Tr.Get("About_NotDetected");
        }
        _reportText = AppEnvironmentInfo.BuildReport(tools, Screens);
    }

    private static TextBlock Header(string text) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 6, 0, 0) };

    // "Label: <link>" row.
    private StackPanel LabeledLink(string label, string linkText, string url)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = label, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Link(linkText, url));
        return row;
    }

    // A hyperlink-styled button that opens a URL in the OS browser via the cross-platform launcher.
    private Button Link(string text, string url)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Avalonia.Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = url,
        };
        b.Click += async (_, _) =>
        {
            try { if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher) await launcher.LaunchUriAsync(new Uri(url)); }
            catch { /* best-effort open */ }
        };
        return b;
    }
}
