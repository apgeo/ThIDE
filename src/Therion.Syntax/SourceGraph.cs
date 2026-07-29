// Cross-file inclusion graph — the single source of truth for "which files does
// this file pull in?". Used by both TherionWorkspace (BFS load) and
// WorkspaceSemanticModel (FileGraph edges) so the two never disagree.
//
// Therion links files two ways:
//   * .thconfig `source <path>` (parsed as UnknownCommand)
//   * .th `input`/`load <path>` (parsed as the typed InputCommand) — crucially,
//     these are usually nested *inside* `survey` / `centreline` blocks, so a
//     top-level-only scan misses almost the entire project.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// Walks a parsed <see cref="TherionFile"/> (recursing into survey / centreline /
/// scrap blocks) and yields the files it includes via <c>source</c> / <c>input</c> /
/// <c>load</c> directives.
/// </summary>
public static class SourceGraph
{
    /// <summary>Raw dependency path tokens (verbatim, as written) found anywhere in the tree.</summary>
    public static IEnumerable<string> DependencyTokens(TherionFile file)
        => DependencyTokenSites(file).Select(t => t.Token);

    /// <summary>
    /// Raw dependency path tokens paired with the span of the <c>source</c>/<c>input</c>/<c>load</c>
    /// command that declares them, so a caller can point a diagnostic at the offending line. A
    /// multi-argument <c>source a.th b.th</c> yields one site per token, all sharing the command's
    /// span: the raw argument text carries no per-token offsets.
    /// </summary>
    public static IEnumerable<(string Token, SourceSpan Span)> DependencyTokenSites(TherionFile file)
    {
        foreach (var node in Descendants(file.Children))
        {
            switch (node)
            {
                // .th `input`/`load` (typed) — often nested inside a survey block.
                case InputCommand input when !string.IsNullOrWhiteSpace(input.Path):
                    yield return (input.Path, input.Span);
                    break;
                // .thconfig `source`/`input`/`load` (raw).
                case UnknownCommand cmd when IsSourceLike(cmd.Keyword):
                    foreach (var token in SplitArgs(cmd.RawArguments))
                        yield return (token, cmd.Span);
                    break;
            }
        }
    }

    /// <summary>
    /// Dependency tokens resolved to absolute paths, relative to
    /// <paramref name="parentPath"/> (falling back to the file's own
    /// <see cref="TherionFile.Path"/>). Both <c>/</c> and <c>\</c> separators are
    /// normalized to the host separator so Windows-style <c>date\x.th</c> paths
    /// resolve on every platform. A token with no extension gets Therion's default
    /// <c>.th</c> (so <c>input B</c> resolves to <c>B.th</c>); see <see cref="DependencySites"/>.
    /// Pass <paramref name="exists"/> to resolve against real files instead — see
    /// <see cref="ResolveIncludePath"/>.
    /// </summary>
    public static IEnumerable<string> Dependencies(
        TherionFile file, string? parentPath = null, Func<string, bool>? exists = null)
        => DependencySites(file, parentPath, exists).Select(d => d.Path);

    /// <summary>
    /// <see cref="Dependencies"/> with the span of the command that pulls each file in — what a
    /// "file not found" diagnostic needs to underline the right line rather than the top of the file.
    /// With no <paramref name="exists"/> probe an extensionless target unconditionally gets the
    /// <c>.th</c> default; with one it is resolved like Therion resolves it (see
    /// <see cref="ResolveIncludePath"/>), so <c>input B</c> can land on an on-disk <c>B.th2</c>.
    /// </summary>
    public static IEnumerable<(string Path, SourceSpan Span)> DependencySites(
        TherionFile file, string? parentPath = null, Func<string, bool>? exists = null)
    {
        var dir = Path.GetDirectoryName(parentPath ?? file.Path) ?? string.Empty;
        foreach (var (token, span) in DependencyTokenSites(file))
        {
            var rel = token.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            string full;
            try
            {
                var combined = Path.IsPathRooted(rel) ? rel : Path.Combine(dir, rel);
                var absolute = Path.GetFullPath(combined);
                full = exists is null
                    ? WithDefaultIncludeExtension(absolute)
                    : ResolveIncludePath(absolute, exists);
            }
            catch { continue; } // malformed path token — skip rather than throw.
            yield return (full, span);
        }
    }

    /// <summary>
    /// Ordered lookup candidates for an include target, mirroring Therion's own open sequence
    /// (thinput tries the literal name first, then the <c>.th:.th2</c> suffix list set by
    /// thdatareader): the path as written, then — only when it carries no extension — the
    /// <c>.th</c> and <c>.th2</c> variants. The single definition of that order; every
    /// filesystem-aware resolver (workspace BFS, reachability, editor ctrl-click) consumes it
    /// so they can never disagree about which file an include names.
    /// </summary>
    public static IEnumerable<string> IncludeCandidates(string path)
    {
        yield return path;
        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            yield return path + ".th";
            yield return path + ".th2";
        }
    }

    /// <summary>
    /// Resolves an include target against an existence probe: the first
    /// <see cref="IncludeCandidates"/> hit wins; when nothing exists the <c>.th</c>-defaulted
    /// path is returned so diagnostics and create-file fixes still name the canonical target.
    /// The probe keeps this layer free of file I/O — callers pass <c>File.Exists</c> (or a
    /// loaded-file set) from where I/O is allowed.
    /// </summary>
    public static string ResolveIncludePath(string path, Func<string, bool> exists)
    {
        foreach (var candidate in IncludeCandidates(path))
            if (exists(candidate)) return candidate;
        return WithDefaultIncludeExtension(path);
    }

    /// <summary>
    /// Applies Therion's default include extension: a target whose extension is omitted gets
    /// <c>.th</c> (so <c>input B</c> resolves to <c>B.th</c> — ThBook: "Default extension is
    /// `.th' and may be omitted"); a target that already carries an extension (<c>B.th</c>, a
    /// <c>.th2</c> scrap) is returned unchanged, so there is never a double append. Therion itself
    /// opens the literal name first and only falls back to <c>.th</c>/<c>.th2</c> when it is missing
    /// on disk — that disk-aware behavior is <see cref="ResolveIncludePath"/>; this is the pure
    /// no-probe default (unconditional <c>.th</c> matches every idiomatic project — an extensionless
    /// survey file is not standard Therion usage). Any code that resolves an include target OUTSIDE
    /// <see cref="DependencySites"/> (e.g. a "create missing file" quick-fix) must route through this
    /// so it agrees with the include graph.
    /// </summary>
    public static string WithDefaultIncludeExtension(string path)
        => string.IsNullOrEmpty(Path.GetExtension(path)) ? path + ".th" : path;

    /// <summary>Depth-first enumeration of a node list, descending into every block body.</summary>
    private static IEnumerable<TherionNode> Descendants(ImmutableArray<TherionNode> nodes)
    {
        if (nodes.IsDefaultOrEmpty) yield break;
        foreach (var node in nodes)
        {
            yield return node;
            if (node is BlockCommand block)
                foreach (var child in Descendants(block.Children))
                    yield return child;
        }
    }

    private static bool IsSourceLike(string keyword)
        => string.Equals(keyword, "source", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(keyword, "input", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(keyword, "load", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits a raw argument string into whitespace- or quote-delimited tokens.</summary>
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
