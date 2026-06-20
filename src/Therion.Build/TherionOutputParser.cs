// Implementation Plan §9bis.2 — output line classification.
// Best-effort heuristic per Decision #23; unmatched lines preserved verbatim.

using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Build;

/// <summary>Pluggable Therion output ? structured diagnostic mapper.</summary>
public interface ITherionOutputParser
{
    CompilerOutputLine Classify(string rawLine, bool isStderr);
}

/// <summary>
/// Default heuristic implementation. Recognizes <c>file:line:</c> prefixes and
/// <c>error:</c> / <c>warning:</c> markers anywhere in the line.
/// </summary>
public sealed class HeuristicTherionOutputParser : ITherionOutputParser
{
    // file:line[:col]: severity?: message
    private static readonly Regex FileLineRx = new(
        @"^\s*(?<file>[^:\r\n]+\.(?:th2?|thconfig|xvi)):(?<line>\d+)(?::(?<col>\d+))?\s*[:\-]?\s*(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CompilerOutputLine Classify(string rawLine, bool isStderr)
    {
        var line = rawLine ?? string.Empty;
        var severity = ClassifySeverity(line, isStderr);
        SourceSpan? span = null;

        var m = FileLineRx.Match(line);
        if (m.Success)
        {
            int.TryParse(m.Groups["line"].Value, out var ln);
            int.TryParse(m.Groups["col"].Value, out var col);
            if (col == 0) col = 1;
            var file = m.Groups["file"].Value;
            var loc = new SourceLocation(ln, col);
            span = new SourceSpan(file, loc, loc, 0, 0);
        }

        return new CompilerOutputLine(line, severity, span);
    }

    private static DiagnosticSeverity ClassifySeverity(string line, bool isStderr)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)) return DiagnosticSeverity.Error;
        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)) return DiagnosticSeverity.Warning;
        if (line.Contains("hint", StringComparison.OrdinalIgnoreCase)) return DiagnosticSeverity.Hint;
        return isStderr ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info;
    }
}
