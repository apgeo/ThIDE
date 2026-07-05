// The write-side counterpart of the region scanner: formats the `#@region`/`#@endregion`
// directive lines used by the editor's "Enclose in region" command. Kept in the library
// (next to DirectiveScanner) so the quoting rules are one place and unit-testable.

namespace Therion.Syntax.Directives;

/// <summary>Formats <c>#@region</c> / <c>#@endregion</c> directive text.</summary>
public static class RegionDirective
{
    public const string Type = "region";
    public const string EndType = "endregion";

    /// <summary>
    /// The opening directive line for a region with the given <paramref name="title"/>
    /// (its first parameter). A null/blank title yields a bare <c>#@region</c>.
    /// </summary>
    public static string StartLine(string? title)
    {
        title = title?.Trim();
        return string.IsNullOrEmpty(title)
            ? $"{DirectiveParser.Prefix}{Type}"
            : $"{DirectiveParser.Prefix}{Type} {QuoteTitle(title)}";
    }

    /// <summary>The closing directive line.</summary>
    public static string EndLine() => $"{DirectiveParser.Prefix}{EndType}";

    /// <summary>
    /// Wraps a title as a single directive argument. Prefers single quotes; falls back to
    /// double quotes when the title contains a single quote (and strips any embedded quote of
    /// the chosen kind so the argument stays a single token).
    /// </summary>
    private static string QuoteTitle(string title)
    {
        if (!title.Contains('\''))
            return $"'{title}'";
        if (!title.Contains('"'))
            return $"\"{title}\"";
        return $"'{title.Replace('\'', ' ')}'";
    }
}
