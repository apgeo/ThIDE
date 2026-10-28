// Plans the lines that link a survey file into a workspace: an `input` that brings the file in, and
// an `equate` that ties one of its stations to a station already in the project. Pure: every file
// read goes through LinkContext, and the result is an EditBatch for the caller to apply.

using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Import;

namespace Therion.Workspace.Linking;

public static class SurveyLinkPlanner
{
    /// <summary>
    /// The scope a new survey joins when linked to <paramref name="workspaceStation"/> (effective
    /// name): the parent of the station's survey, so the new survey becomes its sibling — or, with
    /// <paramref name="nest"/>, the station's survey itself. A root survey is its own default.
    /// </summary>
    public static ImmutableArray<string> DefaultScope(QualifiedName workspaceStation, bool nest)
    {
        var survey = workspaceStation.Parts.RemoveAt(workspaceStation.Parts.Length - 1);
        return nest || survey.Length <= 1 ? survey : survey.RemoveAt(survey.Length - 1);
    }

    /// <summary>
    /// Files that could receive the lines for <paramref name="scope"/>, best first: the survey block
    /// that already holds equates at that level, then a file of bare equates input from inside it,
    /// then the block itself; then the same for each enclosing scope, ranked lower.
    /// </summary>
    public static IReadOnlyList<DestinationCandidate> Candidates(LinkContext ctx, ImmutableArray<string> scope)
    {
        var result = new List<DestinationCandidate>();
        var seen = new HashSet<(string, string)>();
        for (int level = scope.Length, distance = 0; level >= 0; level--, distance++)
        {
            var target = scope.RemoveRange(level, scope.Length - level);
            foreach (var path in ctx.Workspace.PerFile.Keys.Order(StringComparer.OrdinalIgnoreCase))
            {
                if (ctx.Ast(path) is not { } ast) continue;
                var prefix = ctx.Scopes.PrefixOf(path);
                if (!StationReference.IsPrefix(prefix.AsSpan(), target.AsSpan())) continue;
                var local = target.RemoveRange(0, prefix.Length);

                if (local.IsEmpty)
                {
                    // The file's root sits at this scope: a candidate only as a file of bare equates.
                    if (ast.Children.OfType<EquateCommand>().Any())
                        Add(path, local, target, DestinationReason.EquateFile, distance);
                    continue;
                }
                if (FindBlock(ast, local) is not { } block) continue;
                bool hasEquates = block.Children.OfType<EquateCommand>().Any();
                Add(path, local, target, hasEquates ? DestinationReason.BlockWithEquates : DestinationReason.Block, distance);
            }
        }
        return result.OrderBy(c => c.Rank).ThenBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase).ToList();

        void Add(string path, ImmutableArray<string> local, ImmutableArray<string> effective, DestinationReason reason, int distance)
        {
            if (seen.Add((path, string.Join('.', local))))
                result.Add(new(path, local, effective, reason, distance * 3 + (int)reason));
        }
    }

    /// <summary>
    /// The folder a source is copied into by default: the folder most of the destination block's
    /// <c>input</c> targets live in, else the destination file's folder.
    /// </summary>
    public static string DefaultTargetDirectory(LinkContext ctx, LinkDestination destination)
    {
        var fileDir = Path.GetDirectoryName(Path.GetFullPath(destination.FilePath))!;
        if (ctx.Ast(destination.FilePath) is not { } ast) return fileDir;

        var children = destination.LocalScope.IsEmpty ? ast.Children : FindBlock(ast, destination.LocalScope)?.Children;
        var dirs = (children ?? ImmutableArray<TherionNode>.Empty).OfType<InputCommand>()
            .Select(i => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(fileDir, i.Path.Trim('"'))))!)
            .GroupBy(d => d, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        return dirs?.Key ?? fileDir;
    }

    /// <summary>
    /// The sketches beside a source file: every <c>.th2</c> in its folder (and below), with whether the
    /// source inputs it — those are the ones its own <c>input</c> lines need copied along.
    /// </summary>
    public static IReadOnlyList<(string Path, bool Referenced)> Sketches(SourceSurvey source, IEnumerable<string> th2FilesBeside)
    {
        var referenced = InputTargets(source).ToHashSet(FilePaths.Comparer);
        return th2FilesBeside
            .Select(p => Path.GetFullPath(p))
            .Order(StringComparer.Ordinal)
            .Select(p => (p, referenced.Contains(p)))
            .ToList();
    }

    public static LinkPlan Plan(LinkContext ctx, LinkRequest request)
    {
        var warnings = new List<LinkWarning>();
        var dest = request.Destination;
        var opts = request.Options;

        if (request.Members.Count < 2 && opts.InsertEquate || !opts.InsertEquate && !opts.InsertInput)
            warnings.Add(new(LinkWarningCode.NothingToLink));

        // Where the source lands, and the input line that reaches it from the destination file.
        var sourceFull = Path.GetFullPath(request.Source.FilePath);
        var sourceDir = Path.GetDirectoryName(sourceFull)!;
        var target = request.Copy is { CopySource: true, TargetDirectory: { } td }
            ? Path.GetFullPath(Path.Combine(td, Path.GetFileName(sourceFull)))
            : sourceFull;
        var destDir = Path.GetDirectoryName(Path.GetFullPath(dest.FilePath))!;
        string? inputLine = opts.InsertInput ? Th2Scaffold.InputLine(RelativePath(destDir, target)) : null;

        // Equate: every member named from the destination scope.
        var scope = ctx.Scopes.EffectiveScope(dest.FilePath, dest.LocalScope);
        var effective = request.Members.Select(m => m.Side == LinkSide.Source
            ? new QualifiedName(scope.AddRange(m.LocalStation.Parts))
            : ctx.Scopes.Effective(m.FilePath, m.LocalStation)).ToList();
        string? equateLine = null;
        if (opts.InsertEquate && request.Members.Count >= 2)
        {
            var refs = effective.Select(e => StationReference.Format(e, scope)).ToList();
            if (refs.Any(r => r is null)) warnings.Add(new(LinkWarningCode.ScopeNotAncestor));
            else equateLine = "equate " + string.Join(' ', refs);
        }

        // Duplicates (warnings the user may override).
        if (inputLine is not null)
            foreach (var edge in ctx.Workspace.FileGraphEdges)
                if (SamePath(edge.To, target)) { warnings.Add(new(LinkWarningCode.InputExists, edge.From)); break; }
        if (equateLine is not null && FindSameEquate(ctx, effective) is { } existing)
            warnings.Add(new(LinkWarningCode.EquateExists, existing));

        // Copies.
        var copies = new List<FileCopy>();
        if (request.Copy.CopySource && !SamePath(target, sourceFull))
        {
            if (!ctx.FileExists(target)) copies.Add(new(sourceFull, target));
            else if (ctx.SameContent?.Invoke(sourceFull, target) != true) warnings.Add(new(LinkWarningCode.TargetExists, target));

            var targetDir = Path.GetDirectoryName(target)!;
            var copied = new HashSet<string>(FilePaths.Comparer);
            foreach (var sketch in request.Copy.Sketches)
            {
                var full = Path.GetFullPath(sketch);
                var sketchTarget = Path.GetFullPath(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, full)));
                copied.Add(full);
                if (!ctx.FileExists(sketchTarget)) copies.Add(new(full, sketchTarget));
                else if (ctx.SameContent?.Invoke(full, sketchTarget) != true) warnings.Add(new(LinkWarningCode.SketchExists, sketchTarget));
            }
            foreach (var needed in InputTargets(request.Source))
                if (!copied.Contains(needed)) warnings.Add(new(LinkWarningCode.SketchNotCopied, needed));
        }

        // Text edits in the destination file.
        var text = ctx.ReadText(dest.FilePath) ?? "";
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var comment = opts.Comment is { Length: > 0 } c ? "# " + c : null;
        var edits = new List<TextEdit>();
        var shown = new List<string>();
        if (!warnings.Any(w => w.Blocks) && (inputLine ?? equateLine) is not null)
        {
            var ast = new ThParser().Parse(dest.FilePath, text).Value;
            if (Anchors(ast, text, dest.LocalScope, warnings) is { } anchors)
            {
                // The equate follows the last equate of the block; with none, it follows the input.
                bool together = equateLine is null || inputLine is null || anchors.Equate is null;
                if (together)
                {
                    var lines = new List<string>();
                    if (comment is not null) lines.Add(comment);
                    if (inputLine is not null) lines.Add(inputLine);
                    if (equateLine is not null) lines.Add(equateLine);
                    var at = inputLine is not null ? anchors.Input : anchors.Equate ?? anchors.Input;
                    edits.Add(Insert(text, at, lines, newline));
                    shown.AddRange(lines);
                }
                else
                {
                    var inputLines = comment is null ? new List<string> { inputLine! } : [comment, inputLine!];
                    var equateLines = comment is null ? new List<string> { equateLine! } : [comment, equateLine!];
                    edits.Add(Insert(text, anchors.Input, inputLines, newline));
                    edits.Add(Insert(text, anchors.Equate!, equateLines, newline));
                    shown.AddRange(inputLines);
                    shown.AddRange(equateLines);
                }
            }
        }

        var batch = edits.Count == 0 && copies.Count == 0
            ? EditBatch.Empty
            : new EditBatch(
                edits.Count == 0 ? [] : [new FileTextEdits(dest.FilePath, edits)],
                [],
                copies,
                RequiresWorkspaceReload: inputLine is not null && edits.Count > 0);

        var preview = shown.Count > 0
            ? string.Join(newline, shown)
            : string.Join(newline, new[] { comment, inputLine, equateLine }.Where(l => l is not null));
        return new LinkPlan(inputLine, equateLine, preview, batch, warnings);
    }

    /// <summary>Where the input and the equate go in the destination, with the indentation to use.</summary>
    private sealed record Anchor(int Offset, string Indent, bool NeedsLeadingNewline);

    private sealed record AnchorPair(Anchor Input, Anchor? Equate);

    private static AnchorPair? Anchors(TherionFile? ast, string text, ImmutableArray<string> localScope, List<LinkWarning> warnings)
    {
        ImmutableArray<TherionNode> children;
        Anchor header;
        if (localScope.IsEmpty)
        {
            children = ast?.Children ?? ImmutableArray<TherionNode>.Empty;
            var encoding = children.OfType<TherionCommand>().FirstOrDefault(c => c.Keyword == "encoding");
            header = encoding is null ? new Anchor(0, "", false) : AfterLine(text, encoding, "");
        }
        else
        {
            if (FindBlock(ast, localScope) is not { } block)
            {
                warnings.Add(new(LinkWarningCode.ScopeNotFound, string.Join('.', localScope)));
                return null;
            }
            if (SurveyScopeResolver.TerminatorLineStart(block, text) is null)
            {
                warnings.Add(new(LinkWarningCode.UnterminatedBlock, block.Name));
                return null;
            }
            children = block.Children;
            header = AfterLine(text, block, IndentOf(text, block.Span.StartOffset) + "  ", headerOnly: true);
        }

        var lastInput = children.OfType<InputCommand>().LastOrDefault();
        var lastEquate = children.OfType<EquateCommand>().LastOrDefault();
        var input = lastInput is null ? header : AfterLine(text, lastInput, IndentOf(text, lastInput.Span.StartOffset));
        var equate = lastEquate is null ? null : AfterLine(text, lastEquate, IndentOf(text, lastEquate.Span.StartOffset));
        return new AnchorPair(input, equate);
    }

    /// <summary>The offset just past the line a node ends on (or its first line, for a block header).</summary>
    private static Anchor AfterLine(string text, TherionNode node, string indent, bool headerOnly = false)
    {
        int from = headerOnly ? node.Span.StartOffset : Math.Max(node.Span.StartOffset, node.Span.StartOffset + node.Span.Length - 1);
        int lineEnd = text.IndexOf('\n', Math.Min(from, text.Length));
        return lineEnd < 0 ? new Anchor(text.Length, indent, NeedsLeadingNewline: text.Length > 0) : new Anchor(lineEnd + 1, indent, false);
    }

    private static string IndentOf(string text, int offset)
    {
        int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        int i = lineStart;
        while (i < text.Length && text[i] is ' ' or '\t') i++;
        return text[lineStart..i];
    }

    private static TextEdit Insert(string text, Anchor at, IEnumerable<string> lines, string newline)
    {
        var body = string.Concat(lines.Select(l => at.Indent + l + newline));
        return TextEdit.Insert(at.Offset, at.NeedsLeadingNewline ? newline + body : body);
    }

    /// <summary>The survey block at a file-local path (outermost name first), or null.</summary>
    public static SurveyCommand? FindBlock(TherionFile? ast, ImmutableArray<string> localScope)
    {
        if (ast is null || localScope.IsEmpty) return null;
        var children = ast.Children;
        SurveyCommand? found = null;
        foreach (var name in localScope)
        {
            found = Surveys(children).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
            if (found is null) return null;
            children = found.Children;
        }
        return found;

        // Survey blocks may sit inside transparent groups.
        static IEnumerable<SurveyCommand> Surveys(ImmutableArray<TherionNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n is SurveyCommand s) yield return s;
                else if (n is GroupCommand g) foreach (var inner in Surveys(g.Children)) yield return inner;
            }
        }
    }

    /// <summary>A file holding an equate whose members are exactly <paramref name="members"/>, or null.</summary>
    private static string? FindSameEquate(LinkContext ctx, IReadOnlyList<QualifiedName> members)
    {
        var wanted = members.Select(m => m.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (var path in ctx.Workspace.PerFile.Keys)
        {
            if (ctx.Ast(path) is not { } ast) continue;
            foreach (var equate in Equates(ast.Children))
            {
                var scope = ctx.Scopes.EffectiveScope(path, SurveyScopeResolver.SurveyPathAt(ast, equate.Span.StartOffset));
                var names = JoinSplitReferences(equate.Stations).Select(r => StationReference.Resolve(r, scope).ToString()).ToHashSet(StringComparer.Ordinal);
                if (names.SetEquals(wanted)) return path;
            }
        }
        return null;

        static IEnumerable<EquateCommand> Equates(ImmutableArray<TherionNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n is EquateCommand e) yield return e;
                else if (n is BlockCommand b) foreach (var inner in Equates(b.Children)) yield return inner;
            }
        }
    }

    /// <summary>The tokenizer may hand <c>pt@survey</c> over as <c>pt</c>, <c>@survey</c>; glue them back.</summary>
    public static List<string> JoinSplitReferences(IEnumerable<string> raw)
    {
        var joined = new List<string>();
        foreach (var t in raw)
        {
            if (t.StartsWith('@') && joined.Count > 0) joined[^1] += t;
            else joined.Add(t);
        }
        return joined;
    }

    /// <summary>Absolute paths the source's own <c>input</c> lines reach (its sketches, usually).</summary>
    private static IEnumerable<string> InputTargets(SourceSurvey source)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(source.FilePath))!;
        return Inputs(source.Ast.Children).Select(i => Path.GetFullPath(Path.Combine(dir, i.Path.Trim('"'))));

        static IEnumerable<InputCommand> Inputs(ImmutableArray<TherionNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n is InputCommand i) yield return i;
                else if (n is BlockCommand b) foreach (var inner in Inputs(b.Children)) yield return inner;
            }
        }
    }

    /// <summary>A Therion <c>input</c> path: relative to the referring file's folder, forward slashes.</summary>
    public static string RelativePath(string fromDirectory, string target) =>
        Path.GetRelativePath(fromDirectory, target).Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');

    private static bool SamePath(string a, string b) => FilePaths.Same(a, b);
}
