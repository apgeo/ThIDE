// The in-app implementation of Therion.Mcp's ring-R3 seam (IUiBridge). Its mere presence in the
// MCP server's service collection is what makes AddTherionMcpTools register the R3 tool catalog;
// the headless stdio host registers NullUiBridge instead and never exposes R3.
//
// Deliberately thin for T-03.1 — the bridge grows real UI operations (all marshalled onto the
// dispatcher thread) in T-03.2. For now it only answers "is a window up to talk to".

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Therion.Mcp;

namespace ThIDE.Services;

/// <summary>App-side <see cref="IUiBridge"/>: reports whether the running IDE has a main window.</summary>
public sealed class UiBridge : IUiBridge
{
    /// <summary>True once the desktop lifetime has a main window that R3 tools could act on.</summary>
    public bool IsAvailable
    {
        get
        {
            // Reading the MainWindow reference off the Kestrel thread is a benign field read; any real
            // UI touch (T-03.2) marshals through Dispatcher.UIThread. Guarded because Application.Current
            // is null in design-time / headless-test contexts.
            try
            {
                return Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime { MainWindow: not null };
            }
            catch
            {
                return false;
            }
        }
    }
}
