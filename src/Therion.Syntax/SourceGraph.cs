// Cross-file inclusion graph — the single source of truth for "which files does
// this file pull in?". Used by both TherionWorkspace (BFS load) and
// WorkspaceSemanticModel (FileGraph edges) so the two never disagree.
//
// Therion links files two ways:
//   * .thconfig `source <path>` (parsed as UnknownCommand)
//   * .th `input`/`load <path>` (parsed as the typed InputCommand) — crucially,
//     these are usually nested *inside* `survey` / `centreline` blocks, so a
//     top-level-only scan misses almost the entire project.

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
    /// resolve on every platform.
    /// </summary>
    public static IEnumerable<string> Dependencies(TherionFile file, string? parentPath = null)
        => DependencySites(file, parentPath).Select(d => d.Path);

    /// <summary>
    /// <see cref="Dependencies"/> with the span of the command that pulls each file in — what a
    /// "file not found" diagnostic needs to underline the right line rather than the top of the file.
    /// </summary>
    public static IEnumerable<(string Path, SourceSpan Span)> DependencySites(
        TherionFile file, string? parentPath = null)
    {
        var dir = Path.GetDirectoryName(parentPath ?? file.Path) ?? string.Empty;
        foreach (var (token, span) in DependencyTokenSites(file))
        {
            var rel = token.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            string full;
            try
            {
                var combined = Path.IsPathRooted(rel) ? rel : Path.Combine(dir, rel);
                full = Path.GetFullPath(combined);
            }
            catch { continue; } // malformed path token — skip rather than throw.
            yield return (full, span);
        }
    }

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
