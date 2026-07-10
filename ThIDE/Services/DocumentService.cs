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
using ThIDE.ViewModels.Docking;

namespace ThIDE.Services;

/// <summary>
/// Options for <see cref="IDocumentService.ActivateThconfigAsync"/> — the single entry point that all
/// "set the current thconfig" UI paths funnel through, so their side effects stay consistent.
/// </summary>
public readonly record struct ThconfigActivation(
    bool SetWorkingDirectory = false,
    bool OpenInEditor = false,
    bool Notify = false);

/// <summary>
/// The open, dirty, in-graph editor buffers to overlay on their disk files for live analysis. A narrow
/// seam so the in-app MCP host's <c>LiveWorkspaceHost</c> (T-03.2) can read unsaved edits without taking a
/// dependency on the whole document service, and so it is trivial to fake in a test.
/// </summary>
public interface IUnsavedBufferProvider
{
    /// <summary>Path/text of every open document that is dirty and part of the active object graph.
    /// Reads the UI-bound document list, so callers must be on the UI thread.</summary>
    System.Collections.Generic.IReadOnlyList<(string Path, string Text)> DirtyBuffers();
}

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

    // ---- recently-closed tabs (VSCode Ctrl+Shift+T) ----
    /// <summary>True when there is at least one closed tab that can be reopened.</summary>
    bool HasRecentlyClosed { get; }
    /// <summary>Reopens the most-recently-closed file that still exists and isn't already open.
    /// Returns true if a tab was reopened.</summary>
    Task<bool> ReopenLastClosedAsync(CancellationToken ct = default);

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
    /// <summary>opens a file bypassing the binary/huge-file guard (the "Open anyway" path).</summary>
    Task ForceOpenFileAsync(string absolutePath, CancellationToken ct = default);

    /// <summary>
    /// Enters batch-open mode (session restore): while the returned scope is alive,
    /// <see cref="OpenFileAsync"/> creates tabs but skips the per-file activation — so the
    /// <see cref="ActiveDocumentChanged"/>/<see cref="DocumentChanged"/> fan-out to every tool
    /// panel and the per-file recent-list settings write don't run N times. Disposing the scope
    /// activates the last opened document once and saves the recent list in a single write.
    /// </summary>
    IDisposable BeginBatchOpen();
    /// <summary>True while a <see cref="BeginBatchOpen"/> scope is active. The dock host uses this
    /// to add restored tabs without focusing (and thus materializing an editor for) each one in turn.</summary>
    bool IsBatchOpenActive { get; }
    Task OpenFolderAsync(string folderPath, CancellationToken ct = default);
    /// <summary>
    /// The single entry point for making a thconfig the active project configuration. Optionally sets
    /// its folder as the workspace root and/or opens it in the editor, and (when asked) shows a toast.
    /// The session's Changed event then re-targets the whole UI (dropdown, orphan banners, window
    /// title, workspace-explorer selection, build pipeline). Returns false when the file is missing or
    /// fails to load. All the scattered "set active thconfig" call sites route through this so their
    /// behaviour and UI propagation are identical (only the options differ).
    /// </summary>
    Task<bool> ActivateThconfigAsync(string thconfigPath, ThconfigActivation options = default, CancellationToken ct = default);
    /// <summary>Opens the file a span lives in (if needed) and scrolls/flashes the editor to it.</summary>
    Task NavigateToSpanAsync(Therion.Core.SourceSpan span, CancellationToken ct = default);
    Task WriteCurrentTextAsync(string newText, CancellationToken ct = default);
    /// <summary>persists a specific document's current text to disk (auto-save).</summary>
    Task SaveDocumentAsync(FileDocumentViewModel document, CancellationToken ct = default);
    void Close();

    // ---- navigation history (back/forward across files, VSCode-style) ----
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    Task GoBackAsync(CancellationToken ct = default);
    Task GoForwardAsync(CancellationToken ct = default);
    /// <summary>Raised when the back/forward availability changes.</summary>
    event EventHandler? HistoryChanged;
    /// <summary>
    /// Reports the active editor's caret so the back/forward history tracks cursor jumps
    /// like a normal editor (#1). Term-navigation moves (Shift+F12) pass
    /// <paramref name="isTermNavigation"/>=true and are ignored.
    /// </summary>
    void ReportCaret(Therion.Core.SourceSpan span, bool isTermNavigation, bool fromPointer = false);
    /// <summary>Raised on every caret move so the status bar can show line/col/position (#10).</summary>
    event EventHandler<Therion.Core.SourceSpan>? CaretMoved;

    // ---- workspace reveal (highlight a file/object in the Workspace tree) ----
    /// <summary>Asks the Workspace Explorer to reveal/highlight the node for a target (#8/#9).</summary>
    void RequestRevealInWorkspace(Therion.Core.SourceSpan target);
    event EventHandler<Therion.Core.SourceSpan>? RevealInWorkspaceRequested;

    /// <summary>Asks the Workspace Explorer to switch to file view and select a file (editor "reveal" button, #1).</summary>
    void RequestSelectFileInWorkspace(string filePath);
    event EventHandler<string>? SelectFileInWorkspaceRequested;

    /// <summary>Asks the shell to open Find-in-Files as a "find all references" for an identifier (#4).</summary>
    void RequestFindReferences(string term);
    event EventHandler<string>? FindReferencesRequested;

    /// <summary>Asks the shell to start a rename-symbol flow for the given raw token (#1).</summary>
    void RequestRenameSymbol(string raw, Therion.Processing.Abstractions.ReferenceKind kind);
    event EventHandler<(string Raw, Therion.Processing.Abstractions.ReferenceKind Kind)>? RenameSymbolRequested;

    /// <summary>Asks the shell to list every reference to the symbol named by the raw token (#7).</summary>
    void RequestFindAllReferences(string raw, Therion.Processing.Abstractions.ReferenceKind kind);
    event EventHandler<(string Raw, Therion.Processing.Abstractions.ReferenceKind Kind)>? FindAllReferencesRequested;

    /// <summary>Asks the shell to reveal a station/survey (by full dotted name) in the embedded 3D viewer.</summary>
    void RequestShowInModel3D(string fullName);
    event EventHandler<string>? ShowInModel3DRequested;
}

public sealed class DocumentService : IDocumentService, IUnsavedBufferProvider, IAsyncDisposable
{
    private readonly IProjectEntryPointResolver _resolver;
    private readonly IWorkspaceSession _session;
    private readonly IAppSettingsService? _settings;
    private readonly ICommandRegistry? _commands;
    private readonly INotificationService? _notifications;   // (file-changed-on-disk toast)
    private readonly IScriptHookService? _hooks;             // (on-open / on-save hooks)

    // stack of recently-closed file paths (most-recent last) for Ctrl+Shift+T.
    private readonly System.Collections.Generic.List<string> _recentlyClosed = new();
    private const int MaxRecentlyClosed = 25;

    public ObservableCollection<FileDocumentViewModel> Documents { get; } = new();
    public FileDocumentViewModel? Active { get; private set; }

    // The single shared object graph lives in the workspace session (re-org #1/#4).
    public WorkspaceSemanticModel? Workspace => _session.Model;

    public event EventHandler? ActiveDocumentChanged;
    public event EventHandler? DocumentChanged;
    public event EventHandler<FileDocumentViewModel>? OpenInDockRequested;
    public event EventHandler<Therion.Core.SourceSpan>? RevealInWorkspaceRequested;
    public event EventHandler<string>? FindReferencesRequested;

    public void RequestRevealInWorkspace(Therion.Core.SourceSpan target)
        => RevealInWorkspaceRequested?.Invoke(this, target);

    public event EventHandler<string>? SelectFileInWorkspaceRequested;

    public void RequestSelectFileInWorkspace(string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
            SelectFileInWorkspaceRequested?.Invoke(this, filePath);
    }

    public void RequestFindReferences(string term)
    {
        if (!string.IsNullOrWhiteSpace(term)) FindReferencesRequested?.Invoke(this, term);
    }

    public event EventHandler<(string Raw, Therion.Processing.Abstractions.ReferenceKind Kind)>? RenameSymbolRequested;

    public void RequestRenameSymbol(string raw, Therion.Processing.Abstractions.ReferenceKind kind)
    {
        if (!string.IsNullOrWhiteSpace(raw))
            RenameSymbolRequested?.Invoke(this, (raw, kind));
    }

    public event EventHandler<(string Raw, Therion.Processing.Abstractions.ReferenceKind Kind)>? FindAllReferencesRequested;

    public void RequestFindAllReferences(string raw, Therion.Processing.Abstractions.ReferenceKind kind)
    {
        if (!string.IsNullOrWhiteSpace(raw))
            FindAllReferencesRequested?.Invoke(this, (raw, kind));
    }

    public event EventHandler<string>? ShowInModel3DRequested;

    public void RequestShowInModel3D(string fullName)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
            ShowInModel3DRequested?.Invoke(this, fullName);
    }

    // Projection of the active document.
    public string? CurrentPath => Active?.FilePath;
    public string CurrentText => Active?.DocumentText ?? string.Empty;
    public SemanticModel? CurrentSemantics => Active?.Semantics;
    public TherionFile? CurrentAst => Active?.Ast;
    public ImmutableArray<Therion.Core.Diagnostic> CurrentDiagnostics =>
        Active?.Diagnostics ?? ImmutableArray<Therion.Core.Diagnostic>.Empty;
    public ISymbolNavigationService? CurrentNavigation => Active?.Navigation;

    public DocumentService(IProjectEntryPointResolver resolver, IWorkspaceSession session,
        IAppSettingsService? settings = null, ICommandRegistry? commands = null,
        INotificationService? notifications = null, IScriptHookService? hooks = null)
    {
        _resolver = resolver;
        _session = session;
        _settings = settings;
        _commands = commands;
        _notifications = notifications;
        _hooks = hooks;

        // The graph changed (active config switched, or a tracked file changed on disk):
        // re-attach it to every open document and refresh orphan banners (#4).
        _session.Changed += (_, _) => { PropagateWorkspaceToDocuments(); Raise(); };
        // ValidateOnType: a live buffer re-validation updates each doc's cross-file diagnostics/navigation
        // without the heavier full refresh that Changed triggers (no workspace-tree rebuild on every keystroke).
        _session.BuffersRevalidated += (_, _) => PropagateWorkspaceToDocuments();
        // A tracked file changed on disk while open → drive the editor reload banner (#6).
        _session.ExternalFileChanged += (_, e) => OnExternalFileChanged(e);
    }

    public Task OpenFileAsync(string absolutePath, CancellationToken ct = default) => OpenFileAsync(absolutePath, force: false, ct);
    public Task ForceOpenFileAsync(string absolutePath, CancellationToken ct = default) => OpenFileAsync(absolutePath, force: true, ct);

    // ---- batch open (session restore) --------------------------------------
    // Restoring N files used to cost N activations: each one re-ran the whole
    // DocumentChanged tool fan-out (live preview, outline, explorer, analytics, …) on the UI
    // thread, and each per-file RecordRecent settings save re-parsed every already-open
    // document via IAppSettingsService.Changed — an O(N²) reopen. In batch mode the loop only
    // creates tabs; activation, the tool refresh, and the recents write happen once at the end.
    private int _batchOpenDepth;
    private FileDocumentViewModel? _batchLastOpened;
    private readonly System.Collections.Generic.List<string> _batchRecents = new();

    public bool IsBatchOpenActive => _batchOpenDepth > 0;

    public IDisposable BeginBatchOpen()
    {
        _batchOpenDepth++;
        return new BatchOpenScope(this);
    }

    private void EndBatchOpen()
    {
        if (_batchOpenDepth == 0 || --_batchOpenDepth > 0) return;

        if (_batchRecents.Count > 0)
        {
            RecordRecentMany(_batchRecents);
            _batchRecents.Clear();
        }

        var last = _batchLastOpened;
        _batchLastOpened = null;
        if (last is null) return;
        // Activate first: the dock's ActiveDockableChanged echo then finds Active already set
        // and no-ops, so the whole batch costs exactly one ActiveDocumentChanged/DocumentChanged.
        if (ReferenceEquals(Active, last)) Raise();   // SetActive would no-op; tools still need one refresh for the batch
        else SetActive(last);
        OpenInDockRequested?.Invoke(this, last);
    }

    private sealed class BatchOpenScope : IDisposable
    {
        private DocumentService? _owner;
        public BatchOpenScope(DocumentService owner) => _owner = owner;
        public void Dispose() { _owner?.EndBatchOpen(); _owner = null; }
    }

    private async Task OpenFileAsync(string absolutePath, bool force, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(absolutePath)) return;
        var full = Path.GetFullPath(absolutePath);
        if (!File.Exists(full)) return;

        if (IsBatchOpenActive) _batchRecents.Add(full);
        else RecordRecent(full);
        if (FindOpen(full) is { } already)
        {
            if (IsBatchOpenActive) { _batchLastOpened = already; return; }
            OpenInDockRequested?.Invoke(this, already);
            SetActive(already);
            return;
        }

        // don't load a binary blob or a huge file into the text editor (it would hang /
        // show garbage). Offer "Open anyway" via a notification instead of silently doing it.
        if (!force && FileGuard.ShouldBlockTextOpen(full) is { } reason)
        {
            _notifications?.Warning(ThIDE.Resources.Tr.Get("Notif_NotTextTitle"),
                $"{Path.GetFileName(full)}: {reason}", ThIDE.Resources.Tr.Get("Notif_OpenAnyway"), () => _ = ForceOpenFileAsync(full));
            return;
        }

        var text = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
        var doc = CreateDocument(full, text);
        OnUiSync(() => Documents.Add(doc));

        // Establish a workspace for a directly-opened file when none exists yet, then
        // light up cross-file navigation and the orphan banner (#1/#4).
        await _session.EnsureCoversAsync(full, ct).ConfigureAwait(false);
        doc.SetWorkspace(Workspace);
        UpdateMembership(doc);

        // Batch mode still creates the tab (the dock adds it unfocused) but defers activation —
        // and with it the per-file tool fan-out — to the end of the batch.
        OpenInDockRequested?.Invoke(this, doc);
        if (IsBatchOpenActive) _batchLastOpened = doc;
        else SetActive(doc);
        _hooks?.Run(ScriptHookEvent.Open, full);
    }

    public async Task OpenFolderAsync(string folderPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        // Open Folder explicitly sets the single workspace root and activates the first
        // detected thconfig (#1/#2). Open that active config so the user sees the project.
        await _session.SetRootAsync(folderPath, ct).ConfigureAwait(false);
        if (_session.ActiveThconfig is { } active && File.Exists(active.FullPath))
            await OpenFileAsync(active.FullPath, ct).ConfigureAwait(false);
    }

    public async Task<bool> ActivateThconfigAsync(string thconfigPath, ThconfigActivation options = default, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(thconfigPath)) return false;
        string full;
        try { full = Path.GetFullPath(thconfigPath); } catch { return false; }

        if (options.SetWorkingDirectory)
        {
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) await _session.SetRootAsync(dir, ct).ConfigureAwait(false);
        }

        var ok = await _session.SetActiveThconfigAsync(full, ct).ConfigureAwait(false);
        if (!ok)
        {
            if (options.Notify)
                _notifications?.Warning(ThIDE.Resources.Tr.Get("Notif_ActivateFailTitle"),
                    string.Format(ThIDE.Resources.Tr.Get("Notif_ActivateFailMsg"), Path.GetFileName(full)));
            return false;
        }

        if (options.OpenInEditor) await OpenFileAsync(full, ct).ConfigureAwait(false);

        if (options.Notify)
            _notifications?.Info(
                options.SetWorkingDirectory ? "Working directory set" : "Active thconfig set",
                options.SetWorkingDirectory
                    ? $"Working directory is now {Path.GetDirectoryName(full)}, with {Path.GetFileName(full)} as the active thconfig."
                    : $"{Path.GetFileName(full)} is now the active thconfig.");
        return true;
    }

    private void PropagateWorkspaceToDocuments()
    {
        // Snapshot: this can run off a session-changed callback while documents open/close.
        foreach (var d in Documents.ToList()) { d.SetWorkspace(Workspace); UpdateMembership(d); }
    }

    // ---- ValidateOnType: live whole-project re-validation from unsaved buffers -------------------

    private CancellationTokenSource? _validateOnTypeCts;

    /// <summary>Debounced trigger: re-validate the graph against the current unsaved buffers.
    /// Runs when either "validate on type" or "auto-recalc leads" is on — both need the graph rebuilt
    /// from the live buffers; the resulting <c>BuffersRevalidated</c> refreshes diagnostics and leads.</summary>
    private void ScheduleValidateOnType()
    {
        var s = _settings?.Current;
        if (s is null || (!s.ValidateOnType && !s.AutoRecalcLeads)) return;
        _validateOnTypeCts?.Cancel();
        var cts = _validateOnTypeCts = new CancellationTokenSource();
        _ = RunValidateOnTypeAsync(cts.Token);
    }

    private async Task RunValidateOnTypeAsync(CancellationToken ct)
    {
        try { await Task.Delay(600, ct).ConfigureAwait(false); } catch { return; }
        if (ct.IsCancellationRequested) return;

        // Overlay every open, dirty, in-graph document; the session restores the rest from disk.
        var buffers = DirtyBuffers();
        try { await _session.RevalidateBuffersAsync(buffers, ct).ConfigureAwait(false); }
        catch { /* best-effort live validation */ }
    }

    /// <summary>
    /// The open, dirty, in-graph buffers to overlay — the same set live validation uses. Also read by the
    /// in-app MCP host so an agent sees the editor's unsaved state, not the last save (T-03.2). Enumerates
    /// the UI-bound document list, so it must be called on the UI thread.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<(string Path, string Text)> DirtyBuffers() =>
        Documents
            .Where(d => d.IsDirty && !string.IsNullOrEmpty(d.FilePath) && _session.Covers(d.FilePath))
            .Select(d => (d.FilePath, d.DocumentText))
            .ToList();

    /// <summary>Flags a document's orphan banner: true when it isn't in the active graph (#4).</summary>
    private void UpdateMembership(FileDocumentViewModel doc)
    {
        bool member = _session.Covers(doc.FilePath);
        var activeName = _session.ActiveThconfig is { } a ? Path.GetFileName(a.FullPath) : null;
        doc.SetWorkspaceMembership(member, activeName);
    }

    /// <summary>A tracked file changed on disk → ask the matching open document to react (#6).</summary>
    private void OnExternalFileChanged(ExternalFileChange change)
    {
        if (FindOpen(change.Path) is not { } doc) return;
        bool autoReload = _settings?.Current.AutoReloadExternalChanges ?? true;
        doc.NotifyExternalChange(change.TimeUtc.ToLocalTime(), change.Deleted, autoReload);

        // surface the on-disk change as a toast/bell entry. A clean file that is being
        // silently auto-reloaded raises no toast (the editor already flashes its info banner);
        // only conflicts (unsaved edits) and deletions are worth a notification.
        var name = Path.GetFileName(change.Path);
        if (change.Deleted)
            _notifications?.Warning(ThIDE.Resources.Tr.Get("Notif_FileDeletedTitle"), string.Format(ThIDE.Resources.Tr.Get("Notif_FileDeletedMsg"), name));
        else if (doc.IsDirty || !autoReload)
            _notifications?.Info(ThIDE.Resources.Tr.Get("Notif_FileChangedTitle"), string.Format(ThIDE.Resources.Tr.Get("Notif_FileChangedMsg"), name));
    }

    // ---- navigation history -------------------------------------------------
    // Back/forward walk the caret's actual trail, like a normal text editor (#6): every line
    // the cursor comes to rest on — by clicking, arrowing, paging, go-to-definition, or
    // switching files — is a stop. It is NOT limited to highlighted-term/reference jumps.
    // Column-only moves (typing along a line) coalesce, and only programmatic back/forward
    // and Shift+F12 occurrence cycling are kept out of the trail.

    private readonly System.Collections.Generic.List<Therion.Core.SourceSpan> _history = new();
    private int _historyIndex = -1;
    private bool _suppressHistory;
    private Therion.Core.SourceSpan _lastCaret;
    private bool _hasLastCaret;
    private const int MaxHistory = 200;

    public event EventHandler? HistoryChanged;
    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public Task GoBackAsync(CancellationToken ct = default) =>
        CanGoBack ? NavigateHistoryAsync(_historyIndex - 1, ct) : Task.CompletedTask;

    public Task GoForwardAsync(CancellationToken ct = default) =>
        CanGoForward ? NavigateHistoryAsync(_historyIndex + 1, ct) : Task.CompletedTask;

    private async Task NavigateHistoryAsync(int index, CancellationToken ct)
    {
        _historyIndex = index;
        _suppressHistory = true; // the resulting caret move must not re-enter the trail
        // History stores zero-length caret positions; the navigation/scroll path treats a
        // zero-length span as "no location", so give the target a minimal extent to move to.
        var target = _history[index];
        if (target.IsEmpty) target = target with { Length = 1 };
        try { await NavigateToSpanAsync(target, ct).ConfigureAwait(true); }
        finally { _suppressHistory = false; HistoryChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler<Therion.Core.SourceSpan>? CaretMoved;

    public void ReportCaret(Therion.Core.SourceSpan span, bool isTermNavigation, bool fromPointer = false)
    {
        // A caret is a zero-length position, so DO NOT reject on span.IsEmpty (Length == 0) —
        // that guard previously discarded every caret report, leaving the trail permanently
        // empty (the back/forward buttons never lit up). Only a missing file is unusable (#6).
        if (string.IsNullOrEmpty(span.FilePath)) return;

        // The status bar follows every caret move (#7), independent of the history filtering.
        CaretMoved?.Invoke(this, span);

        // Programmatic back/forward navigation and highlighted-term cycling (Shift+F12) keep the
        // reference point current but never add to the trail (#6).
        if (isTermNavigation || _suppressHistory) { _lastCaret = span; _hasLastCaret = true; return; }

        // Record a stop whenever the caret settles on a new line (or another file). The
        // fromPointer flag is retained for the interface but no longer gates recording — the
        // trail captures keyboard navigation too, not just clicks.
        _ = fromPointer;
        if (!_hasLastCaret || !SameLine(_lastCaret, span))
            RecordHistory(span);
        _lastCaret = span;
        _hasLastCaret = true;
    }

    private void RecordHistory(Therion.Core.SourceSpan loc)
    {
        if (string.IsNullOrEmpty(loc.FilePath)) return; // zero-length caret position is fine

        // Moving after going back drops the forward branch (standard editor behaviour).
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        // Coalesce: staying on the line we're already parked at just updates that stop's column.
        if (_historyIndex >= 0 && SameLine(_history[_historyIndex], loc))
        {
            _history[_historyIndex] = loc;
            return;
        }

        _history.Add(loc);
        if (_history.Count > MaxHistory) _history.RemoveAt(0); // cap the trail; oldest stop falls off
        _historyIndex = _history.Count - 1;
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool SameLine(Therion.Core.SourceSpan a, Therion.Core.SourceSpan b) =>
        string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase) &&
        a.Start.Line == b.Start.Line;

    public async Task NavigateToSpanAsync(Therion.Core.SourceSpan span, CancellationToken ct = default)
    {
        if (span.IsEmpty || string.IsNullOrEmpty(span.FilePath)) return;

        // History is no longer recorded here — the resulting caret move is captured by
        // ReportCaret, so navigation (go-to-definition, diagnostics, tree) lands in the
        // history as an ordinary cursor jump (#1).
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
        OnUiSync(() => Documents.Add(doc));
        OpenInDockRequested?.Invoke(this, doc);
        SetActive(doc);
        return doc;
    }

    public void CloseDocument(FileDocumentViewModel document)
    {
        OnClosed(document);
        ScheduleValidateOnType();   // drop any buffer overlay for the closed doc (restore it from disk)
    }

    public void Close()
    {
        foreach (var d in Documents.ToList()) OnClosed(d);
        Raise();
    }

    public async Task WriteCurrentTextAsync(string newText, CancellationToken ct = default)
    {
        if (Active is not { } doc) return;
        // trim trailing whitespace + ensure a final newline on save. Idempotent, so a
        // loaded editor that already cleaned itself in place (caret-preserving) makes this a no-op.
        newText = EditorTextCleanup.ApplyOnSave(newText, _settings?.Current);
        if (File.Exists(doc.FilePath))
        {
            // Mark the path so the watcher doesn't treat our own save as an external edit (#6).
            _session.SuppressSelfWrite(doc.FilePath);
            await File.WriteAllTextAsync(doc.FilePath, newText, ct).ConfigureAwait(true);
            _hooks?.Run(ScriptHookEvent.Save, doc.FilePath);
        }
        doc.SetText(newText, reparse: true);
    }

    public async Task SaveDocumentAsync(FileDocumentViewModel document, CancellationToken ct = default)
    {
        if (document is null || string.IsNullOrEmpty(document.FilePath) || !File.Exists(document.FilePath)) return;
        // cleanup, then write — suppressing the watcher so our own save isn't flagged external (#6).
        var newText = EditorTextCleanup.ApplyOnSave(document.DocumentText, _settings?.Current);
        _session.SuppressSelfWrite(document.FilePath);
        await File.WriteAllTextAsync(document.FilePath, newText, ct).ConfigureAwait(true);
        document.SetText(newText, reparse: true);
    }

    private FileDocumentViewModel CreateDocument(string path, string text)
    {
        var doc = new FileDocumentViewModel(path, text, new ViewModels.MeasurementsViewModel(), _commands, _settings);
        doc.Reparsed += (_, _) => { if (ReferenceEquals(doc, Active)) Raise(); };
        // ValidateOnType: after a per-file re-parse, (debounced) re-validate the whole object graph
        // against every unsaved buffer, so cross-file/project diagnostics track typing, not just saves.
        doc.Reparsed += (_, _) => ScheduleValidateOnType();
        // "Keep mine (save to disk)" — write the editor text to disk (overwrite the
        // external change or recreate a deleted file), suppressing the self-write watcher echo.
        doc.SaveToDiskRequested += async (_, _) => await OverwriteToDiskAsync(doc).ConfigureAwait(true);
        return doc;
    }

    private async Task OverwriteToDiskAsync(FileDocumentViewModel doc)
    {
        try
        {
            var text = EditorTextCleanup.ApplyOnSave(doc.DocumentText, _settings?.Current);
            _session.SuppressSelfWrite(doc.FilePath);
            await File.WriteAllTextAsync(doc.FilePath, text).ConfigureAwait(true);
            doc.SetText(text, reparse: true);
        }
        catch { /* best-effort; the banner is already cleared */ }
    }

    private const int MaxRecentFiles = 64;

    /// <summary>Promotes a just-opened file to the front of the persisted recent list (#8).</summary>
    private void RecordRecent(string fullPath)
    {
        if (_settings is null) return;
        var current = _settings.Current;
        var list = new System.Collections.Generic.List<string>(current.RecentFiles.Count + 1) { fullPath };
        foreach (var p in current.RecentFiles)
            if (!string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase)) list.Add(p);
        if (list.Count > MaxRecentFiles) list.RemoveRange(MaxRecentFiles, list.Count - MaxRecentFiles);
        try { _settings.Save(current with { RecentFiles = list }); } catch { /* best-effort */ }
    }

    /// <summary>Promotes a batch of just-opened files with a single settings save (same result as
    /// sequential <see cref="RecordRecent"/> calls: the last-opened file ends up first).</summary>
    private void RecordRecentMany(System.Collections.Generic.IReadOnlyList<string> fullPaths)
    {
        if (_settings is null || fullPaths.Count == 0) return;
        var current = _settings.Current;
        var list = new System.Collections.Generic.List<string>(current.RecentFiles.Count + fullPaths.Count);
        for (int i = fullPaths.Count - 1; i >= 0; i--)
            if (!list.Contains(fullPaths[i], StringComparer.OrdinalIgnoreCase)) list.Add(fullPaths[i]);
        foreach (var p in current.RecentFiles)
            if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
        if (list.Count > MaxRecentFiles) list.RemoveRange(MaxRecentFiles, list.Count - MaxRecentFiles);
        try { _settings.Save(current with { RecentFiles = list }); } catch { /* best-effort */ }
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
        OnUi(() => ActiveDocumentChanged?.Invoke(this, EventArgs.Empty));
        Raise();
        // Single shared workspace (#1): switching tabs no longer changes the active project.
    }

    // OpenFileAsync resumes on a thread-pool thread (ConfigureAwait(false)), so DocumentChanged
    // can fire off the UI thread. Several subscribers mutate UI-bound state directly, which
    // raised "the calling thread cannot access this object". Marshal the notifications onto the
    // UI thread at the source so every subscriber is safe (#6).
    // In the app these marshal to the UI thread. In a headless unit-test process no dispatcher
    // loop is running (Application.Current is null — the app sets it before DI spins up), so
    // Invoke would deadlock the test and Post would silently drop the callback; run inline there.
    private static bool NoUiLoop => Avalonia.Application.Current is null;

    private static void OnUi(Action action)
    {
        if (NoUiLoop || Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    // The UI-bound Documents collection is an ObservableCollection and must only be MUTATED on the
    // UI thread (OpenFileAsync's continuation can run on a thread-pool thread). Marshal synchronously
    // (Invoke, not Post) so the open/close flow stays correctly ordered. This stops "Collection was
    // modified" enumeration crashes in any consumer iterating Documents (e.g. TodoScanViewModel).
    private static void OnUiSync(Action action)
    {
        if (NoUiLoop || Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Invoke(action);
    }

    private void OnClosed(FileDocumentViewModel doc)
    {
        bool removed = false;
        OnUiSync(() => removed = Documents.Remove(doc));
        if (removed)
        {
            RecordRecentlyClosed(doc.FilePath);   // enable Ctrl+Shift+T reopen
            doc.Dispose(); // stop its debounced re-parse timer
            if (ReferenceEquals(Active, doc))
                SetActive(Documents.LastOrDefault());
        }
    }

    // ---- recently-closed tabs ---------------------------------------

    /// <summary>Pushes a just-closed file onto the reopen stack (de-duplicated, capped).</summary>
    private void RecordRecentlyClosed(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // unsaved/in-memory → nothing to reopen
        _recentlyClosed.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentlyClosed.Add(path);
        if (_recentlyClosed.Count > MaxRecentlyClosed) _recentlyClosed.RemoveAt(0);
    }

    public bool HasRecentlyClosed
    {
        get
        {
            for (int i = _recentlyClosed.Count - 1; i >= 0; i--)
                if (File.Exists(_recentlyClosed[i]) && FindOpen(_recentlyClosed[i]) is null) return true;
            return false;
        }
    }

    public async Task<bool> ReopenLastClosedAsync(CancellationToken ct = default)
    {
        // Pop until we find a file that still exists and isn't already open again.
        while (_recentlyClosed.Count > 0)
        {
            var path = _recentlyClosed[^1];
            _recentlyClosed.RemoveAt(_recentlyClosed.Count - 1);
            if (!File.Exists(path) || FindOpen(path) is not null) continue;
            await OpenFileAsync(path, ct).ConfigureAwait(false);
            return true;
        }
        return false;
    }

    private void Raise() => OnUi(() => DocumentChanged?.Invoke(this, EventArgs.Empty));

    // The workspace session is a DI singleton and disposes itself; nothing to do here.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
