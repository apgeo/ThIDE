// The variables templates can use, worked out from the file being edited and the caret. Everything
// here is derived from the text (and its parse), so the editor, the LSP and the file-template
// dialog resolve them the same way.

using System.Globalization;
using System.Text.RegularExpressions;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;

namespace Therion.Workspace.Templates;

/// <summary>Where a template is being expanded.</summary>
public sealed record SnippetContext(string? FilePath, string Text, int Caret, DateTime Now)
{
    public static SnippetContext ForNewFile(string filePath, DateTime now) => new(filePath, "", 0, now);
}

public static partial class SnippetVariables
{
    /// <summary>Every variable name <see cref="Standard"/> knows, for documentation and completion.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "DATE", "TIME", "YEAR", "FILE_NAME", "FILE_BASENAME", "FILE_DIR",
        "CURRENT_SURVEY", "PARENT_SURVEY", "TEAM_RECENT",
        "LAST_UNITS", "LAST_CALIBRATE", "LAST_INSTRUMENT", "LAST_DATA", "LAST_STATION", "NEXT_STATION",
    ];

    /// <summary>
    /// A resolver for the standard variables. Values that need the parse are computed on first use;
    /// a variable with nothing to say (no survey at the caret, no earlier data row) resolves to null,
    /// so the template's default — or a placeholder — applies.
    /// </summary>
    public static Func<string, string?> Standard(SnippetContext ctx, IReadOnlyDictionary<string, string>? extra = null)
    {
        TherionFile? ast = null;
        bool parsed = false;
        string? lastStation = null;
        bool lastStationDone = false;
        string? LastStationOnce()
        {
            if (!lastStationDone) { lastStationDone = true; lastStation = LastStation(Ast(), ctx); }
            return lastStation;
        }
        TherionFile? Ast()
        {
            if (!parsed)
            {
                parsed = true;
                if (ctx.Text.Length > 0) ast = new ThParser().Parse(ctx.FilePath ?? "untitled.th", ctx.Text).Value;
            }
            return ast;
        }

        return name =>
        {
            if (extra is not null && extra.TryGetValue(name, out var given)) return given;
            return name switch
            {
                "DATE" => ctx.Now.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
                "TIME" => ctx.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                "YEAR" => ctx.Now.Year.ToString(CultureInfo.InvariantCulture),
                "FILE_NAME" => ctx.FilePath is null ? null : Path.GetFileName(ctx.FilePath),
                "FILE_BASENAME" => ctx.FilePath is null ? null : Path.GetFileNameWithoutExtension(ctx.FilePath),
                "FILE_DIR" => ctx.FilePath is null ? null : Path.GetDirectoryName(ctx.FilePath),
                "CURRENT_SURVEY" => SurveyAt(Ast(), ctx, 1),
                "PARENT_SURVEY" => SurveyAt(Ast(), ctx, 2),
                "TEAM_RECENT" => TeamRecent(Ast(), ctx),
                "LAST_UNITS" => LastCommand(Ast(), ctx, "units"),
                "LAST_CALIBRATE" => LastCommand(Ast(), ctx, "calibrate"),
                "LAST_INSTRUMENT" => LastCommand(Ast(), ctx, "instrument"),
                "LAST_DATA" => LastCommand(Ast(), ctx, "data"),
                "LAST_STATION" => LastStationOnce(),
                "NEXT_STATION" => LastStationOnce() is { } last ? NextStation(last) : null,
                _ => null,
            };
        };
    }

    // depth 1 = innermost survey, 2 = its parent.
    private static string? SurveyAt(TherionFile? ast, SnippetContext ctx, int depth)
    {
        var path = SurveyScopeResolver.SurveyPathAt(ast, Math.Min(ctx.Caret, ctx.Text.Length), ctx.Text);
        return path.Length >= depth ? path[^depth] : null;
    }

    private static IEnumerable<TherionNode> Before(TherionFile? ast, SnippetContext ctx) =>
        All(ast?.Children ?? []).Where(n => n.Span.StartOffset < ctx.Caret);

    private static IEnumerable<TherionNode> All(IEnumerable<TherionNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n is BlockCommand b) foreach (var inner in All(b.Children)) yield return inner;
        }
    }

    private static string LineText(SnippetContext ctx, TherionNode node)
    {
        int start = node.Span.StartOffset;
        int end = Math.Min(ctx.Text.Length, start + node.Span.Length);
        return ctx.Text[start..end].TrimEnd();
    }

    /// <summary>The text of the last <paramref name="keyword"/> command before the caret.</summary>
    private static string? LastCommand(TherionFile? ast, SnippetContext ctx, string keyword) =>
        Before(ast, ctx).OfType<TherionCommand>().LastOrDefault(c => string.Equals(c.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
            is { } cmd ? LineText(ctx, cmd) : null;

    /// <summary>The <c>team</c> lines of the last centreline before the caret that has any.</summary>
    private static string? TeamRecent(TherionFile? ast, SnippetContext ctx)
    {
        var block = Before(ast, ctx).OfType<CentrelineCommand>()
            .LastOrDefault(c => c.Children.OfType<TherionCommand>().Any(k => k.Keyword == "team"));
        if (block is null) return null;
        return string.Join("\n", block.Children.OfType<TherionCommand>().Where(k => k.Keyword == "team").Select(k => LineText(ctx, k)));
    }

    /// <summary>The "to" station of the last leg before the caret (splays skipped).</summary>
    private static string? LastStation(TherionFile? ast, SnippetContext ctx)
    {
        if (ast is null) return null;
        var model = new SemanticBinder().Bind(ast);
        var last = model.Shots
            .Where(s => s.Span.StartOffset < ctx.Caret && (s.Flags & ShotFlags.Splay) == 0)
            .OrderBy(s => s.Span.StartOffset)
            .LastOrDefault();
        return last?.To.Last;
    }

    /// <summary>
    /// The station after <paramref name="station"/>: its trailing number plus one, keeping zero
    /// padding (<c>A07</c> → <c>A08</c>, <c>1.9</c> → <c>1.10</c>). Null when it does not end in a
    /// number. The token is never split on '.': only its trailing digits change.
    /// </summary>
    public static string? NextStation(string station)
    {
        var m = TrailingNumber().Match(station);
        if (!m.Success) return null;
        var digits = m.Groups[1].Value;
        var next = (System.Numerics.BigInteger.Parse(digits, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        return station[..m.Index] + next.PadLeft(digits.Length, '0');
    }

    [GeneratedRegex(@"(\d+)$")]
    private static partial Regex TrailingNumber();
}
