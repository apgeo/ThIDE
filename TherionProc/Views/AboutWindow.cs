// Help ▸ About: a small modal showing the app name, version, and the pinned Therion
// source-of-truth, plus a button to open the bundled Therion book (#1).

using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TherionProc.Resources;
using TherionProc.Services;

namespace TherionProc.Views;

public sealed class AboutWindow : Window
{
    public AboutWindow(IThbookDocumentationService? thbook = null)
    {
        Title = Tr.Get("About_Title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var version = ResolveDisplayVersion();

        var title = new TextBlock { Text = "TherionProc", FontSize = 22, FontWeight = FontWeight.Bold };
        var ver = new TextBlock { Text = Tr.Get("About_VersionPrefix") + version, Foreground = Brushes.Gray };
        var blurb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Tr.Get("About_Blurb"),
        };

        var book = new Button { Content = Tr.Get("About_OpenBook"), MinWidth = 150 };
        book.Click += (_, _) => thbook?.OpenAtPage(1);
        var close = new Button { Content = Tr.Get("Common_Close"), IsCancel = true, MinWidth = 80 };
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

    // The SemVer display version. Prefers the informational version (e.g. "0.2.0-beta.1+build.250"),
    // then the file version, then the assembly version. The build-number metadata is reformatted as
    // a friendly "(build N)" suffix (and dropped for release builds, where N is 0).
    private static string ResolveDisplayVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return FormatInformationalVersion(info);
        var file = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(file)) return file;
        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }

    // "0.2.0-beta.1+build.250" → "0.2.0-beta.1 (build 250)"; a "+build.0" or other "+<meta>" is
    // dropped so a tagged release shows a clean SemVer.
    private static string FormatInformationalVersion(string info)
    {
        int plus = info.IndexOf('+');
        if (plus < 0) return info;
        var core = info[..plus];
        var meta = info[(plus + 1)..];
        const string buildPrefix = "build.";
        if (meta.StartsWith(buildPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var n = meta[buildPrefix.Length..];
            if (n.Length > 0 && n != "0") return $"{core} (build {n})";
        }
        return core;
    }
}
