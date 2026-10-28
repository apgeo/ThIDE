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
using Therion.Syntax;
using ThIDE.Resources;

namespace ThIDE.Editor;

/// <summary>A single quick-fix: a menu title and the edit it applies.</summary>
public sealed record TherionQuickFix(string Title, Action Apply);

public static class TherionCodeActions
{
    public static IReadOnlyList<TherionQuickFix> Build(
        TextDocument doc, IReadOnlyList<Diagnostic>? diagnostics, int caretOffset, string? filePath)
    {
        var fixes = new List<TherionQuickFix>();
        if (doc.TextLength == 0) return fixes;

        caretOffset = Math.Clamp(caretOffset, 0, doc.TextLength);
        var line = doc.GetLineByOffset(caretOffset);
        string lineText = doc.GetText(line.Offset, line.Length);
        string fw = FirstWord(lineText);

        // The text fixes come from the library (shared with "fix all"). Creating a missing file is
        // not a text edit and stays here, in its original place: after rename and terminator.
        var textFixes = Therion.Workspace.Fixes.CodeFixes.ForLine(doc.Text, line.LineNumber, diagnostics ?? Array.Empty<Diagnostic>());
        int head = textFixes.TakeWhile(f => f.TitleKey is "Fix_Rename" or "Fix_InsertEnd").Count();
        fixes.AddRange(textFixes.Take(head).Select(f => ToQuickFix(doc, f)));
        if (fw is "input" or "source" or "load" && ResolvePath(lineText, filePath) is { } target && !File.Exists(target))
            fixes.Add(new(string.Format(Tr.Get("Fix_CreateFile"), Path.GetFileName(target)), () => CreateStub(target)));
        fixes.AddRange(textFixes.Skip(head).Select(f => ToQuickFix(doc, f)));
        return fixes;
    }

    private static string Title(Therion.Workspace.Fixes.CodeFix fix) => string.Format(Tr.Get(fix.TitleKey), fix.TitleArgs.Cast<object>().ToArray());

    // One undo step; edits back to front so earlier offsets stay valid.
    private static TherionQuickFix ToQuickFix(TextDocument doc, Therion.Workspace.Fixes.CodeFix fix) => new(Title(fix), () =>
    {
        doc.BeginUpdate();
        try
        {
            foreach (var e in fix.Edits.OrderByDescending(e => e.Start))
                if (e.End <= doc.TextLength && doc.GetText(e.Start, e.Length) == e.ExpectedText)
                    doc.Replace(e.Start, e.Length, e.NewText);
        }
        finally { doc.EndUpdate(); }
    });

    // ---- edits -------------------------------------------------------------------------------

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

    private static string? ResolvePath(string lineText, string? filePath)
    {
        var parts = lineText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var token = parts[1].Trim('"');
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var rel = token.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            var dir = string.IsNullOrEmpty(filePath) ? Environment.CurrentDirectory : Path.GetDirectoryName(filePath);
            var full = Path.IsPathRooted(rel)
                ? Path.GetFullPath(rel)
                : Path.GetFullPath(Path.Combine(dir ?? string.Empty, rel));
            // Match the include graph: `input B` targets `B.th`, so the quick-fix creates `B.th` and
            // actually clears the "File not found" the diagnostic reports (both resolve through
            // SourceGraph.WithDefaultIncludeExtension — keep them in lockstep).
            return SourceGraph.WithDefaultIncludeExtension(full);
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
