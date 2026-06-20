// Implementation Plan §6 / §7.3 — active-document host for the UI.
//
// Wraps `TherionWorkspace` so the UI gets a single source of truth for:
//   - what's open (single file or a thconfig-driven set),
//   - the current document's text,
//   - the latest semantic model.
//
// Lives in the UI assembly because it deliberately owns the choice of which
// parser to dispatch by extension (mirrors `TherionWorkspace.ParseFile` but
// without forcing a workspace load for the simple "open one file" case).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace;

namespace TherionProc.Services;

public interface IDocumentService
{
    string? CurrentPath { get; }
    string CurrentText { get; }
    SemanticModel? CurrentSemantics { get; }
    /// <summary>Active document's parsed AST root (Plan §7.3 / M6 #8) — needed for inline edits.</summary>
    TherionFile? CurrentAst { get; }
    WorkspaceSemanticModel? Workspace { get; }
    /// <summary>Parser + semantic diagnostics for the active document (and workspace files when loaded).</summary>
    System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> CurrentDiagnostics { get; }
    /// <summary>Navigation service for the editor (workspace-aware when a workspace is loaded).</summary>
    Therion.Processing.Abstractions.ISymbolNavigationService? CurrentNavigation { get; }
    event EventHandler? DocumentChanged;

    Task OpenFileAsync(string absolutePath, CancellationToken ct = default);
    Task OpenFolderAsync(string folderPath, CancellationToken ct = default);
    /// <summary>Persist <paramref name="newText"/> to the current document and re-parse (Plan §7.3 / M6 #8).</summary>
    Task WriteCurrentTextAsync(string newText, CancellationToken ct = default);
    void Close();
}

public sealed class DocumentService : IDocumentService, IAsyncDisposable
{
    private readonly IProjectEntryPointResolver _resolver;
    private readonly ICommandRegistry? _commands;
    private TherionWorkspace? _workspace;

    public string? CurrentPath { get; private set; }
    public string CurrentText { get; private set; } = string.Empty;
    public SemanticModel? CurrentSemantics { get; private set; }
    public TherionFile? CurrentAst { get; private set; }
    public WorkspaceSemanticModel? Workspace { get; private set; }
    public System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> CurrentDiagnostics { get; private set; } = System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic>.Empty;
    public Therion.Processing.Abstractions.ISymbolNavigationService? CurrentNavigation { get; private set; }
    public event EventHandler? DocumentChanged;

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

        await DisposeWorkspaceAsync().ConfigureAwait(false);

        var text = await File.ReadAllTextAsync(full, ct).ConfigureAwait(false);
        var ext = Path.GetExtension(full).ToLowerInvariant();
        ParseResult<TherionFile> parsed = ext switch
        {
            ".th2"      => new Th2Parser().Parse(full, text),
            ".thconfig" => new ThconfigParser().Parse(full, text),
            ".thc"      => new ThconfigParser().Parse(full, text),
            ".th"       => new ThParser(_commands).Parse(full, text),
            _           => new ThParser(_commands).Parse(full, text),
        };

        CurrentPath = full;
        CurrentText = text;
        CurrentAst = parsed.Value;
        CurrentSemantics = parsed.Value is null ? null : new SemanticBinder().Bind(parsed.Value);
        Workspace = null;
        CurrentNavigation = CurrentSemantics is null
            ? null
            : new SymbolNavigationService(CurrentSemantics);
        var diags = System.Collections.Immutable.ImmutableArray.CreateBuilder<Therion.Core.Diagnostic>();
        diags.AddRange(parsed.Diagnostics);
        if (CurrentSemantics is not null) diags.AddRange(CurrentSemantics.Diagnostics);
        CurrentDiagnostics = diags.ToImmutable();
        Raise();
    }

    public async Task OpenFolderAsync(string folderPath, CancellationToken ct = default)
    {
        var resolved = await _resolver.ResolveAsync(folderPath, ct).ConfigureAwait(false);
        if (resolved.Selected is null) return;

        await DisposeWorkspaceAsync().ConfigureAwait(false);

        _workspace = new TherionWorkspace();
        await _workspace.LoadAsync(resolved.Selected, ct).ConfigureAwait(false);

        var entryParse = _workspace.TryGetFile(resolved.Selected);
        CurrentPath = resolved.Selected;
        CurrentText = File.Exists(resolved.Selected)
            ? await File.ReadAllTextAsync(resolved.Selected, ct).ConfigureAwait(false)
            : string.Empty;
        CurrentAst = entryParse?.Value;
        CurrentSemantics = entryParse?.Value is null ? null : new SemanticBinder().Bind(entryParse.Value);
        Workspace = _workspace.BuildSemanticModel();
        CurrentNavigation = Workspace is null
            ? (CurrentSemantics is null ? null : new SymbolNavigationService(CurrentSemantics))
            : new WorkspaceSymbolNavigationService(Workspace, CurrentSemantics);
        var diags = System.Collections.Immutable.ImmutableArray.CreateBuilder<Therion.Core.Diagnostic>();
        if (entryParse is not null) diags.AddRange(entryParse.Diagnostics);
        if (Workspace is not null) diags.AddRange(Workspace.Diagnostics);
        CurrentDiagnostics = diags.ToImmutable();
        Raise();
    }

    public void Close()
    {
        CurrentPath = null;
        CurrentText = string.Empty;
        CurrentAst = null;
        CurrentSemantics = null;
        Workspace = null;
        CurrentNavigation = null;
        CurrentDiagnostics = System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic>.Empty;
        _ = DisposeWorkspaceAsync();
        Raise();
    }

    public async Task WriteCurrentTextAsync(string newText, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentPath)) return;
        await File.WriteAllTextAsync(CurrentPath, newText, ct).ConfigureAwait(false);
        await OpenFileAsync(CurrentPath, ct).ConfigureAwait(false);
    }

    private async Task DisposeWorkspaceAsync()
    {
        if (_workspace is not null)
        {
            await _workspace.DisposeAsync().ConfigureAwait(false);
            _workspace = null;
        }
    }

    public ValueTask DisposeAsync() => new(DisposeWorkspaceAsync());

    private void Raise() => DocumentChanged?.Invoke(this, EventArgs.Empty);
}
