// Implementation Plan §5 / §6 — workspace-level semantic snapshot.
// Combines all per-file SemanticModels with cross-file indexes (XviIndex,
// FileGraph). Immutable; rebuilt atomically (§18 — UI threading).

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>
/// Cross-file semantic snapshot for an entire workspace.
/// Aggregates per-file <see cref="SemanticModel"/> instances and the
/// workspace-wide <see cref="XviIndex"/> + FileGraph edges.
/// </summary>
public sealed class WorkspaceSemanticModel
{
    public FrozenDictionary<string, SemanticModel> PerFile { get; }
    public XviIndex Xvi { get; }
    /// <summary>All cross-file edges (`.thconfig source`, `.th input`/`load`, `.th2 sketch`).</summary>
    public ImmutableArray<(string From, string To)> FileGraphEdges { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public WorkspaceSemanticModel(
        FrozenDictionary<string, SemanticModel> perFile,
        XviIndex xvi,
        ImmutableArray<(string From, string To)> edges,
        ImmutableArray<Diagnostic> diagnostics)
    {
        PerFile = perFile;
        Xvi = xvi;
        FileGraphEdges = edges;
        Diagnostics = diagnostics;
    }

    public static WorkspaceSemanticModel Empty { get; } = new(
        FrozenDictionary<string, SemanticModel>.Empty,
        XviIndex.Empty,
        ImmutableArray<(string, string)>.Empty,
        ImmutableArray<Diagnostic>.Empty);

    /// <summary>
    /// Build a workspace-level snapshot from raw <see cref="ParseResult{TherionFile}"/>
    /// per file plus their parsed XVI counterparts.
    /// </summary>
    public static WorkspaceSemanticModel Build(
        IReadOnlyDictionary<string, ParseResult<TherionFile>> parsedFiles,
        IReadOnlyCollection<XviFile> xviFiles,
        System.Func<string, bool>? fileExists = null)
    {
        var binder = new SemanticBinder();
        var perFile = new Dictionary<string, SemanticModel>(System.StringComparer.OrdinalIgnoreCase);
        var allDiags = ImmutableArray.CreateBuilder<Diagnostic>();
        var graphEdges = ImmutableArray.CreateBuilder<(string, string)>();
        var th2Files = new List<TherionFile>();

        foreach (var (path, parse) in parsedFiles)
        {
            allDiags.AddRange(parse.Diagnostics);
            if (parse.Value is null) continue;

            // Collect .th2 trees for the XVI index.
            if (path.EndsWith(".th2", System.StringComparison.OrdinalIgnoreCase))
                th2Files.Add(parse.Value);

            // Bind per-file semantics (.th only — others have no station model yet).
            if (path.EndsWith(".th", System.StringComparison.OrdinalIgnoreCase))
            {
                var model = binder.Bind(parse.Value);
                perFile[path] = model;
                allDiags.AddRange(model.Diagnostics);
            }

            // Source / input edges from .thconfig and .th files.
            CollectSourceEdges(path, parse.Value, graphEdges);
        }

        var xvi = XviIndex.Build(xviFiles, th2Files, fileExists);
        allDiags.AddRange(xvi.Diagnostics);
        foreach (var edge in xvi.FileGraphEdges) graphEdges.Add(edge);

        return new WorkspaceSemanticModel(
            perFile.ToFrozenDictionary(System.StringComparer.OrdinalIgnoreCase),
            xvi,
            graphEdges.ToImmutable(),
            allDiags.ToImmutable());
    }

    private static void CollectSourceEdges(
        string parentPath, TherionFile file,
        ImmutableArray<(string, string)>.Builder edges)
    {
        var dir = System.IO.Path.GetDirectoryName(parentPath) ?? string.Empty;
        foreach (var child in file.Children)
        {
            if (child is not UnknownCommand cmd) continue;
            if (!IsSourceLike(cmd.Keyword)) continue;
            foreach (var token in SplitArgs(cmd.RawArguments))
            {
                var dep = System.IO.Path.IsPathRooted(token)
                    ? token
                    : System.IO.Path.Combine(dir, token);
                edges.Add((parentPath, System.IO.Path.GetFullPath(dep)));
            }
        }
    }

    private static bool IsSourceLike(string keyword)
        => string.Equals(keyword, "source", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(keyword, "input", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(keyword, "load", System.StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitArgs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        int i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length) yield break;
            if (raw[i] == '"')
            {
                int end = raw.IndexOf('"', ++i);
                if (end < 0) { yield return raw[i..]; yield break; }
                yield return raw[i..end];
                i = end + 1;
            }
            else
            {
                int start = i;
                while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
                yield return raw[start..i];
            }
        }
    }
}
