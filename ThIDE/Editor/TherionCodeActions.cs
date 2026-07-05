// editor quick-fixes / code actions (Ctrl+.). Computes line-scoped fixes for the caret
// line from the active diagnostics + the line text: rename to a suggested station ("did you mean"),
// insert a missing block terminator, create a missing input/source file, insert a cs line, and
// comment out an unknown command. Line/text-based so it never depends on fragile token spans.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using Therion.Core;

namespace ThIDE.Editor;

/// <summary>A single quick-fix: a menu title and the edit it applies.</summary>
public sealed record TherionQuickFix(string Title, Action Apply);

public static class TherionCodeActions
{
    private static readonly Dictionary<string, string> Enders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["survey"] = "endsurvey", ["centreline"] = "endcentreline", ["centerline"] = "endcentreline",
        ["scrap"] = "endscrap", ["map"] = "endmap", ["surface"] = "endsurface", ["line"] = "endline",
        ["area"] = "endarea", ["layout"] = "endlayout", ["group"] = "endgroup",
    };

    public static IReadOnlyList<TherionQuickFix> Build(
        TextDocument doc, IReadOnlyList<Diagnostic>? diagnostics, int caretOffset, string? filePath)
    {
        var fixes = new List<TherionQuickFix>();
        if (doc.TextLength == 0) return fixes;

        caretOffset = Math.Clamp(caretOffset, 0, doc.TextLength);
        var line = doc.GetLineByOffset(caretOffset);
        int lineNo = line.LineNumber;
        string lineText = doc.GetText(line.Offset, line.Length);
        string indent = LeadingWhitespace(lineText);
        string fw = FirstWord(lineText);

        var onLine = (diagnostics ?? Array.Empty<Diagnostic>())
            .Where(d => d.Span.Start.Line == lineNo).ToList();

        // 1. "did you mean 'X'?" → rename the offending token in this line.
        foreach (var d in onLine)
        {
            var bad = Regex.Match(d.Message ?? string.Empty, @"'([^']+)'");
            var sug = Regex.Match((d.Message ?? "") + "  " + (d.Hint ?? ""), @"did you mean '([^']+)'", RegexOptions.IgnoreCase);
            if (bad.Success && sug.Success)
            {
                string from = bad.Groups[1].Value, to = sug.Groups[1].Value;
                if (from != to && lineText.Contains(from, StringComparison.Ordinal))
                    fixes.Add(new($"Rename '{from}' → '{to}'", () => ReplaceFirst(doc, line, from, to)));
            }
        }

        // 2. Insert the matching terminator for a block opener that isn't terminated.
        if (Enders.TryGetValue(fw, out var ender) && !HasTerminatorAhead(doc, lineNo, ender))
            fixes.Add(new($"Insert '{ender}'", () => InsertTerminator(doc, line, indent, ender)));

        // 3. Create a missing input/source/load target on disk.
        if (fw is "input" or "source" or "load")
        {
            var target = ResolvePath(lineText, filePath);
            if (target is not null && !File.Exists(target))
                fixes.Add(new($"Create file '{Path.GetFileName(target)}'", () => CreateStub(target)));
        }

        // 4. Insert a cs line into a centreline that has none.
        if ((fw is "centreline" or "centerline") && !BodyHasKeyword(doc, lineNo, "cs"))
            fixes.Add(new("Insert 'cs ' line", () => InsertBelow(doc, line, indent + "  cs ")));

        // 5. Comment out the current line (handy for an unknown command, code TH0010).
        if (!lineText.TrimStart().StartsWith("#"))
            fixes.Add(new("Comment out this line", () => CommentOut(doc, line, indent)));

        return fixes;
    }

    // ---- edits -------------------------------------------------------------------------------

    private static void ReplaceFirst(TextDocument doc, DocumentLine line, string from, string to)
    {
        string text = doc.GetText(line.Offset, line.Length);
        int i = text.IndexOf(from, StringComparison.Ordinal);
        if (i >= 0) doc.Replace(line.Offset + i, from.Length, to);
    }

    private static void InsertTerminator(TextDocument doc, DocumentLine opener, string indent, string ender)
    {
        // Place the terminator after the opener's indented body: walk forward while lines are
        // blank or more-indented than the opener, then insert before the first dedented line (or EOF).
        var cur = opener;
        var insertAfter = opener;
        while (cur.NextLine is { } next)
        {
            string t = doc.GetText(next.Offset, next.Length);
            if (t.Trim().Length == 0) { cur = next; continue; }
            if (LeadingWhitespace(t).Length > indent.Length) { cur = next; insertAfter = next; continue; }
            break;
        }
        int at = insertAfter.EndOffset;
        doc.Insert(at, Environment.NewLine + indent + ender);
    }

    private static void InsertBelow(TextDocument doc, DocumentLine line, string text) =>
        doc.Insert(line.EndOffset, Environment.NewLine + text);

    private static void CommentOut(TextDocument doc, DocumentLine line, string indent) =>
        doc.Insert(line.Offset + indent.Length, "# ");

    private static void CreateStub(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            string stub = ext switch
            {
                ".th2" => "encoding utf-8\n\n# created by ThIDE\n",
                ".thconfig" or ".thc" => "# created by ThIDE\n",
                _ => "# created by ThIDE\n",
            };
            File.WriteAllText(path, stub);
        }
        catch { /* best effort */ }
    }

    // ---- queries -----------------------------------------------------------------------------

    private static bool HasTerminatorAhead(TextDocument doc, int openerLineNo, string ender)
    {
        for (int n = openerLineNo + 1; n <= doc.LineCount; n++)
        {
            var l = doc.GetLineByNumber(n);
            if (string.Equals(FirstWord(doc.GetText(l.Offset, l.Length)), ender, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool BodyHasKeyword(TextDocument doc, int openerLineNo, string keyword)
    {
        for (int n = openerLineNo + 1; n <= doc.LineCount; n++)
        {
            var l = doc.GetLineByNumber(n);
            var fw = FirstWord(doc.GetText(l.Offset, l.Length));
            if (string.Equals(fw, keyword, StringComparison.OrdinalIgnoreCase)) return true;
            if (fw.StartsWith("end", StringComparison.OrdinalIgnoreCase)) return false; // left the block
        }
        return false;
    }

    private static string? ResolvePath(string lineText, string? filePath)
    {
        var parts = lineText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var token = parts[1].Trim('"');
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var rel = token.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(rel)) return Path.GetFullPath(rel);
            var dir = string.IsNullOrEmpty(filePath) ? Environment.CurrentDirectory : Path.GetDirectoryName(filePath);
            return Path.GetFullPath(Path.Combine(dir ?? string.Empty, rel));
        }
        catch { return null; }
    }

    private static string FirstWord(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return text[start..i];
    }

    private static string LeadingWhitespace(string text)
    {
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        return text[..i];
    }
}
