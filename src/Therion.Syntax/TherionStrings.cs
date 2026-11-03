// Values of Therion string literals. Therion source-of-truth: therion/src/thparse.cxx
// (thsplit_args): a word is either a bare run of non-blank characters or a "…" string, in which
// a doubled quote ("") stands for one quote character; a string with no closing quote takes the
// rest of the line.

using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>Reads the value of Therion string literals and quote-aware argument lists.</summary>
public static class TherionStrings
{
    /// <summary>
    /// The value of a string literal as written in source: the quotes are removed and each doubled
    /// quote becomes one. Text that does not start with a quote is returned unchanged. A string
    /// without its closing quote yields the rest of its text, as Therion reads it.
    /// </summary>
    public static string Unquote(string text)
    {
        if (text.Length == 0 || text[0] != '"') return text;
        int quotes = 0;
        foreach (char ch in text) if (ch == '"') quotes++;
        bool closed = quotes % 2 == 0;
        var inner = closed ? text[1..^1] : text[1..];
        return inner.Contains("\"\"", System.StringComparison.Ordinal) ? inner.Replace("\"\"", "\"") : inner;
    }

    /// <summary>
    /// Splits a raw argument string into its words: whitespace-separated, or quoted (returned
    /// without the quotes, a doubled quote inside read as one quote).
    /// </summary>
    public static IEnumerable<string> SplitArguments(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        int i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length) yield break;
            if (raw[i] == '"')
            {
                var sb = new System.Text.StringBuilder();
                i++;
                while (i < raw.Length)
                {
                    if (raw[i] == '"')
                    {
                        if (i + 1 < raw.Length && raw[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    sb.Append(raw[i++]);
                }
                yield return sb.ToString();
            }
            else
            {
                int start = i;
                while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
                yield return raw[start..i];
            }
        }
    }

    /// <summary>Reports every string in <paramref name="tokens"/> whose closing quote is missing.</summary>
    internal static void ReportUnterminated(
        IEnumerable<TherionToken> tokens, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        foreach (var t in tokens)
            if (TherionTokenizer.IsUnterminatedString(t))
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.UnterminatedString,
                    DiagnosticSeverity.Warning,
                    "String is missing its closing quote; it ends at the end of the line.",
                    t.Span,
                    hint: "Close the string with \". Write \"\" for a quote character inside it."));
    }
}
