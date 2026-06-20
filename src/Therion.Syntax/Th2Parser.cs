// Implementation Plan §4 — .th2 parser (M4).
// Recognizes scrap/point/line/area + their inner structure; surfaces
// `-sketch <xvi-path>` references as SketchReference nodes attached to scraps.

using System;
using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// Parser for Therion <c>.th2</c> 2-D drawing files. Block-structured: scraps
/// contain points, lines (with vertex bodies) and areas (with border lists).
/// </summary>
public sealed class Th2Parser
{
    private readonly TherionTokenizer _tokenizer = new();

    public ParseResult<TherionFile> Parse(string filePath, string sourceText, ParserOptions? options = null)
    {
        options ??= ParserOptions.Default;

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var tokens = _tokenizer.Tokenize(filePath, sourceText);
        var lines = LogicalLineReader.Split(tokens);

        int cursor = 0;
        var children = ParseBody(lines, ref cursor, filePath, options, null, null, diags);

        var fileSpan = sourceText.Length == 0
            ? SourceSpan.None with { FilePath = filePath }
            : new SourceSpan(filePath, SourceLocation.Start,
                new SourceLocation(1, sourceText.Length + 1), 0, sourceText.Length);

        var file = new TherionFile(fileSpan, filePath, children,
            options.Version ?? TherionSyntaxVersion.Default);
        return new ParseResult<TherionFile>(file, diags.ToImmutable());
    }

    private ImmutableArray<TherionNode> ParseBody(
        ImmutableArray<LogicalLine> lines,
        ref int cursor, string filePath, ParserOptions options,
        string? blockTerminator, string? blockKeyword,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        var children = ImmutableArray.CreateBuilder<TherionNode>();

        while (cursor < lines.Length)
        {
            var line = lines[cursor];
            foreach (var c in line.LeadingComments)
                children.Add(new TrivialComment(c.Span, c.Text));

            if (line.IsEmpty) { cursor++; continue; }
            var kw = line.Keyword;

            if (blockTerminator is not null &&
                string.Equals(kw, blockTerminator, StringComparison.OrdinalIgnoreCase))
            {
                cursor++;
                return children.ToImmutable();
            }

            cursor++;
            switch (kw.ToLowerInvariant())
            {
                case "scrap":
                    children.Add(ParseScrap(line, lines, ref cursor, filePath, options, diags));
                    break;
                case "point":
                    children.Add(ParsePoint(line, diags));
                    break;
                case "line":
                    children.Add(ParseLine(line, lines, ref cursor, options, diags));
                    break;
                case "area":
                    children.Add(ParseArea(line, lines, ref cursor, options, diags));
                    break;
                default:
                    var sev = options.Mode == ParserMode.Strict
                        ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                    diags.Add(Diagnostic.Create(DiagnosticCodes.UnknownCommand, sev,
                        $"Unknown .th2 command '{kw}'.", line.Head.Span));
                    children.Add(new UnknownCommand(line.Span, kw, JoinFrom(line, 1)));
                    break;
            }
        }

        if (blockTerminator is not null)
        {
            var sev = options.Mode == ParserMode.Strict
                ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2UnterminatedScrap, sev,
                $"Block '{blockKeyword}' missing '{blockTerminator}'.",
                children.Count > 0 ? children[^1].Span : SourceSpan.None));
        }

        return children.ToImmutable();
    }

    private ScrapBlock ParseScrap(
        LogicalLine line, ImmutableArray<LogicalLine> lines,
        ref int cursor, string filePath, ParserOptions options,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        string id = line.Tokens.Length > 1 ? line.Tokens[1].Text : string.Empty;
        string optionsRaw = JoinFrom(line, 2);

        // Extract any `-sketch <path> <x> <y>` option from the scrap header.
        var sketches = ExtractSketches(line, filePath);

        bool terminated = false;
        int startCursor = cursor;
        var body = ParseBody(lines, ref cursor, filePath, options,
            blockTerminator: "endscrap", blockKeyword: "scrap", diags);
        terminated = cursor != startCursor || body.Length > 0;

        var endSpan = body.Length > 0 ? body[^1].Span : line.Span;
        return new ScrapBlock(SpanUnion(line.Span, endSpan), id, optionsRaw,
            sketches, body, terminated);
    }

    private static ImmutableArray<SketchReference> ExtractSketches(LogicalLine line, string filePath)
    {
        var result = ImmutableArray.CreateBuilder<SketchReference>();
        for (int i = 0; i < line.Tokens.Length - 3; i++)
        {
            if (string.Equals(line.Tokens[i].Text, "-sketch", StringComparison.OrdinalIgnoreCase))
            {
                string xvi = line.Tokens[i + 1].Text;
                double x = ParseDouble(line.Tokens[i + 2].Text);
                double y = ParseDouble(line.Tokens[i + 3].Text);
                var span = line.Tokens[i].Span;
                result.Add(new SketchReference(span, Unquote(xvi), x, y));
            }
        }
        return result.ToImmutable();
    }

    private PointObject ParsePoint(LogicalLine line, ImmutableArray<Diagnostic>.Builder diags)
    {
        // point <x> <y> <type> [options]
        if (line.Tokens.Length < 4)
        {
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2MalformedPoint,
                DiagnosticSeverity.Warning,
                "'point' requires x, y, and type.", line.Span));
            return new PointObject(line.Span, 0, 0, string.Empty, JoinFrom(line, 1));
        }
        double x = ParseDouble(line.Tokens[1].Text);
        double y = ParseDouble(line.Tokens[2].Text);
        string type = line.Tokens[3].Text;
        return new PointObject(line.Span, x, y, type, JoinFrom(line, 4));
    }

    private LineObject ParseLine(
        LogicalLine header, ImmutableArray<LogicalLine> lines, ref int cursor,
        ParserOptions options, ImmutableArray<Diagnostic>.Builder diags)
    {
        // line <type> [options]
        string type = header.Tokens.Length > 1 ? header.Tokens[1].Text : string.Empty;
        string optsRaw = JoinFrom(header, 2);
        var vertices = ImmutableArray.CreateBuilder<LineVertex>();
        bool terminated = false;

        while (cursor < lines.Length)
        {
            var ln = lines[cursor];
            if (ln.IsEmpty) { cursor++; continue; }
            if (string.Equals(ln.Keyword, "endline", StringComparison.OrdinalIgnoreCase))
            {
                cursor++; terminated = true; break;
            }
            // Vertex: <x> <y> [options...] — first token is a number, not a keyword.
            if (ln.Tokens.Length >= 2 &&
                double.TryParse(ln.Tokens[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vx) &&
                double.TryParse(ln.Tokens[1].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vy))
            {
                vertices.Add(new LineVertex(ln.Span, vx, vy, JoinFrom(ln, 2)));
            }
            cursor++;
        }

        if (!terminated)
        {
            var sev = options.Mode == ParserMode.Strict
                ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2MalformedLine, sev,
                "'line' block is missing 'endline'.", header.Span));
        }

        var endSpan = vertices.Count > 0 ? vertices[^1].Span : header.Span;
        return new LineObject(SpanUnion(header.Span, endSpan), type, optsRaw,
            vertices.ToImmutable(), terminated);
    }

    private AreaObject ParseArea(
        LogicalLine header, ImmutableArray<LogicalLine> lines, ref int cursor,
        ParserOptions options, ImmutableArray<Diagnostic>.Builder diags)
    {
        string type = header.Tokens.Length > 1 ? header.Tokens[1].Text : string.Empty;
        string optsRaw = JoinFrom(header, 2);
        var borders = ImmutableArray.CreateBuilder<string>();
        bool terminated = false;

        while (cursor < lines.Length)
        {
            var ln = lines[cursor];
            if (ln.IsEmpty) { cursor++; continue; }
            if (string.Equals(ln.Keyword, "endarea", StringComparison.OrdinalIgnoreCase))
            {
                cursor++; terminated = true; break;
            }
            // Each line is a border line identifier.
            foreach (var t in ln.Tokens) borders.Add(t.Text);
            cursor++;
        }

        if (!terminated)
        {
            var sev = options.Mode == ParserMode.Strict
                ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2MalformedArea, sev,
                "'area' block is missing 'endarea'.", header.Span));
        }

        return new AreaObject(header.Span, type, optsRaw, borders.ToImmutable(), terminated);
    }

    // ---- helpers --------------------------------------------------------

    private static string JoinFrom(LogicalLine line, int from)
    {
        if (line.Tokens.Length <= from) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int i = from; i < line.Tokens.Length; i++)
        {
            if (i > from) sb.Append(' ');
            sb.Append(line.Tokens[i].Text);
        }
        return sb.ToString();
    }

    private static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d;

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private static SourceSpan SpanUnion(SourceSpan a, SourceSpan b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        int start = Math.Min(a.StartOffset, b.StartOffset);
        int end = Math.Max(a.StartOffset + a.Length, b.StartOffset + b.Length);
        var startLoc = a.StartOffset <= b.StartOffset ? a.Start : b.Start;
        var endLoc = a.StartOffset + a.Length >= b.StartOffset + b.Length ? a.End : b.End;
        return new SourceSpan(a.FilePath, startLoc, endLoc, start, end - start);
    }
}
