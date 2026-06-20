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
    Task WriteCurrentTextAsync(string newText, CancellationToken ct = default);
    void Close();
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
        OpenInDockRequested?.Invoke(this, doc);
        SetActive(doc);
    }

    public async Task OpenFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var resolved = await _resolver.ResolveAsync(folderPath, ct).ConfigureAwait(false);
        if (resolved.Selected is null) return;

        await DisposeWorkspaceAsync().ConfigureAwait(false);

        _workspace = new TherionWorkspace();
        await _workspace.LoadAsync(resolved.Selected, ct).ConfigureAwait(false);
        Workspace = _workspace.BuildSemanticModel();

        // Show the entry file as a document; per-file semantics live on the document.
        if (File.Exists(resolved.Selected))
            await OpenFileAsync(resolved.Selected, ct).ConfigureAwait(false);
        else
            Raise();
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
        if (Documents.Remove(doc) && ReferenceEquals(Active, doc))
            SetActive(Documents.LastOrDefault());
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
