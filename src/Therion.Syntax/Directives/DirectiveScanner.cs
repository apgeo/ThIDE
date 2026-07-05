// Scans a whole source file for `#@…` directives and resolves the ones that form blocks.
// Currently the only block directive is `region`/`endregion`; the pairing is a simple stack
// (innermost `#@endregion` closes the innermost open `#@region`), which nests exactly like
// C# #region and mirrors the editor's block-fold stack. Adding `#@if/#@elif/#@else/#@endif`
// later means adding another paired handler here — no framework rewrite.
//
// Cost: one linear pass over the lines, and directive parsing only touches lines whose
// comment starts with `@`. For the handful of directives in a real file this is negligible.

using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Directives;

/// <summary>Extracts application directives and region blocks from source text.</summary>
public static class DirectiveScanner
{
    /// <summary>
    /// Scans <paramref name="text"/> for directives. <see cref="DirectiveScanResult.Regions"/>
    /// carries char offsets so the editor can fold without re-parsing; diagnostics flag
    /// unmatched <c>#@region</c>/<c>#@endregion</c> directives.
    /// </summary>
    public static DirectiveScanResult Scan(string text, string filePath = "")
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(DirectiveParser.Prefix, System.StringComparison.Ordinal) < 0)
            return DirectiveScanResult.Empty;

        var directives = ImmutableArray.CreateBuilder<TherionDirective>();
        var regions = ImmutableArray.CreateBuilder<DirectiveRegion>();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var openRegions = new Stack<(TherionDirective Start, int StartOffset)>();

        int lineNo = 0;
        foreach (var (lineText, lineStart, lineLen) in EnumerateLines(text))
        {
            lineNo++;
            if (!DirectiveParser.TryParse(lineText, filePath, lineNo, lineStart, out var d))
                continue;
            directives.Add(d);

            switch (d.Type)
            {
                case "region":
                    openRegions.Push((d, lineStart));
                    break;

                case "endregion":
                    if (openRegions.Count > 0)
                    {
                        var open = openRegions.Pop();
                        regions.Add(new DirectiveRegion(
                            Title: open.Start.ArgValue(0),
                            StartLine: open.Start.Line,
                            EndLine: d.Line,
                            StartOffset: open.StartOffset,
                            EndOffset: lineStart + lineLen,
                            StartDirective: open.Start,
                            EndDirective: d));
                    }
                    else
                    {
                        diagnostics.Add(Diagnostic.Create(
                            DiagnosticCodes.DirectiveUnmatchedEndRegion, DiagnosticSeverity.Warning,
                            "'#@endregion' has no matching '#@region'.", d.Span));
                    }
                    break;
            }
        }

        // Any region still open at end of file is unclosed.
        while (openRegions.Count > 0)
        {
            var open = openRegions.Pop();
            var title = open.Start.ArgValue(0);
            regions.Add(new DirectiveRegion(title, open.Start.Line, -1, open.StartOffset, -1, open.Start, null));
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.DirectiveUnclosedRegion, DiagnosticSeverity.Warning,
                title is null
                    ? "'#@region' is missing its '#@endregion'."
                    : $"'#@region' \"{title}\" is missing its '#@endregion'.",
                open.Start.Span));
        }

        return new DirectiveScanResult(
            directives.ToImmutable(), regions.ToImmutable(), diagnostics.ToImmutable());
    }

    /// <summary>Yields each line's text (without its newline), start offset and length.</summary>
    private static IEnumerable<(string Text, int Start, int Length)> EnumerateLines(string text)
    {
        int i = 0, n = text.Length;
        while (i < n)
        {
            int start = i;
            while (i < n && text[i] != '\n' && text[i] != '\r') i++;
            int len = i - start;
            yield return (text.Substring(start, len), start, len);
            // consume the newline (handle \r, \n, and \r\n as one break)
            if (i < n && text[i] == '\r') i++;
            if (i < n && text[i] == '\n') i++;
        }
    }
}
