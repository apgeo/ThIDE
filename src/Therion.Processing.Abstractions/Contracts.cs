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
/// A place where a symbol is <em>aggregated</em>, for the editor's "go to aggregation" navigation:
/// an <c>equate</c> command that ties a station/survey to other stations, or a <c>map</c> command
/// that composes a scrap / sub-map. <see cref="Kind"/> is <c>"equate"</c> or <c>"map"</c>.
/// </summary>
/// <param name="Kind">Aggregation kind: <c>"equate"</c> (stations/surveys) or <c>"map"</c> (scraps/sub-maps).</param>
/// <param name="Span">Source span of the aggregating command.</param>
public sealed record AggregationReference(string Kind, SourceSpan Span);

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

    /// <summary>
    /// Places where <paramref name="reference"/> is aggregated: the <c>equate</c> commands that
    /// reference a station/survey, or the <c>map</c> commands that compose a scrap / sub-map.
    /// Ordered by discovery; empty when there are none. Default implementation returns nothing —
    /// only the workspace-aware service scans equates/maps across files.
    /// </summary>
    /// <remarks>
    /// TODO(nav-choose-reference): when several aggregations exist, the editor currently jumps to
    /// the first and logs the rest. Surface a chooser — a dedicated "find all references" / pick-a-
    /// target window — so the user can select which occurrence to open. See the future-features plan
    /// (.claude/feature-roadmap-ideas.md → NAV-choose-reference).
    /// </remarks>
    ImmutableArray<AggregationReference> FindAggregations(string reference, ReferenceKind kind) =>
        ImmutableArray<AggregationReference>.Empty;
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

/// <summary>
/// A classified line of compiler output. <see cref="Span"/> carries a navigable file (and
/// line, when known) detected in the text; <see cref="Symbol"/> is the offending identifier
/// for Therion errors like "… does not exist -- E65a" (captured for future use, #1).
/// </summary>
public sealed record CompilerOutputLine(string Text, DiagnosticSeverity Severity, SourceSpan? Span, string? Symbol = null);

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

/// <summary>
/// Catalog of well-known command identifiers used by <see cref="IKeyboardShortcutService"/>.
/// <para>
/// Two scopes exist. <em>Shell</em> commands are bound as <c>Window.KeyBindings</c> and fire
/// wherever focus is. <em>Editor</em> commands are caret-scoped: the focused editor matches the
/// configured gesture itself, so they only fire with an editor focused.
/// </para>
/// </summary>
public static class ShellCommandIds
{
    // ---- Shell scope (Window.KeyBindings) ----
    public const string Build              = "Build";
    public const string Rebuild            = "Rebuild";
    public const string CancelBuild        = "CancelBuild";
    public const string OpenInLoch         = "OpenInLoch";
    public const string OpenInAven         = "OpenInAven";
    public const string ToggleWorkspaceExplorer = "ToggleWorkspaceExplorer";
    public const string ToggleDiagnostics  = "ToggleDiagnostics";
    public const string Save               = "Save";
    public const string GoBack             = "GoBack";
    public const string GoForward          = "GoForward";
    public const string FindInFiles        = "FindInFiles";
    public const string ReplaceInFiles     = "ReplaceInFiles";
    public const string QuickOpen          = "QuickOpen";
    public const string CommandPalette     = "CommandPalette";
    public const string ReopenClosedTab    = "ReopenClosedTab";
    public const string ToggleFullScreen   = "ToggleFullScreen";
    public const string NextProblem        = "NextProblem";
    public const string PreviousProblem    = "PreviousProblem";
    public const string NewFile            = "NewFile";
    public const string OpenFile           = "OpenFile";
    public const string OpenFolder         = "OpenFolder";
    public const string OpenThconfig       = "OpenThconfig";
    public const string ToggleObjectBrowser     = "ToggleObjectBrowser";
    public const string ToggleOutline           = "ToggleOutline";
    public const string ToggleProject           = "ToggleProject";
    public const string ToggleLog               = "ToggleLog";
    public const string ToggleLivePreview       = "ToggleLivePreview";
    public const string ToggleMapViewer         = "ToggleMapViewer";
    public const string ToggleModel3DViewer     = "ToggleModel3DViewer";
    public const string ToggleStructuralGeology = "ToggleStructuralGeology";
    public const string ToggleBlenderAnimation  = "ToggleBlenderAnimation";
    public const string SplitEditor        = "SplitEditor";
    public const string ResetLayout        = "ResetLayout";
    public const string FloatActiveDocument = "FloatActiveDocument";
    public const string QuickExport        = "QuickExport";
    public const string OpenOutputFolder   = "OpenOutputFolder";
    public const string ToggleWordWrap     = "ToggleWordWrap";
    public const string NewScrapScaffold   = "NewScrapScaffold";
    public const string GenerateReport     = "GenerateReport";

    // ---- Editor scope (matched by the focused TherionTextEditor) ----
    public const string GoToDefinition     = "GoToDefinition";
    public const string FindReferences     = "FindReferences";
    public const string RenameSymbol       = "RenameSymbol";
    public const string PeekDefinition     = "PeekDefinition";
    public const string GoToMatchingBlock  = "GoToMatchingBlock";
    public const string StepIntoInclude    = "StepIntoInclude";
    public const string StepOutInclude     = "StepOutInclude";
    public const string TriggerCompletion  = "TriggerCompletion";
    public const string GoToLine           = "GoToLine";
    public const string ToggleComment      = "ToggleComment";
    public const string FormatDocument     = "FormatDocument";
    public const string EncloseInRegion    = "EncloseInRegion";
    public const string QuickFixes         = "QuickFixes";
    public const string DuplicateLines     = "DuplicateLines";
    public const string MoveLinesUp        = "MoveLinesUp";
    public const string MoveLinesDown      = "MoveLinesDown";
    public const string SortLines          = "SortLines";
}

