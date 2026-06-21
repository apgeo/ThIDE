// Implementation Plan §6 / §7.3 — active-document host, now multi-document (MDI).
//
// Owns the set of open documents (one FileDocumentViewModel per file) and adds
// them to the central DocumentDock via DockFactory. The legacy "Current*" surface
// is preserved as a projection of the Active document so existing consumers
// (BuildViewModel, WorkspaceExplorer, XVI) keep working unchanged.

using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Services;

public interface IDocumentService
{
    // ---- Multi-document (MDI) ----
    ObservableCollection<FileDocumentViewModel> Documents { get; }
    FileDocumentViewModel? Active { get; }
    event EventHandler? ActiveDocumentChanged;
    /// <summary>Raised when a document should be shown/activated in the dock host.</summary>
    event EventHandler<FileDocumentViewModel>? OpenInDockRequested;
    /// <summary>Updates the active document (called by the dock host when the tab changes).</summary>
    void SetActive(FileDocumentViewModel? document);
    /// <summary>Opens an unsaved in-memory document (e.g. the startup sample).</summary>
    FileDocumentViewModel OpenTextDocument(string displayPath, string text);
    void CloseDocument(FileDocumentViewModel document);

    // ---- Legacy "current" surface (projection of Active) ----
    string? CurrentPath { get; }
    string CurrentText { get; }
    SemanticModel? CurrentSemantics { get; }
    TherionFile? CurrentAst { get; }
    WorkspaceSemanticModel? Workspace { get; }
    ImmutableArray<Therion.Core.Diagnostic> CurrentDiagnostics { get; }
    ISymbolNavigationService? CurrentNavigation { get; }
    event EventHandler? DocumentChanged;

    Task OpenFileAsync(string absolutePath, CancellationToken ct = default);
    Task OpenFolderAsync(string folderPath, CancellationToken ct = default);
    /// <summary>Opens the file a span lives in (if needed) and scrolls/flashes the editor to it.</summary>
    Task NavigateToSpanAsync(Therion.Core.SourceSpan span, CancellationToken ct = default);
    Task WriteCurrentTextAsync(string newText, CancellationToken ct = default);
    void Close();

    // ---- navigation history (back/forward across files, VSCode-style) ----
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    Task GoBackAsync(CancellationToken ct = default);
    Task GoForwardAsync(CancellationToken ct = default);
    /// <summary>Raised when the back/forward availability changes.</summary>
    event EventHandler? HistoryChanged;
}

public sealed class DocumentService : IDocumentService, IAsyncDisposable
{
    private readonly IProjectEntryPointResolver _resolver;
    private readonly ICommandRegistry? _commands;
    private TherionWorkspace? _workspace;

    public ObservableCollection<FileDocumentViewModel> Documents { get; } = new();
    public FileDocumentViewModel? Active { get; private set; }
    public WorkspaceSemanticModel? Workspace { get; private set; }

    public event EventHandler? ActiveDocumentChanged;
    public event EventHandler? DocumentChanged;
    public event EventHandler<FileDocumentViewModel>? OpenInDockRequested;

    // Projection of the active document.
    public string? CurrentPath => Active?.FilePath;
    public string CurrentText => Active?.DocumentText ?? string.Empty;
    public SemanticModel? CurrentSemantics => Active?.Semantics;
    public TherionFile? CurrentAst => Active?.Ast;
    public ImmutableArray<Therion.Core.Diagnostic> CurrentDiagnostics =>
        Active?.Diagnostics ?? ImmutableArray<Therion.Core.Diagnostic>.Empty;
    public ISymbolNavigationService? CurrentNavigation => Active?.Navigation;

    public DocumentService(IProjectEntryPointResolver resolver, ICommandRegistry? commands = null)
    {
        _resolver = resolver;
        _commands = commands;
    }

    public async Task OpenFileAsync(string absolutePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(absolutePath)) return;
        var full = Path.GetFullPath(absolutePath);
        if (!File.Exists(full)) return;

        if (FindOpen(full) is { } already)
        {
            OpenInDockRequested?.Invoke(this, already);
            SetActive(already);
            return;
        }

        var text = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
        var doc = CreateDocument(full, text);
        Documents.Add(doc);

        // Ensure a project workspace covers this file (auto-discovering its parent
        // .thconfig when a lone .th/.th2 is opened), then light up cross-file nav.
        await EnsureWorkspaceForAsync(full, ct).ConfigureAwait(false);
        doc.SetWorkspace(Workspace);

        OpenInDockRequested?.Invoke(this, doc);
        SetActive(doc);
    }

    public async Task OpenFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var resolved = await _resolver.ResolveAsync(folderPath, ct).ConfigureAwait(false);
        if (resolved.Selected is null || !File.Exists(resolved.Selected)) return;

        // OpenFileAsync builds/reuses the workspace for the resolved entry point.
        await OpenFileAsync(resolved.Selected, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Guarantees <see cref="Workspace"/> covers <paramref name="full"/>: reuses the
    /// current workspace when it already does, otherwise auto-discovers the project
    /// entry, loads it and rebuilds the cross-file semantic snapshot.
    /// </summary>
    private async Task EnsureWorkspaceForAsync(string full, CancellationToken ct)
    {
        if (_workspace is not null && Covers(_workspace, full)) return;

        string entry;
        try { entry = await Task.Run(() => ProjectEntryDiscovery.FindEntryPoint(full), ct).ConfigureAwait(false); }
        catch { entry = full; }

        var ws = new TherionWorkspace();
        try { await ws.LoadAsync(entry, ct).ConfigureAwait(false); }
        catch { await ws.DisposeAsync().ConfigureAwait(false); return; }

        var model = ws.BuildSemanticModel();
        var old = _workspace;
        _workspace = ws;
        Workspace = model;
        if (old is not null) await old.DisposeAsync().ConfigureAwait(false);

        PropagateWorkspaceToDocuments();
        Raise();
    }

    private static bool Covers(TherionWorkspace ws, string full)
    {
        foreach (var p in ws.LoadedFiles)
            if (string.Equals(p, full, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void PropagateWorkspaceToDocuments()
    {
        foreach (var d in Documents) d.SetWorkspace(Workspace);
    }

    // ---- navigation history -------------------------------------------------

    private readonly System.Collections.Generic.List<Therion.Core.SourceSpan> _history = new();
    private int _historyIndex = -1;
    private bool _suppressHistory;

    public event EventHandler? HistoryChanged;
    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public Task GoBackAsync(CancellationToken ct = default) =>
        CanGoBack ? NavigateHistoryAsync(--_historyIndex, ct) : Task.CompletedTask;

    public Task GoForwardAsync(CancellationToken ct = default) =>
        CanGoForward ? NavigateHistoryAsync(++_historyIndex, ct) : Task.CompletedTask;

    private async Task NavigateHistoryAsync(int index, CancellationToken ct)
    {
        _suppressHistory = true;
        try { await NavigateToSpanAsync(_history[index], ct).ConfigureAwait(true); }
        finally { _suppressHistory = false; HistoryChanged?.Invoke(this, EventArgs.Empty); }
    }

    private void RecordHistory(Therion.Core.SourceSpan loc)
    {
        if (loc.IsEmpty || string.IsNullOrEmpty(loc.FilePath)) return;
        if (_historyIndex >= 0 && SameLine(_history[_historyIndex], loc)) return; // collapse dupes
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1); // drop forward branch
        _history.Add(loc);
        _historyIndex = _history.Count - 1;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameLine(Therion.Core.SourceSpan a, Therion.Core.SourceSpan b) =>
        string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase) &&
        a.Start.Line == b.Start.Line;

    public async Task NavigateToSpanAsync(Therion.Core.SourceSpan span, CancellationToken ct = default)
    {
        if (span.IsEmpty || string.IsNullOrEmpty(span.FilePath)) return;

        if (!_suppressHistory)
        {
            if (Active?.CurrentCaret is { IsEmpty: false } from) RecordHistory(from); // leaving here
            RecordHistory(span);                                                       // going there
        }

        var target = FindOpen(span.FilePath);
        if (target is null)
        {
            await OpenFileAsync(span.FilePath, ct).ConfigureAwait(true);
            target = FindOpen(span.FilePath);
        }
        else
        {
            OpenInDockRequested?.Invoke(this, target);
            SetActive(target);
        }
        target?.RequestScrollTo(span);
    }

    public FileDocumentViewModel OpenTextDocument(string displayPath, string text)
    {
        if (FindOpen(displayPath) is { } already)
        {
            OpenInDockRequested?.Invoke(this, already);
            SetActive(already);
            return already;
        }
        var doc = CreateDocument(displayPath, text);
        Documents.Add(doc);
        OpenInDockRequested?.Invoke(this, doc);
        SetActive(doc);
        return doc;
    }

    public void CloseDocument(FileDocumentViewModel document) => OnClosed(document);

    public void Close()
    {
        foreach (var d in Documents.ToList()) OnClosed(d);
        Workspace = null;
        _ = DisposeWorkspaceAsync();
        Raise();
    }

    public async Task WriteCurrentTextAsync(string newText, CancellationToken ct = default)
    {
        if (Active is not { } doc) return;
        if (File.Exists(doc.FilePath))
            await File.WriteAllTextAsync(doc.FilePath, newText, ct).ConfigureAwait(false);
        doc.SetText(newText, reparse: true);
    }

    private FileDocumentViewModel CreateDocument(string path, string text)
    {
        var doc = new FileDocumentViewModel(path, text, new ViewModels.MeasurementsViewModel(), _commands);
        doc.Reparsed += (_, _) => { if (ReferenceEquals(doc, Active)) Raise(); };
        return doc;
    }

    private FileDocumentViewModel? FindOpen(string path) =>
        Documents.FirstOrDefault(d =>
            string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFullPath(d.FilePath), TryFull(path), StringComparison.OrdinalIgnoreCase));

    private static string TryFull(string p) { try { return Path.GetFullPath(p); } catch { return p; } }

    public void SetActive(FileDocumentViewModel? doc)
    {
        if (ReferenceEquals(Active, doc)) return;
        Active = doc;
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        Raise();
    }

    private void OnClosed(FileDocumentViewModel doc)
    {
        bool removed = Documents.Remove(doc);
        if (removed)
        {
            doc.Dispose(); // stop its debounced re-parse timer
            if (ReferenceEquals(Active, doc))
                SetActive(Documents.LastOrDefault());
        }
    }

    private void Raise() => DocumentChanged?.Invoke(this, EventArgs.Empty);

    private async Task DisposeWorkspaceAsync()
    {
        if (_workspace is not null)
        {
            await _workspace.DisposeAsync().ConfigureAwait(false);
            _workspace = null;
        }
    }

    public ValueTask DisposeAsync() => new(DisposeWorkspaceAsync());
}
