// "Next occurrence in this file": whole-word text occurrences of the word at the caret, cycling
// through the document. Pure text analysis — no symbol resolution, so it also works on words the
// semantic model does not know (a bare station name, a keyword, a value). The symbol-scoped list
// across the project is Find All References.

using System;

namespace ThIDE.Editor;

internal static class WordOccurrences
{
    // Therion station refs are letters, digits, '.', '_', '-'.
    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';

    /// <summary>The word touching <paramref name="offset"/> (start, length); length 0 when there is none.</summary>
    public static (int Start, int Length) WordAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length) return (Math.Max(0, offset), 0);
        int start = offset;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        int end = offset;
        while (end < text.Length && IsWordChar(text[end])) end++;
        return (start, end - start);
    }

    /// <summary>
    /// The next whole-word occurrence of the word at <paramref name="offset"/> after that word,
    /// wrapping to the top of the text; null when there is no word or no other occurrence.
    /// Case-sensitive, as Therion names are.
    /// </summary>
    public static (int Start, int Length)? Next(string text, int offset)
    {
        var (start, length) = WordAt(text, offset);
        if (length == 0) return null;
        var word = text.Substring(start, length);

        int? first = null;
        for (int i = text.IndexOf(word, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(word, i + 1, StringComparison.Ordinal))
        {
            if (!IsWholeWord(text, i, length) || i == start) continue;
            if (i > start) return (i, length);
            first ??= i;
        }
        return first is { } wrapped ? (wrapped, length) : null;
    }

    private static bool IsWholeWord(string text, int at, int length) =>
        (at == 0 || !IsWordChar(text[at - 1])) &&
        (at + length >= text.Length || !IsWordChar(text[at + length]));
}
