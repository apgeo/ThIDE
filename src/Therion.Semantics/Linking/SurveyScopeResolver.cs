// Survey names in the semantic model are file-local: SemanticBinder starts every file at an empty
// scope and never follows `input`. A file input from inside `survey cave` therefore declares
// `a.st`, while the rest of the project knows that station as `cave.a.st`. Writing an equate that
// another file can resolve needs the effective (workspace-wide) path, which only the include graph
// knows: for each `input` site, the survey blocks around it in the including file.

using System.Collections.Immutable;
using Therion.Syntax;

namespace Therion.Semantics.Linking;

public enum ScopeWarningKind
{
    /// <summary>The file is input from more than one place with different scopes; the first one was used.</summary>
    MultipleParents,
    /// <summary>The file is (indirectly) input from itself; its scope was taken as the root.</summary>
    Cycle,
}

public sealed record ScopeWarning(ScopeWarningKind Kind, string FilePath);

/// <summary>
/// The effective survey scope each workspace file's root sits in. Paths compare case-insensitively
/// after normalisation, like the semantic model's own file keys.
/// </summary>
public sealed class FileScopes
{
    private readonly Dictionary<string, ImmutableArray<string>> _prefix;
    private readonly Dictionary<string, string> _parent;

    internal FileScopes(
        Dictionary<string, ImmutableArray<string>> prefix,
        Dictionary<string, string> parent,
        IReadOnlyList<ScopeWarning> warnings)
    {
        _prefix = prefix;
        _parent = parent;
        Warnings = warnings;
    }

    public IReadOnlyList<ScopeWarning> Warnings { get; }

    /// <summary>The survey path (top-down) a file's root sits in; empty for the entry files.</summary>
    public ImmutableArray<string> PrefixOf(string filePath) =>
        _prefix.TryGetValue(SurveyScopeResolver.Normalize(filePath), out var p) ? p : ImmutableArray<string>.Empty;

    /// <summary>A file-local name made workspace-wide.</summary>
    public QualifiedName Effective(string filePath, QualifiedName local) =>
        new(PrefixOf(filePath).AddRange(local.Parts));

    /// <summary>A file-local survey path (possibly empty: the file root) made workspace-wide.</summary>
    public ImmutableArray<string> EffectiveScope(string filePath, ImmutableArray<string> localScope) =>
        PrefixOf(filePath).AddRange(localScope);

    /// <summary>The file whose <c>input</c> brings this one in (the first, when there are several).</summary>
    public string? EnclosingFileOf(string filePath) =>
        _parent.TryGetValue(SurveyScopeResolver.Normalize(filePath), out var p) ? p : null;
}

public static class SurveyScopeResolver
{
    /// <summary>
    /// Computes every file's scope from the include edges of <paramref name="ws"/>.
    /// </summary>
    /// <param name="ast">The parsed file for a path (the workspace's cached parse, or a fresh one);
    /// null when unavailable — its includes then count as made from the file root.</param>
    public static FileScopes Build(WorkspaceSemanticModel ws, Func<string, TherionFile?> ast)
    {
        // Incoming include edges per target, in the model's (stable) order.
        var incoming = new Dictionary<string, List<FileGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in ws.FileGraphEdges)
        {
            var to = Normalize(edge.To);
            files.Add(to);
            files.Add(Normalize(edge.From));
            if (!incoming.TryGetValue(to, out var list)) incoming[to] = list = [];
            list.Add(edge);
        }
        foreach (var path in ws.PerFile.Keys) files.Add(Normalize(path));

        var asts = new Dictionary<string, TherionFile?>(StringComparer.OrdinalIgnoreCase);
        var prefix = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<ScopeWarning>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase)) Resolve(file);

        return new FileScopes(prefix, parent, warnings);

        ImmutableArray<string> Resolve(string file)
        {
            if (prefix.TryGetValue(file, out var known)) return known;
            if (!visiting.Add(file))
            {
                warnings.Add(new(ScopeWarningKind.Cycle, file));
                return ImmutableArray<string>.Empty;
            }

            var result = ImmutableArray<string>.Empty;
            if (incoming.TryGetValue(file, out var edges))
            {
                bool first = true;
                foreach (var edge in edges)
                {
                    var from = Normalize(edge.From);
                    var scope = Resolve(from).AddRange(SurveyPathAt(AstOf(from), edge.Site.StartOffset));
                    if (first)
                    {
                        result = scope;
                        parent[file] = from;
                        first = false;
                    }
                    else if (!scope.SequenceEqual(result))
                    {
                        warnings.Add(new(ScopeWarningKind.MultipleParents, file));
                        break;
                    }
                }
            }

            visiting.Remove(file);
            // A cycle found below may already have stored a value for this file; keep the first.
            prefix.TryAdd(file, result);
            return prefix[file];
        }

        TherionFile? AstOf(string path)
        {
            if (!asts.TryGetValue(path, out var parsed)) asts[path] = parsed = ast(path);
            return parsed;
        }
    }

    /// <summary>
    /// The file-local survey path (top-down) enclosing <paramref name="offset"/>: the names of every
    /// <c>survey</c> block containing it. Empty at the file root or without an AST.
    /// </summary>
    /// <param name="text">The file's text. With it, a block reaches to the end of its terminator
    /// line (a caret on the blank line before <c>endsurvey</c> is inside); without it, only to the
    /// end of its last child — enough for positions of commands, such as <c>input</c> sites.</param>
    public static ImmutableArray<string> SurveyPathAt(TherionFile? file, int offset, string? text = null) =>
        [.. EnclosingBlocks(file, offset, text).OfType<SurveyCommand>().Select(s => s.Name)];

    /// <summary>The innermost <c>survey</c> block enclosing <paramref name="offset"/>, or null at the file root.</summary>
    public static SurveyCommand? SurveyBlockAt(TherionFile? file, int offset, string? text = null) =>
        EnclosingBlocks(file, offset, text).OfType<SurveyCommand>().LastOrDefault();

    /// <summary>Every block (survey, centreline, group, …) containing the offset, outermost first.</summary>
    public static IReadOnlyList<BlockCommand> EnclosingBlocks(TherionFile? file, int offset, string? text = null)
    {
        var chain = new List<BlockCommand>();
        var children = file?.Children ?? ImmutableArray<TherionNode>.Empty;
        while (true)
        {
            BlockCommand? inner = null;
            foreach (var node in children)
                if (node is BlockCommand block && offset >= block.Span.StartOffset && offset < ExtentEnd(block, text))
                {
                    inner = block;
                    break;
                }
            if (inner is null) return chain;
            chain.Add(inner);
            children = inner.Children;
        }
    }

    /// <summary>
    /// Where a block ends: just past the line break of its terminator, or the end of the text when
    /// it has none. The parser's span stops at the end of the last child's span and leaves the
    /// terminators out, and it marks a block cut off by the end of the file as terminated, so both
    /// are worked out here from the text. Without the text, the span end is all there is.
    /// </summary>
    public static int ExtentEnd(BlockCommand block, string? text)
    {
        if (text is null) return block.Span.StartOffset + block.Span.Length;
        if (TerminatorStart(block, text) is not { } t) return text.Length;
        int lineEnd = text.IndexOf('\n', t);
        return lineEnd < 0 ? text.Length : lineEnd + 1;
    }

    /// <summary>
    /// The start of the line holding a block's terminator (where an insertion "at the end of the
    /// block" goes), or null when the block has no terminator.
    /// </summary>
    public static int? TerminatorLineStart(BlockCommand block, string text) =>
        TerminatorStart(block, text) is { } t ? text.LastIndexOf('\n', Math.Max(t - 1, 0)) + 1 : null;

    /// <summary>
    /// Offset of the terminator keyword (<c>endsurvey</c>, <c>endcentreline</c>, …): the first
    /// non-blank text after the block's last child, when it starts with <c>end</c>.
    /// </summary>
    private static int? TerminatorStart(BlockCommand block, string text)
    {
        int i = block.Span.StartOffset + block.Span.Length;
        if (!block.Children.IsDefaultOrEmpty)
        {
            var last = block.Children[^1];
            i = Math.Max(i, last is BlockCommand inner ? ExtentEnd(inner, text) : last.Span.StartOffset + last.Span.Length);
        }
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i < text.Length && text.AsSpan(i).StartsWith("end", StringComparison.OrdinalIgnoreCase) ? i : null;
    }

    internal static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}
