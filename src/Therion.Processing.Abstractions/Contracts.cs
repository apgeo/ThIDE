// Implementation Plan �1 (decoupling), �4.4 (extensibility),
// �5.3 (semantic rule plugins), �9bis (toolchain abstractions).
// This project intentionally has zero implementation � only public contracts.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Processing.Abstractions;

/// <summary>Parses a single Therion source file into an AST + diagnostics.</summary>
public interface ITherionParser
{
    /// <summary>Parses <paramref name="text"/> as the format identified by <paramref name="filePath"/>'s extension.</summary>
    ValueTask<IParseResult> ParseAsync(string filePath, string text, CancellationToken cancellationToken = default);
}

/// <summary>Format-agnostic parse result envelope.</summary>
public interface IParseResult
{
    string FilePath { get; }
    ImmutableArray<Diagnostic> Diagnostics { get; }
    bool HasErrors { get; }
}

/// <summary>An open workspace / project (set of loaded files + cross-file model).</summary>
public interface IWorkspace : IAsyncDisposable
{
    string? EntryPointPath { get; }
    ImmutableArray<string> LoadedFiles { get; }
    event EventHandler? WorkspaceChanged;

    ValueTask LoadAsync(string entryPointPath, CancellationToken cancellationToken = default);
    void InvalidateAll();
}

/// <summary>Resolves the entry-point file (see Implementation Plan �6.1).</summary>
public interface IProjectEntryPointResolver
{
    ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IProjectEntryPointResolver.ResolveAsync"/>.</summary>
public sealed record EntryPointResolution(
    ImmutableArray<string> Candidates,
    string? Selected,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>Cheap "is this a .thconfig?" probe used by entry-point discovery.</summary>
public interface IThconfigSniffer
{
    SnifferVerdict Probe(string filePath);
}

public enum SnifferVerdict { Likely, Unlikely, Unknown }

/// <summary>Looks up symbols across the loaded workspace.</summary>
public interface ISymbolIndex
{
    bool TryResolve(string qualifiedName, out SourceSpan declarationSpan);
    ImmutableArray<SourceSpan> FindReferences(string qualifiedName);
}

/// <summary>
/// What kind of entity a textual reference points at. Lets the editor disambiguate
/// the two halves of Therion's <c>point@survey</c> / <c>map@survey</c> notation and
/// hint the resolver (e.g. a <c>join</c> id is a scrap object, not a station).
/// </summary>
public enum ReferenceKind
{
    /// <summary>Try every kind (station, survey, map, scrap object) in turn.</summary>
    Any,
    /// <summary>A station (the <c>point</c> half of <c>point@survey</c>; equate/fix/station ids).</summary>
    Station,
    /// <summary>A survey (the half after <c>@</c>, or a bare survey-name reference).</summary>
    Survey,
    /// <summary>A map (the <c>map</c> half of <c>map@survey</c> in a <c>select</c>).</summary>
    Map,
    /// <summary>A scrap or scrap object / point-line id (the targets of <c>join</c>).</summary>
    ScrapObject,
}

/// <summary>Resolved information about a reference, for the editor's hover overlay.</summary>
/// <param name="Kind">Human-readable kind (station / survey / map / scrap / file).</param>
/// <param name="Declaration">Where it is declared.</param>
public sealed record ReferenceInfo(string Kind, SourceSpan Declaration);

/// <summary>
/// UI-facing navigation service (Implementation Plan �7.3): the backing model
/// for Go to Definition (F12) and Find All References (Shift+F12).
/// </summary>
public interface ISymbolNavigationService
{
    /// <summary>
    /// Describes what a reference resolves to (kind + declaration) for hover info.
    /// Returns null when it doesn't resolve. Default implementation reports nothing.
    /// </summary>
    ReferenceInfo? Describe(string reference, ReferenceKind kind) => null;

    /// <summary>Returns the declaration span of <paramref name="qualifiedName"/>, or <c>null</c> if not found.</summary>
    SourceSpan? GoToDefinition(string qualifiedName);

    /// <summary>
    /// Returns the declaration span of a reference, using <paramref name="kind"/> to
    /// resolve Therion's <c>@</c> notation across files. The default implementation
    /// ignores the hint and falls back to <see cref="GoToDefinition(string)"/> — only
    /// the workspace-aware service implements true cross-file reference resolution.
    /// </summary>
    SourceSpan? GoToDefinition(string reference, ReferenceKind kind) => GoToDefinition(reference);

    /// <summary>
    /// Cheap "would this reference resolve?" check for the editor's hover affordance
    /// (so it only underlines genuinely-navigable tokens). Implementations should avoid
    /// expensive scans here. Defaults to a <see cref="GoToDefinition(string, ReferenceKind)"/> probe.
    /// </summary>
    bool CanNavigate(string reference, ReferenceKind kind) => GoToDefinition(reference, kind) is not null;

    /// <summary>Returns all reference spans of <paramref name="qualifiedName"/> (may be empty).</summary>
    ImmutableArray<SourceSpan> FindReferences(string qualifiedName);
}

/// <summary>Locates installed Therion / Loch / Aven executables (�9bis.1).</summary>
public interface IExternalToolLocator
{
    ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default);
}

/// <summary>Information about a discovered external tool.</summary>
public sealed record ToolInfo(string ToolId, string Path, string? Version, string Source);

/// <summary>
/// User-configured override paths for <see cref="IExternalToolLocator"/> (�9bis.5 / D #29).
/// When an entry is set, <see cref="IExternalToolLocator.FindAsync"/> short-circuits to it.
/// </summary>
public interface IExternalToolPathOverrides
{
    /// <summary>The current toolId ? absolute-path map (snapshot, may be empty).</summary>
    IReadOnlyDictionary<string, string> Overrides { get; }

    /// <summary>Set an override path; pass <c>null</c>/empty to clear.</summary>
    void Set(string toolId, string? path);

    /// <summary>Raised after persisting a change.</summary>
    event EventHandler? OverridesChanged;
}

/// <summary>Drives the external Therion compiler (�9bis.2).</summary>
public interface ITherionCompiler
{
    ValueTask<CompileResult> CompileAsync(
        string entryPointPath,
        IProgress<CompilerOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record CompilerOutputLine(string Text, DiagnosticSeverity Severity, SourceSpan? Span);

public sealed record CompileResult(
    int ExitCode,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<OutputArtifact> Artifacts);

public sealed record OutputArtifact(string Path, string Kind, long SizeBytes, DateTimeOffset LastWriteUtc);

/// <summary>
/// Configurable keyboard shortcut map for shell commands (Plan �9bis.5a / Decision #29).
/// Gesture strings use Avalonia syntax (e.g. <c>"F5"</c>, <c>"Ctrl+F5"</c>).
/// </summary>
public interface IKeyboardShortcutService
{
    /// <summary>The current command-id ? gesture-string map (snapshot).</summary>
    IReadOnlyDictionary<string, string> Gestures { get; }

    /// <summary>Built-in defaults from the plan; never mutated.</summary>
    IReadOnlyDictionary<string, string> Defaults { get; }

    /// <summary>Sets a single gesture and persists to user-profile storage.</summary>
    void Set(string commandId, string gesture);

    /// <summary>Resets a single command to its plan default and persists.</summary>
    void ResetToDefault(string commandId);

    /// <summary>Resets every command to its plan default and persists.</summary>
    void ResetAllToDefaults();

    /// <summary>Raised when any gesture changes (after persistence).</summary>
    event EventHandler? GesturesChanged;
}

/// <summary>Catalog of well-known command identifiers used by <see cref="IKeyboardShortcutService"/>.</summary>
public static class ShellCommandIds
{
    public const string Build              = "Build";
    public const string Rebuild            = "Rebuild";
    public const string CancelBuild        = "CancelBuild";
    public const string OpenInLoch         = "OpenInLoch";
    public const string OpenInAven         = "OpenInAven";
    public const string GoToDefinition     = "GoToDefinition";
    public const string FindReferences     = "FindReferences";
    public const string ToggleWorkspaceExplorer = "ToggleWorkspaceExplorer";
    public const string ToggleDiagnostics  = "ToggleDiagnostics";
}

