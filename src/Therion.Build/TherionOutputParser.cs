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
    // A file extension: a dot followed by ≥2 characters, at least one of them a letter. The full
    // extension is always captured — we never assume it is ".th"/".th2"/".3d", so ".thconfig" and
    // ".3dmf" link in full rather than truncating to ".th"/".3d". Requiring ≥2 chars with a letter
    // keeps decimal tokens ("1.5", "6.2.3") and abbreviations ("e.g.") from looking like a path.
    private const string Ext = @"\.(?=[0-9]*[A-Za-z])[A-Za-z0-9]{2,}";

    // Therion native location: "<file> [<line>]" (e.g. "… -- foo/bar.th2 [168] -- station …").
    private static readonly Regex TherionLocRx = new(
        @"(?<file>(?:[A-Za-z]:[\\/])?[^\s\[\]]+" + Ext + @")\s*\[(?<line>\d+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // file:line[:col]: message
    private static readonly Regex FileLineRx = new(
        @"^\s*(?<file>[^:\r\n]+" + Ext + @"):(?<line>\d+)(?::(?<col>\d+))?\s*[:\-]?\s*(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Any bare path token (no line) — makes "reading <file>" / "configuration file: <file>" style
    // lines openable too. The extension is captured generically; a match is only treated as a link
    // when it looks like a real file (see IsLinkablePath), so dotted survey/station names such as
    // "main.upper" are not mistaken for paths.
    private static readonly Regex AnyPathRx = new(
        @"(?<file>(?:[A-Za-z]:[\\/])?[^\s:""'\[\]()<>|?*]+" + Ext + @")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Therion "<label> file: <path>" info lines, where the path is the ENTIRE remainder of the line
    // (e.g. "configuration file: cave.thconfig", "initialization file: C:\Program Files\Therion/therion.ini").
    // Capturing to end-of-line is the only reliable way to keep an absolute path that contains spaces
    // ("Program Files") and/or mixed \ and / separators whole — a token scan would stop at the space.
    private static readonly Regex LabeledPathRx = new(
        @"^\s*[A-Za-z][A-Za-z ]*file:[ \t]+(?<file>\S.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly char[] PathSeparators = { '/', '\\' };

    /// <summary>
    /// Broad set of file extensions Therion reads or writes. Used only as a *positive* signal that a
    /// bare token (no directory separator) is a real file worth linking — a token that already
    /// contains a separator is linked whatever its extension. The extension itself is always captured
    /// in full, so the set never truncates what is shown/opened.
    /// </summary>
    private static readonly HashSet<string> KnownFileExtensions = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // source / project
        "th", "th2", "thc", "thconfig", "xvi",
        // survey-data imports
        "svx", "dat", "mak", "clp", "plt", "tro", "tab", "top", "pta", "plg", "srv", "cave",
        // model exports
        "lox", "3d", "3dmf", "vrml", "wrl", "kml", "kmz", "shp", "shx", "dbf", "json", "dlp", "drml",
        // map / drawing exports + images
        "pdf", "svg", "svgz", "xhtml", "dxf", "bbox", "ai", "eps", "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff",
        // database / lists / intermediates / logs / config
        "sql", "csv", "html", "htm", "dem", "thm", "tex", "mp", "mpx", "log", "tlx", "txt", "lst", "ini",
    };

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
        else if (LabeledPath(line) is { } labeled)
        {
            var loc = new SourceLocation(1, 1);
            span = new SourceSpan(labeled, loc, loc, 0, 0);
        }
        else if (FirstLinkablePath(line) is { } file)
        {
            var loc = new SourceLocation(1, 1);
            span = new SourceSpan(file, loc, loc, 0, 0);
        }

        return new CompilerOutputLine(line, severity, span, symbol);
    }

    /// <summary>
    /// The full path from a Therion "&lt;label&gt; file: &lt;path&gt;" line (the whole remainder, so an
    /// absolute path with spaces and/or mixed separators stays intact), or null when the line isn't one
    /// of those or the captured remainder doesn't look like a file.
    /// </summary>
    private static string? LabeledPath(string line)
    {
        var m = LabeledPathRx.Match(line);
        return m.Success && IsLinkablePath(m.Groups["file"].Value) ? m.Groups["file"].Value : null;
    }

    /// <summary>
    /// The first bare path token in <paramref name="line"/> that looks like a real file (has a
    /// directory separator or a known file extension), or null. Skips dotted survey/station names.
    /// </summary>
    private static string? FirstLinkablePath(string line)
    {
        for (var m = AnyPathRx.Match(line); m.Success; m = m.NextMatch())
        {
            var file = m.Groups["file"].Value;
            if (IsLinkablePath(file)) return file;
        }
        return null;
    }

    /// <summary>
    /// True when a bare token is plausibly a file path: it has a directory separator, or its
    /// (fully-captured) extension is a known Therion file extension. This keeps dotted identifiers
    /// like a fully-qualified survey name ("main.upper") from being hyperlinked as files.
    /// </summary>
    private static bool IsLinkablePath(string file)
    {
        if (file.IndexOfAny(PathSeparators) >= 0) return true;
        var dot = file.LastIndexOf('.');
        if (dot < 0 || dot == file.Length - 1) return false;
        return KnownFileExtensions.Contains(file[(dot + 1)..]);
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
