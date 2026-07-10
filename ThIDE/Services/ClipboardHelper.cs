// Desktop clipboard access. Split out of DataExport when the pure formatters moved to
// Therion.Semantics; this is the only half that needs Avalonia.

using Avalonia.Input.Platform;

namespace ThIDE.Services;

/// <summary>Sets text on the desktop clipboard via the main window (no-op in headless tests).</summary>
public static class ClipboardHelper
{
    public static void SetText(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            _ = life.MainWindow?.Clipboard?.SetTextAsync(text);
    }
}
