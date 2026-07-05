// Help ▸ Debug Info (#2): a read-only, selectable diagnostic report (Notepad++-style) the user can
// copy to share when reporting an issue. Content comes from AppEnvironmentInfo (no personal data).

using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Therion.Processing.Abstractions;
using TherionProc.Resources;
using TherionProc.Services;

namespace TherionProc.Views;

public sealed class DebugInfoWindow : Window
{
    private readonly TextBox _text;

    public DebugInfoWindow()
    {
        Title = Tr.Get("Debug_Title");
        Width = 700;
        Height = 560;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Read-only but fully selectable/copyable, monospace, both scrollbars for wide lines.
        _text = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            FontSize = 12,
            Text = Tr.Get("Debug_Collecting"),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_text, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_text, ScrollBarVisibility.Auto);

        var copy = new Button { Content = Tr.Get("Common_CopyToClipboard"), MinWidth = 150 };
        copy.Click += async (_, _) => { if (Clipboard is { } cb) await cb.SetTextAsync(_text.Text ?? string.Empty); };
        var close = new Button { Content = Tr.Get("Common_Close"), IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { copy, close },
        };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);

        Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children = { buttons, _text },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += async (_, _) => await PopulateAsync();
    }

    private async Task PopulateAsync()
    {
        IExternalToolLocator? locator = null;
        try { locator = AppServices.Provider.GetService<IExternalToolLocator>(); } catch { /* design-time */ }
        var tools = await AppEnvironmentInfo.DetectToolsAsync(locator).ConfigureAwait(true);
        _text.Text = AppEnvironmentInfo.BuildReport(tools, Screens);
    }
}
