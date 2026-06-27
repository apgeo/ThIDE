// BUILD-01/02 — derive a temporary thconfig from the active one. Therion compiles a whole
// thconfig, so to run a single export (BUILD-01) or a composed quick-export (BUILD-02) we copy the
// active thconfig (keeping its source/select/cs/layout context) and comment the export lines we
// don't want, optionally appending a new export block. The temp file is written in the project
// folder so relative source paths still resolve, then deleted after the build.

using System.Collections.Generic;
using Therion.Syntax;

namespace TherionProc.Services;

/// <summary>An export command found in a thconfig (with its source line range).</summary>
public sealed record ExportTargetInfo(string Title, string? Format, string? Output, int StartLine, int EndLine);

public static class ThconfigExportEditor
{
    /// <summary>Parses the export commands declared at the top level of a thconfig.</summary>
    public static IReadOnlyList<ExportTargetInfo> ParseExports(string thconfigText)
    {
        var list = new List<ExportTargetInfo>();
        TherionFile? ast;
        try { ast = new ThconfigParser().Parse("thconfig", thconfigText).Value; }
        catch { return list; }
        if (ast is null) return list;

        foreach (var node in ast.Children)
        {
            if (node is not ExportCommand ec) continue;
            var title = "export " + ec.ExportType
                + (string.IsNullOrEmpty(ec.Format) ? string.Empty : $"  -fmt {ec.Format}")
                + (string.IsNullOrEmpty(ec.Output) ? string.Empty : $"  →  {ec.Output}");
            list.Add(new ExportTargetInfo(title, ec.Format, ec.Output, ec.Span.Start.Line, ec.Span.End.Line));
        }
        return list;
    }

    /// <summary>Returns the thconfig with every export commented out except <paramref name="keep"/>.</summary>
    public static string IsolateExport(string thconfigText, ExportTargetInfo keep)
    {
        var toComment = new HashSet<int>();
        foreach (var e in ParseExports(thconfigText))
            if (!(e.StartLine == keep.StartLine && e.EndLine == keep.EndLine))
                for (int ln = e.StartLine; ln <= e.EndLine; ln++) toComment.Add(ln);
        return CommentLines(thconfigText, toComment);
    }

    /// <summary>Comments out all existing exports and appends <paramref name="exportBlock"/>.</summary>
    public static string ComposeExport(string thconfigText, string exportBlock)
    {
        var toComment = new HashSet<int>();
        foreach (var e in ParseExports(thconfigText))
            for (int ln = e.StartLine; ln <= e.EndLine; ln++) toComment.Add(ln);
        var body = CommentLines(thconfigText, toComment).TrimEnd();
        return body + "\n\n# --- TherionProc quick export (temporary) ---\n" + exportBlock + "\n";
    }

    private static string CommentLines(string text, HashSet<int> oneBasedLines)
    {
        if (oneBasedLines.Count == 0) return text;
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (oneBasedLines.Contains(i + 1)) lines[i] = "# " + lines[i];
        return string.Join('\n', lines);
    }
}
