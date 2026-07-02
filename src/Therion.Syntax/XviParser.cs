// Parser for the Therion `.xvi` export format (`set XVI* {…}`, see XviAst). It is lenient by
// design — the format is machine-generated, so the goal is to read the data (stations, shots,
// grid, sketch lines) and only flag genuinely broken structure: an unterminated brace, an
// unknown `set` variable, stray non-`set` content, or a wrong-arity grid.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax;

public sealed class XviParser
{
    public ParseResult<XviFile> Parse(string filePath, string text, ParserOptions? options = null)
    {
        options ??= ParserOptions.Default;
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var (statements, leadingComments, strayLines) = XviTokenizer.Tokenize(filePath, text);

        double? gridSpacing = null;
        string gridUnits = string.Empty;
        var stations = ImmutableArray.CreateBuilder<XviStation>();
        var shots = ImmutableArray.CreateBuilder<XviShot>();
        var sketchLines = ImmutableArray.CreateBuilder<XviSketchLine>();
        var images = ImmutableArray.CreateBuilder<string>();
        XviGrid? grid = null;

        var lenient = options.Mode == ParserMode.Strict
            ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

        foreach (var st in statements)
        {
            if (!st.Terminated)
                diags.Add(Diagnostic.Create(DiagnosticCodes.XviUnterminatedBlock,
                    DiagnosticSeverity.Error,
                    $"'set {st.Name}' value is missing its closing '}}'.", st.Span));

            switch (st.Name)
            {
                case "XVIgrids":
                {
                    var toks = SplitTokens(st.Body);
                    if (toks.Count > 0 && TryNum(toks[0], out var sp)) gridSpacing = sp;
                    if (toks.Count > 1) gridUnits = toks[1];
                    break;
                }
                case "XVIgrid":
                {
                    var toks = SplitTokens(st.Body);
                    if (toks.Count == 8 && TryParseAll(toks, out var g))
                        grid = new XviGrid(g[0], g[1], g[2], g[3], g[4], g[5], (int)g[6], (int)g[7]);
                    else
                        diags.Add(Diagnostic.Create(DiagnosticCodes.XviMalformedGrid, lenient,
                            "'XVIgrid' expects 8 numeric values (x0 y0 xax yax xay yay nx ny).",
                            st.BodySpan));
                    break;
                }
                case "XVIstations":
                    foreach (var rec in BraceRecords(st.Body))
                    {
                        var t = SplitTokens(rec);
                        if (t.Count >= 3 && TryNum(t[0], out var x) && TryNum(t[1], out var y))
                            // The name is everything after the two coordinates (names may contain spaces).
                            stations.Add(new XviStation(x, y, string.Join(' ', t.GetRange(2, t.Count - 2))));
                        else if (t.Count == 2 && TryNum(t[0], out var x2) && TryNum(t[1], out var y2))
                            stations.Add(new XviStation(x2, y2, string.Empty));
                    }
                    break;
                case "XVIshots":
                    foreach (var rec in BraceRecords(st.Body))
                    {
                        var t = SplitTokens(rec);
                        if (t.Count >= 4 && TryNum(t[0], out var a) && TryNum(t[1], out var b)
                            && TryNum(t[2], out var c) && TryNum(t[3], out var d))
                            shots.Add(new XviShot(a, b, c, d));
                    }
                    break;
                case "XVIimages":
                    // Background sketch bitmaps written by therion's exporter (thexpmap.cxx; B7).
                    // Kept as raw records — image payloads are not needed for validation.
                    foreach (var rec in BraceRecords(st.Body))
                        images.Add(rec);
                    break;
                case "XVIsketchlines":
                    foreach (var rec in BraceRecords(st.Body))
                    {
                        var t = SplitTokens(rec);
                        if (t.Count == 0) continue;
                        var coords = ImmutableArray.CreateBuilder<double>();
                        for (int i = 1; i < t.Count; i++)
                            if (TryNum(t[i], out var v)) coords.Add(v);
                        sketchLines.Add(new XviSketchLine(t[0], coords.ToImmutable()));
                    }
                    break;
                default:
                    // Unknown `set` variable — lenient warning (some exporters add extra vars).
                    diags.Add(Diagnostic.Create(DiagnosticCodes.XviUnknownVariable, lenient,
                        $"Unknown XVI variable '{st.Name}'.", st.Span,
                        hint: "Expected XVIgrids, XVIstations, XVIshots, XVIsketchlines, XVIimages or XVIgrid."));
                    break;
            }
        }

        foreach (var (span, word) in strayLines)
            diags.Add(Diagnostic.Create(DiagnosticCodes.XviUnexpectedStatement, lenient,
                $"Unexpected statement '{word}' (expected a 'set XVI…' command).", span));

        SourceSpan fileSpan = statements.Length == 0
            ? new SourceSpan(filePath, SourceLocation.Start, SourceLocation.Start, 0, text.Length)
            : new SourceSpan(filePath, statements[0].Span.Start, statements[^1].Span.End,
                statements[0].Span.StartOffset,
                statements[^1].Span.StartOffset + statements[^1].Span.Length - statements[0].Span.StartOffset);

        var file = new XviFile(fileSpan, filePath, gridSpacing, gridUnits,
            stations.ToImmutable(), shots.ToImmutable(), sketchLines.ToImmutable(),
            grid, leadingComments)
        { Images = images.ToImmutable() };
        return new ParseResult<XviFile>(file, diags.ToImmutable());
    }

    /// <summary>Yields each top-level <c>{ … }</c> record inside a body (brace-depth aware).</summary>
    private static IEnumerable<string> BraceRecords(string body)
    {
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] != '{') { i++; continue; }
            int depth = 0, start = i + 1, j = i;
            for (; j < body.Length; j++)
            {
                if (body[j] == '{') depth++;
                else if (body[j] == '}') { depth--; if (depth == 0) break; }
            }
            int end = j < body.Length ? j : body.Length;
            yield return body[start..end];
            i = end + 1;
        }
    }

    /// <summary>Splits a flat body on whitespace into tokens (no brace handling).</summary>
    private static List<string> SplitTokens(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            list.Add(s[start..i]);
        }
        return list;
    }

    private static bool TryNum(string s, out double d) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    private static bool TryParseAll(List<string> toks, out double[] values)
    {
        values = new double[toks.Count];
        for (int i = 0; i < toks.Count; i++)
            if (!TryNum(toks[i], out values[i])) return false;
        return true;
    }
}
