// The in-app implementation of Therion.Mcp's ring-R3 seam (IUiBridge). Its mere presence in the
// MCP server's service collection is what makes AddTherionMcpTools register the R3 tool catalog;
// the headless stdio host registers NullUiBridge instead and never exposes R3.
//
// Besides "is a window up to talk to", it is the UI-thread marshaller: LiveWorkspaceHost and (later)
// the R3 tools reach the running IDE's session/documents/UI only through InvokeAsync, so no MCP
// handler thread ever touches Avalonia state directly (T-03.2).

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Therion.Mcp;

namespace ThIDE.Services;

/// <summary>App-side <see cref="IUiBridge"/>: window availability + dispatcher marshalling.</summary>
public sealed class UiBridge : IUiBridge
{
    /// <summary>True once the desktop lifetime has a main window that R3 tools could act on.</summary>
    public bool IsAvailable
    {
        get
        {
            // Reading the MainWindow reference off the Kestrel thread is a benign field read; any real
            // UI touch marshals through InvokeAsync. Guarded because Application.Current is null in
            // design-time / headless-test contexts.
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

    /// <summary>Runs <paramref name="func"/> on the Avalonia UI thread and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> func) => Dispatcher.UIThread.InvokeAsync(func);
}
