namespace Therion.Workspace.Fixes;

/// <summary>
/// A short piece of one line around a hit, for a preview row that must show the hit even when the
/// line is longer than the row: <c>…equate 1@a 17@b </c>[<c>hit</c>]<c> 3@c…</c>.
/// </summary>
/// <param name="Before">The text left of the hit, cut at a word boundary with "…" when the line goes further.</param>
/// <param name="Hit">The hit itself (up to the end of its line).</param>
/// <param name="After">The text right of the hit, cut the same way.</param>
public sealed record HitSnippet(string Before, string Hit, string After)
{
    /// <summary>
    /// The snippet for the hit at <paramref name="start"/> (length <paramref name="length"/>) in
    /// <paramref name="text"/>, keeping at most <paramref name="context"/> characters on each side.
    /// Leading indentation is dropped.
    /// </summary>
    public static HitSnippet Around(string text, int start, int length, int context = 40)
    {
        start = Math.Clamp(start, 0, text.Length);
        int lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        int lineEnd = text.IndexOf('\n', start);
        if (lineEnd < 0) lineEnd = text.Length;
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--;
        int hitEnd = Math.Clamp(start + length, start, Math.Max(start, lineEnd));

        var before = text[lineStart..start].TrimStart(' ', '\t');
        var after = text[hitEnd..lineEnd];
        if (before.Length > context)
        {
            var cut = before[^context..];
            int space = cut.IndexOfAny([' ', '\t']);
            before = "…" + (space >= 0 && space < cut.Length - 1 ? cut[(space + 1)..] : cut);
        }
        if (after.Length > context)
        {
            var cut = after[..context];
            int space = cut.LastIndexOfAny([' ', '\t']);
            after = (space > 0 ? cut[..space] : cut) + "…";
        }
        return new HitSnippet(before, text[start..hitEnd], after);
    }
}
