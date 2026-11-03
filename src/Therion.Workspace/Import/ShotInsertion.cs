// Where Paste as shot data puts its block. With the caret in a centreline the block goes there; with
// the caret elsewhere in a survey (or in a file with one survey, the caret above it, as in a new trip
// file) it goes at the end of that survey's last centreline, or in a new centreline at its end. A file
// without a survey has nowhere for shots to go.

using Therion.Syntax;

namespace Therion.Workspace.Import;

/// <param name="Start">Offset of the text the block replaces (or where it is inserted, with <paramref name="Length"/> 0).</param>
/// <param name="Length">With the caret: 0. Otherwise the blank line closing the centreline, which the block replaces.</param>
/// <param name="AtCaret">The caret is inside a centreline and the block goes there.</param>
/// <param name="Centreline">The centreline the block goes into; null = the block brings its own.</param>
/// <param name="Indent">Indentation of the block's lines (not used at the caret, where the line's own is kept).</param>
/// <param name="Line">1-based line of the centreline's header, or of the <c>endsurvey</c> the new centreline goes above.</param>
/// <param name="Survey">The survey the shots land in; null for a centreline outside any survey.</param>
public sealed record ShotInsertTarget(int Start, int Length, bool AtCaret, CentrelineCommand? Centreline, string Indent, int Line, string? Survey)
{
    public bool Wrap => Centreline is null;
}

public static class ShotInsertion
{
    /// <summary>The place for a block of shots pasted with the caret at <paramref name="caret"/>; null when the file has no survey to take them.</summary>
    public static ShotInsertTarget? Find(TherionFile? ast, string text, int caret)
    {
        if (ast is null) return null;
        var path = new List<BlockCommand>();
        Enclosing(ast.Children, text, caret, path);

        if (path.LastOrDefault() is CentrelineCommand here)
            return new ShotInsertTarget(caret, 0, true, here, "", LineOf(text, here.Span.StartOffset), path.OfType<SurveyCommand>().LastOrDefault()?.Name);

        // The survey around the caret, else the file's only survey, else (a file of bare centrelines) the file.
        var survey = path.OfType<SurveyCommand>().LastOrDefault();
        if (survey is null)
        {
            var surveys = ast.Children.OfType<SurveyCommand>().ToList();
            if (surveys.Count > 1) return null;
            survey = surveys.FirstOrDefault();
        }
        var children = survey?.Children ?? ast.Children;
        if (children.OfType<CentrelineCommand>().LastOrDefault() is { } last && End(text, last) is { } end)
        {
            // A blank line closing the centreline (a new trip's first data line) is replaced by the block.
            int start = end, length = 0;
            int prevStart = end == 0 ? 0 : text.LastIndexOf('\n', Math.Max(0, end - 2)) + 1;
            if (end > 0 && prevStart > last.Span.StartOffset && text[prevStart..end].Trim().Length == 0 && prevStart < end)
            {
                start = prevStart;
                length = end - prevStart;
            }
            var indent = BodyIndent(text, last) ?? Indentation(text, end) + "  ";
            return new ShotInsertTarget(start, length, false, last, indent, LineOf(text, last.Span.StartOffset), survey?.Name);
        }
        if (survey is not null && End(text, survey) is { } endSurvey)
            return new ShotInsertTarget(endSurvey, 0, false, null, Indentation(text, endSurvey) + "  ", LineOf(text, endSurvey), survey.Name);
        return null;
    }

    // The survey/centreline blocks around the offset, outermost first. A block reaches to its terminator line.
    private static void Enclosing(IEnumerable<TherionNode> nodes, string text, int offset, List<BlockCommand> path)
    {
        foreach (var n in nodes)
        {
            if (n is not (SurveyCommand or CentrelineCommand) || n is not BlockCommand b || offset <= b.Span.StartOffset) continue;
            int limit = End(text, b) ?? b.Span.StartOffset + b.Span.Length;
            if (offset > limit) continue;
            path.Add(b);
            Enclosing(b.Children, text, offset, path);
            return;
        }
    }

    // The start of the line holding the block's terminator: the first line after its body that is
    // neither blank nor a comment. The parsed span stops at the last child, before any terminator.
    private static int? End(string text, BlockCommand block)
    {
        int at = block.Span.StartOffset + block.Span.Length;
        if (block.Children.LastOrDefault() is BlockCommand inner && End(text, inner) is { } innerEnd) at = innerEnd + 1;
        int lineStart = text.IndexOf('\n', Math.Clamp(at - 1, block.Span.StartOffset, text.Length));
        while (lineStart >= 0 && lineStart < text.Length)
        {
            lineStart++;
            int lineEnd = text.IndexOf('\n', lineStart);
            var line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)].Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                var word = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
                bool ends = block is CentrelineCommand
                    ? word.Equals("endcentreline", StringComparison.OrdinalIgnoreCase) || word.Equals("endcenterline", StringComparison.OrdinalIgnoreCase)
                    : word.Equals("end" + block.Keyword, StringComparison.OrdinalIgnoreCase);
                return ends ? lineStart : null;
            }
            lineStart = lineEnd;
        }
        return null;
    }

    private static string? BodyIndent(string text, CentrelineCommand centreline) =>
        centreline.Children.LastOrDefault(c => c is TherionCommand or DataRow) is { } child ? Indentation(text, child.Span.StartOffset) : null;

    private static string Indentation(string text, int offset)
    {
        int s = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1, e = s;
        while (e < text.Length && text[e] is ' ' or '\t') e++;
        return text[s..e];
    }

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++) if (text[i] == '\n') line++;
        return line;
    }
}
