// Implementation Plan �9bis.2 � output line classification.
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
/// Default heuristic implementation. Recognizes Therion's native
/// <c>… -- file [line] -- message -- symbol</c> diagnostics, <c>file:line:</c> prefixes,
/// and bare file-path tokens, so the compiler output can link to the source (#1).
/// </summary>
public sealed class HeuristicTherionOutputParser : ITherionOutputParser
{
    // Therion native location: "<file> [<line>]" (e.g. "… -- foo/bar.th2 [168] -- station …").
    private static readonly Regex TherionLocRx = new(
        @"(?<file>(?:[A-Za-z]:[\\/])?[^\s\[\]]+\.(?:th2?|thc|thconfig|xvi))\s*\[(?<line>\d+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // file:line[:col]: message
    private static readonly Regex FileLineRx = new(
        @"^\s*(?<file>[^:\r\n]+\.(?:th2?|thc|thconfig|xvi)):(?<line>\d+)(?::(?<col>\d+))?\s*[:\-]?\s*(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Any bare path token (no line) — makes "reading <file>" style lines openable too.
    private static readonly Regex AnyPathRx = new(
        @"(?<file>(?:[A-Za-z]:[\\/])?[^\s:""'\[\]]+\.(?:th2?|thc|thconfig|xvi|lox|pdf|svg|3d|dxf|log))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CompilerOutputLine Classify(string rawLine, bool isStderr)
    {
        var line = rawLine ?? string.Empty;
        var severity = ClassifySeverity(line, isStderr);
        SourceSpan? span = null;
        string? symbol = null;

        if (TherionLocRx.Match(line) is { Success: true } tm)
        {
            int.TryParse(tm.Groups["line"].Value, out var ln);
            var loc = new SourceLocation(ln <= 0 ? 1 : ln, 1);
            span = new SourceSpan(tm.Groups["file"].Value, loc, loc, 0, 0);
            symbol = ExtractTrailingSymbol(line, tm.Index + tm.Length);
        }
        else if (FileLineRx.Match(line) is { Success: true } fm)
        {
            int.TryParse(fm.Groups["line"].Value, out var ln);
            int.TryParse(fm.Groups["col"].Value, out var col);
            if (col == 0) col = 1;
            var loc = new SourceLocation(ln <= 0 ? 1 : ln, col);
            span = new SourceSpan(fm.Groups["file"].Value, loc, loc, 0, 0);
        }
        else if (AnyPathRx.Match(line) is { Success: true } pm)
        {
            var loc = new SourceLocation(1, 1);
            span = new SourceSpan(pm.Groups["file"].Value, loc, loc, 0, 0);
        }

        return new CompilerOutputLine(line, severity, span, symbol);
    }

    /// <summary>
    /// The offending identifier Therion appends as the final <c>-- &lt;token&gt;</c> segment
    /// (e.g. the station id in "station does not exist -- E65a"). Returns null when the tail
    /// is a sentence rather than a single token.
    /// </summary>
    private static string? ExtractTrailingSymbol(string line, int afterLocation)
    {
        if (afterLocation >= line.Length) return null;
        var parts = line[afterLocation..].Split("--", System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null; // need at least "<message> -- <symbol>"
        var last = parts[^1].Trim();
        return last.Length > 0 && !last.Contains(' ') ? last : null;
    }

    private static DiagnosticSeverity ClassifySeverity(string line, bool isStderr)
    {
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase)) return DiagnosticSeverity.Error;
        if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)) return DiagnosticSeverity.Warning;
        if (line.Contains("hint", StringComparison.OrdinalIgnoreCase)) return DiagnosticSeverity.Hint;
        return isStderr ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info;
    }
}
