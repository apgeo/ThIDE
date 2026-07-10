// The in-app implementation of Therion.Mcp's ring-R3 seam (IUiBridge). Its mere presence in the
// MCP server's service collection is what makes AddTherionMcpTools register the R3 tool catalog;
// the headless stdio host registers NullUiBridge instead and never exposes R3.
//
// Three jobs: the UI-thread marshaller (InvokeAsync), the read side of ring R3 (get_ui_state /
// get_open_documents, T-03.3), and the benign action side (open_file, focus_tool, goto_symbol,
// show_in_3d, show_toast, T-03.4). Every action runs on the dispatcher thread and drives an existing
// app service — no new UI plumbing. All app dependencies are optional so a bare `new UiBridge()` still
// stands in for a UI-less test.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Therion.Mcp;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using ThIDE.Docking;
using ThIDE.Editor;

namespace ThIDE.Services;

/// <summary>App-side <see cref="IUiBridge"/>: availability, dispatcher marshalling, R3 reads + actions.</summary>
public sealed class UiBridge : IUiBridge
{
    private readonly IDocumentService? _documents;
    private readonly IAppSettingsService? _settings;
    private readonly DockFactory? _dock;
    private readonly INotificationService? _notifications;

    public UiBridge(
        IDocumentService? documents = null,
        IAppSettingsService? settings = null,
        DockFactory? dock = null,
        INotificationService? notifications = null)
    {
        _documents = documents;
        _settings = settings;
        _dock = dock;
        _notifications = notifications;
    }

    /// <summary>True once the desktop lifetime has a main window that R3 tools could act on.</summary>
    public bool IsAvailable
    {
        get
        {
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

    /// <summary>Whether R3 <em>action</em> tools may drive the UI (on by default).</summary>
    public bool FollowAgent => _settings?.Current.McpFollowAgent ?? true;

    /// <summary>Runs <paramref name="func"/> on the Avalonia UI thread and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> func) => Dispatcher.UIThread.InvokeAsync(func);

    // ---- reads (T-03.3) --------------------------------------------------------------------------

    public Task<UiState?> GetUiStateAsync() => InvokeAsync(() => Task.FromResult(GatherUiState()));

    public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
        InvokeAsync(() => Task.FromResult(GatherOpenDocuments()));

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
            FollowAgent: FollowAgent);
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

    // ---- actions (T-03.4). Each runs on the UI thread and drives an existing service. ------------

    public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line) => InvokeAsync(async () =>
    {
        if (_documents is null) return new UiActionResult(false, "No document service.");
        try
        {
            await _documents.OpenFileAsync(absolutePath).ConfigureAwait(true);
            if (line is int ln) TherionTextEditor.ForPath(absolutePath)?.GoToLine(ln);
            var where = line is int l ? $" at line {l}" : string.Empty;
            return new UiActionResult(true, $"Opened {Path.GetFileName(absolutePath)}{where}.");
        }
        catch (Exception ex)
        {
            return new UiActionResult(false, $"Could not open the file: {ex.Message}");
        }
    });

    public Task<UiActionResult> FocusToolAsync(string toolId) => InvokeAsync(() =>
    {
        if (_dock is null) return Task.FromResult(new UiActionResult(false, "No dock."));
        if (_dock.ShowToolById(toolId))
            return Task.FromResult(new UiActionResult(true, $"Focused the {toolId} pane."));
        var ids = string.Join(", ", _dock.AvailableToolIds().OrderBy(x => x, StringComparer.Ordinal));
        return Task.FromResult(new UiActionResult(false, $"Unknown tool id '{toolId}'. Available: {ids}."));
    });

    public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => InvokeAsync(async () =>
    {
        if (_documents?.Workspace is not { } model)
            return new UiActionResult(false, "No project is loaded to resolve the symbol against.");

        var span = new WorkspaceSymbolNavigationService(model).GoToDefinition(qualifiedName, ReferenceKind.Any);
        if (span is not { } target || target.IsEmpty)
            return new UiActionResult(false, $"No station or survey named '{qualifiedName}' was found.");

        await _documents.NavigateToSpanAsync(target).ConfigureAwait(true);
        return new UiActionResult(true, $"Navigated to {qualifiedName}.");
    });

    public Task<UiActionResult> ShowInThreeDAsync(string station) => InvokeAsync(() =>
    {
        if (_documents is null) return Task.FromResult(new UiActionResult(false, "No document service."));
        _documents.RequestShowInModel3D(station);
        return Task.FromResult(new UiActionResult(true, $"Asked the 3D viewer to reveal {station}."));
    });

    public Task<UiActionResult> ShowToastAsync(string message, string kind) => InvokeAsync(() =>
    {
        if (_notifications is null) return Task.FromResult(new UiActionResult(false, "No notification service."));
        var title = Resources.Tr.Get("Mcp_ToastTitle");
        switch (kind?.Trim().ToLowerInvariant())
        {
            case "success": _notifications.Success(title, message); break;
            case "warning": _notifications.Warning(title, message); break;
            case "error": _notifications.Error(title, message); break;
            default: _notifications.Info(title, message); break;
        }
        return Task.FromResult(new UiActionResult(true, "Toast shown."));
    });
}
