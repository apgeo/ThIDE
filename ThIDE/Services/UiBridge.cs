// The in-app implementation of Therion.Mcp's ring-R3 seam (IUiBridge). Its mere presence in the
// MCP server's service collection is what makes AddTherionMcpTools register the R3 tool catalog;
// the headless stdio host registers NullUiBridge instead and never exposes R3.
//
// Two jobs: the UI-thread marshaller (InvokeAsync) that LiveWorkspaceHost and the R3 tools reach the
// IDE through, and the read side of ring R3 (T-03.3) — get_ui_state / get_open_documents gather their
// answer here, on the dispatcher thread, from the document service, the focused editor, the dock and
// the settings. All the app dependencies are optional so a bare `new UiBridge()` still stands in for a
// UI-less test.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Therion.Mcp;
using ThIDE.Docking;
using ThIDE.Editor;

namespace ThIDE.Services;

/// <summary>App-side <see cref="IUiBridge"/>: window availability, dispatcher marshalling, R3 reads.</summary>
public sealed class UiBridge : IUiBridge
{
    private readonly IDocumentService? _documents;
    private readonly IAppSettingsService? _settings;
    private readonly DockFactory? _dock;

    public UiBridge(
        IDocumentService? documents = null,
        IAppSettingsService? settings = null,
        DockFactory? dock = null)
    {
        _documents = documents;
        _settings = settings;
        _dock = dock;
    }

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

    public Task<UiState?> GetUiStateAsync() => InvokeAsync(() => Task.FromResult(GatherUiState()));

    public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
        InvokeAsync(() => Task.FromResult(GatherOpenDocuments()));

    // ---- gatherers (run on the UI thread) --------------------------------------------------------

    private UiState? GatherUiState()
    {
        if (!IsAvailable) return null;

        var focused = TherionTextEditor.LastFocused;
        var activeDoc = _documents?.Active?.FilePath;
        var unsaved = _documents?.Documents
            .Where(d => d.IsDirty && !string.IsNullOrEmpty(d.FilePath))
            .Select(d => d.FilePath)
            .ToList() ?? new List<string>();
        var panes = _dock?.OpenToolTitles() ?? (IReadOnlyList<string>)Array.Empty<string>();

        return new UiState(
            ActiveDocument: string.IsNullOrEmpty(activeDoc) ? null : activeDoc,
            FocusedDocument: string.IsNullOrEmpty(focused?.CurrentFilePath) ? null : focused!.CurrentFilePath,
            CaretLine: focused?.CurrentLine ?? 0,
            CaretColumn: focused?.CurrentColumn ?? 0,
            SelectionLength: focused?.SelectedText.Length ?? 0,
            VisiblePanes: panes,
            UnsavedDocuments: unsaved,
            FollowAgent: _settings?.Current.McpFollowAgent ?? true);
    }

    private IReadOnlyList<OpenDocumentInfo> GatherOpenDocuments()
    {
        if (_documents is null) return Array.Empty<OpenDocumentInfo>();
        var active = _documents.Active;
        return _documents.Documents
            .Where(d => !string.IsNullOrEmpty(d.FilePath))
            .Select(d => new OpenDocumentInfo(d.FilePath, ReferenceEquals(d, active), d.IsDirty))
            .ToList();
    }
}
