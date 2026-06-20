// Implementation Plan §10 — rustc-style diagnostic renderer.
// Produces output of the form:
//
//   error TH0021: expected 'endsurvey', found 'survey'
//     --> cave.th:42:1
//      |
//   42 | survey upper
//      | ^^^^^^ unterminated 'survey' starts here (line 17)
//
// Designed for the CLI but reusable (e.g., in tests).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Therion.Core;

/// <summary>Renders <see cref="Diagnostic"/>s in a rustc-like format.</summary>
public sealed class RustcStyleDiagnosticFormatter
{
    private readonly Dictionary<string, string[]> _fileLineCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Render <paramref name="diagnostic"/> as a multi-line string.</summary>
    public string Format(Diagnostic diagnostic)
    {
        var sb = new StringBuilder();
        AppendOne(sb, diagnostic);
        return sb.ToString();
    }

    /// <summary>Render a batch of diagnostics, one block per entry.</summary>
    public string FormatAll(IEnumerable<Diagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        foreach (var d in diagnostics)
        {
            AppendOne(sb, d);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void AppendOne(StringBuilder sb, Diagnostic d)
    {
        var severity = d.Severity.ToString().ToLowerInvariant();
        sb.Append(severity).Append(' ').Append(d.Code.Value).Append(": ").Append(d.Message).AppendLine();

        if (d.Span.IsEmpty)
            return;

        sb.Append("  --> ").Append(d.Span).AppendLine();

        var sourceLine = TryReadLine(d.Span.FilePath, d.Span.Start.Line);
        if (sourceLine is null) return;

        var lineNumStr = d.Span.Start.Line.ToString();
        var pad = new string(' ', lineNumStr.Length);

        sb.Append(' ').Append(pad).Append(" |").AppendLine();
        sb.Append(' ').Append(lineNumStr).Append(" | ").Append(sourceLine).AppendLine();
        sb.Append(' ').Append(pad).Append(" | ");

        int caretCol = Math.Max(1, d.Span.Start.Column);
        for (int i = 1; i < caretCol; i++) sb.Append(' ');

        int caretLen = d.Span.End.Line == d.Span.Start.Line
            ? Math.Max(1, d.Span.End.Column - d.Span.Start.Column)
            : Math.Max(1, sourceLine.Length - caretCol + 1);
        for (int i = 0; i < caretLen; i++) sb.Append('^');

        if (!string.IsNullOrEmpty(d.Hint))
            sb.Append(' ').Append(d.Hint);

        sb.AppendLine();
    }

    private string? TryReadLine(string filePath, int lineNumber)
    {
        if (string.IsNullOrEmpty(filePath) || lineNumber < 1) return null;
        try
        {
            if (!_fileLineCache.TryGetValue(filePath, out var lines))
            {
                if (!File.Exists(filePath)) return null;
                lines = File.ReadAllLines(filePath);
                _fileLineCache[filePath] = lines;
            }
            return lineNumber <= lines.Length ? lines[lineNumber - 1] : null;
        }
        catch
        {
            return null;
        }
    }
}
