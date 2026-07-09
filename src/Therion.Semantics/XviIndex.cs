// Implementation Plan �5.1 � XVI index + per-scrap sketch resolution.
// Built from .th2 ScrapBlock nodes (which carry SketchReferences) and the
// parsed .xvi files known to the workspace.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>One entry in the XVI cross-file index.</summary>
public sealed record XviSymbol(
    string ResolvedXviPath,
    XviFile File,
    ImmutableArray<SourceSpan> ReferencingScraps);

/// <summary>Result of indexing .xvi + sketch references across the workspace.</summary>
public sealed class XviIndex
{
    public FrozenDictionary<string, XviSymbol> ByPath { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    /// <summary>Directed edges <c>(from .th2 path) ? (to .xvi path)</c>, with the sketch's span.</summary>
    public ImmutableArray<FileGraphEdge> FileGraphEdges { get; }

    public XviIndex(
        FrozenDictionary<string, XviSymbol> byPath,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArray<FileGraphEdge> edges)
    {
        ByPath = byPath;
        Diagnostics = diagnostics;
        FileGraphEdges = edges;
    }

    public static XviIndex Empty { get; } = new(
        FrozenDictionary<string, XviSymbol>.Empty,
        ImmutableArray<Diagnostic>.Empty,
        ImmutableArray<FileGraphEdge>.Empty);

    /// <summary>
    /// Build an index from a collection of parsed XVI files plus the set of
    /// <c>.th2</c> AST roots whose scraps may reference them.
    /// </summary>
    /// <param name="fileExists">
    /// File-existence probe (injected for testability). Defaults to <see cref="File.Exists"/>.
    /// </param>
    public static XviIndex Build(
        IReadOnlyCollection<XviFile> xviFiles,
        IReadOnlyCollection<TherionFile> th2Files,
        Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var edges = ImmutableArray.CreateBuilder<FileGraphEdge>();
        var byPath = new Dictionary<string, XviSymbol>(StringComparer.OrdinalIgnoreCase);
        var refsByXvi = new Dictionary<string, ImmutableArray<SourceSpan>.Builder>(StringComparer.OrdinalIgnoreCase);

        // First, walk all th2 scraps and collect their sketch references.
        foreach (var th2 in th2Files)
        {
            foreach (var scrap in WalkScraps(th2.Children))
            {
                foreach (var sk in scrap.Sketches)
                {
                    var th2Dir = Path.GetDirectoryName(th2.Path) ?? string.Empty;
                    var resolved = ResolveRelative(th2Dir, sk.XviPath);
                    edges.Add(new FileGraphEdge(th2.Path, resolved, sk.Span));
                    if (!refsByXvi.TryGetValue(resolved, out var list))
                        refsByXvi[resolved] = list = ImmutableArray.CreateBuilder<SourceSpan>();
                    list.Add(sk.Span);

                    if (!fileExists(resolved))
                    {
                        diags.Add(Diagnostic.Create(
                            SemanticDiagnosticCodes.XviFileMissing,
                            DiagnosticSeverity.Error,
                            $"Referenced .xvi file '{sk.XviPath}' not found.", sk.Span));
                    }
                }
            }
        }

        // Then, build XviSymbol entries for every parsed .xvi. The `set XVI*` format is
        // self-contained vector data — it carries no external image reference or affine
        // transform — so the only cross-file check is the .th2 → sketch-target resolution above.
        foreach (var xvi in xviFiles)
        {
            var refs = refsByXvi.TryGetValue(xvi.Path, out var b)
                ? b.ToImmutable() : ImmutableArray<SourceSpan>.Empty;

            byPath[xvi.Path] = new XviSymbol(xvi.Path, xvi, refs);
        }

        return new XviIndex(byPath.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            diags.ToImmutable(), edges.ToImmutable());
    }

    private static IEnumerable<ScrapBlock> WalkScraps(ImmutableArray<TherionNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n is ScrapBlock s)
            {
                yield return s;
                foreach (var inner in WalkScraps(s.Children)) yield return inner;
            }
            else if (n is BlockCommand b)
            {
                foreach (var inner in WalkScraps(b.Children)) yield return inner;
            }
        }
    }

    private static string ResolveRelative(string baseDir, string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }
}
