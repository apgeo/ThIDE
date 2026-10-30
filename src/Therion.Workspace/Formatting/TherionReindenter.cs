// The conservative formatter behind the editor's Format Document, Format all files, `therion-cli
// format` and the format_file tool: each line goes to its block-nesting depth (survey/centreline/
// scrap/…) and loses trailing whitespace. Lines inside layout/lookup bodies (metapost, TeX) are left
// verbatim. It never reorders, adds or drops a token, so unlike an AST round-trip it cannot lose text.

using System.Text;

namespace Therion.Workspace.Formatting;

public static class TherionReindenter
{
    // Canonical block keyword → its closer. A copy of the editor's table, which also drives folding.
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "survey", "centreline", "scrap", "line", "area", "map", "surface", "scan", "comment",
        "group", "layout", "lookup", "code", "source",
    };

    /// <summary>The indent unit a setting asks for: a tab, or <paramref name="size"/> spaces.</summary>
    public static string IndentUnit(bool useTabs, int size) => useTabs ? "\t" : new string(' ', Math.Max(1, size));

    /// <summary>
    /// <paramref name="text"/> re-indented. Each line keeps its own terminator (LF, CRLF or none), so
    /// line endings are not this method's business.
    /// </summary>
    public static string Format(string text, string indentUnit)
    {
        var (lines, endings) = Split(text);
        var desired = DesiredLines(lines, indentUnit);
        var sb = new StringBuilder(text.Length + 64);
        for (int i = 0; i < lines.Count; i++)
            sb.Append(desired[i] ?? lines[i]).Append(endings[i]);
        return sb.ToString();
    }

    /// <summary>
    /// For each line (without its terminator), the text it should have, or null when it is fine as it
    /// is or must not be touched (a layout/lookup body line). The editor applies these in place so the
    /// caret survives.
    /// </summary>
    public static string?[] DesiredLines(IReadOnlyList<string> lines, string indentUnit)
    {
        var result = new string?[lines.Count];
        var stack = new Stack<string>();
        string? opaqueCloser = null;
        for (int i = 0; i < lines.Count; i++)
        {
            var text = lines[i];
            var fw = FirstWord(text);

            // Inside layout/lookup only the closer counts; the body is someone else's language.
            if (opaqueCloser is not null)
            {
                if (!string.Equals(fw, opaqueCloser, StringComparison.OrdinalIgnoreCase)) continue;
                opaqueCloser = null;
            }

            if (CloserType(fw) is { } ctype)
            {
                while (stack.Count > 0 && stack.Peek() != ctype) stack.Pop();
                if (stack.Count > 0) stack.Pop();
            }

            var body = text.Trim();
            var want = body.Length == 0 ? string.Empty : Repeat(indentUnit, stack.Count) + body;
            if (!string.Equals(text, want, StringComparison.Ordinal)) result[i] = want;

            if (IsBlockOpenerLine(text, fw, out var otype))
            {
                stack.Push(otype);
                if (otype is "layout" or "lookup") opaqueCloser = "end" + otype;
            }
        }
        return result;
    }

    private static (List<string> Lines, List<string> Endings) Split(string text)
    {
        var lines = new List<string>();
        var endings = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n' && text[i] != '\r') continue;
            int len = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            lines.Add(text[start..i]);
            endings.Add(text.Substring(i, len));
            i += len - 1;
            start = i + 1;
        }
        lines.Add(text[start..]);
        endings.Add("");
        return (lines, endings);
    }

    private static string FirstWord(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return text[start..i];
    }

    // centerline/centreline are the same block.
    private static string Normalize(string keyword) =>
        string.Equals(keyword, "centerline", StringComparison.OrdinalIgnoreCase) ? "centreline" : keyword.ToLowerInvariant();

    private static string? CloserType(string firstWord)
    {
        if (firstWord.Length <= 3 || !firstWord.StartsWith("end", StringComparison.OrdinalIgnoreCase)) return null;
        int n = 3;
        while (n < firstWord.Length && char.IsLetter(firstWord[n])) n++;
        var rest = Normalize(firstWord[3..n]);
        return Blocks.Contains(rest) ? rest : null;
    }

    // Only a bare `source` opens a block (the thconfig source…endsource); `source <file>` is a command.
    private static bool IsBlockOpenerLine(string text, string firstWord, out string type)
    {
        type = Normalize(firstWord);
        if (!Blocks.Contains(type)) return false;
        return type != "source" || text.Trim().Length == "source".Length;
    }

    private static string Repeat(string unit, int count)
    {
        if (count <= 0) return string.Empty;
        var sb = new StringBuilder(unit.Length * count);
        for (int i = 0; i < count; i++) sb.Append(unit);
        return sb.ToString();
    }
}
