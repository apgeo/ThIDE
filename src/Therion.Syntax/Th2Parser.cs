// Implementation Plan �4 � .th2 parser (M4).
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

        Schema.SchemaValidator.Validate(file, Schema.SchemaContext.Th2TopLevel,
            options.EffectiveValidation, Schema.SchemaRegistry.Default, diags, options.Mode);

        return new ParseResult<TherionFile>(file, diags.ToImmutable());
    }

    private ImmutableArray<TherionNode> ParseBody(
        ImmutableArray<LogicalLine> lines,
        ref int cursor, string filePath, ParserOptions options,
        string? blockTerminator, string? blockKeyword,
        ImmutableArray<Diagnostic>.Builder diags,
        string? blockId = null)
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
                // `endscrap [<id>]` must match the opener's id when present.
                if (blockId is { Length: > 0 } && line.Tokens.Length > 1 &&
                    !string.Equals(line.Tokens[1].Text, blockId, StringComparison.Ordinal))
                    diags.Add(Diagnostic.Create(
                        DiagnosticCodes.BlockIdMismatch,
                        options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        $"'{kw} {line.Tokens[1].Text}' does not match '{blockKeyword} {blockId}'.",
                        line.Tokens[1].Span));
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
                    children.Add(ParsePoint(line, options, diags));
                    break;
                case "line":
                    children.Add(ParseLine(line, lines, ref cursor, options, diags));
                    break;
                case "area":
                    children.Add(ParseArea(line, lines, ref cursor, options, diags));
                    break;
                case "join":
                    children.Add(ParseJoin(line));
                    break;
                // File-header charset directive — recognized, carries no .th2 semantics.
                case "encoding":
                    children.Add(new UnknownCommand(line.Span, kw, JoinFrom(line, 1)));
                    break;
                // `comment … endcomment` multi-line comment block — valid at top level AND inside a
                // scrap (this body loop is shared). Consumed opaquely as a single TrivialComment.
                case "comment":
                    children.Add(ParseCommentBlock(line, lines, ref cursor));
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
        if (line.Tokens.Length > 1 && TherionIdentifiers.FirstIllegalChar(id) is { } badId)
            diags.Add(Diagnostic.Create(
                DiagnosticCodes.IllegalIdentifier,
                options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                $"Scrap id '{id}' contains an illegal character '{badId}'.",
                line.Tokens[1].Span));

        // Extract any `-sketch <path> <x> <y>` option from the scrap header.
        var sketches = ExtractSketches(line, filePath);

        bool terminated = false;
        int startCursor = cursor;
        var body = ParseBody(lines, ref cursor, filePath, options,
            blockTerminator: "endscrap", blockKeyword: "scrap", diags, blockId: id);
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

    private PointObject ParsePoint(LogicalLine line, ParserOptions options, ImmutableArray<Diagnostic>.Builder diags)
    {
        // point <x> <y> <type> [options]
        if (line.Tokens.Length < 4)
        {
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2MalformedPoint,
                DiagnosticSeverity.Warning,
                "'point' requires x, y, and type.", line.Span));
            return new PointObject(line.Span, 0, 0, string.Empty, JoinFrom(line, 1));
        }
        var sevNum = options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
        if (!IsNumeric(line.Tokens[1].Text))
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2MalformedPoint, sevNum,
                $"'point' x coordinate '{line.Tokens[1].Text}' is not a number.", line.Tokens[1].Span));
        if (!IsNumeric(line.Tokens[2].Text))
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2MalformedPoint, sevNum,
                $"'point' y coordinate '{line.Tokens[2].Text}' is not a number.", line.Tokens[2].Span));
        double x = ParseDouble(line.Tokens[1].Text);
        double y = ParseDouble(line.Tokens[2].Text);
        var (type, optStart) = CoalesceFrom(line.Tokens, 3);

        if (!Th2Symbols.IsKnownPointType(type))
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2UnknownPointType,
                options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                $"Unknown point type '{type}'.", line.Tokens[3].Span));

        var pointOpts = Th2OptionList.Parse(line.Tokens, optStart);
        ValidateOptions(pointOpts, options, diags);
        return new PointObject(line.Span, x, y, type, JoinFrom(line, optStart))
        {
            Options = pointOpts,
        };
    }

    private LineObject ParseLine(
        LogicalLine header, ImmutableArray<LogicalLine> lines, ref int cursor,
        ParserOptions options, ImmutableArray<Diagnostic>.Builder diags)
    {
        // line <type> [options]
        var (type, lineOptStart) = header.Tokens.Length > 1
            ? CoalesceFrom(header.Tokens, 1) : (string.Empty, header.Tokens.Length);
        string optsRaw = JoinFrom(header, lineOptStart);

        // FUTURE (custom symbol awareness): Therion lets a project DEFINE its own point/line/area
        // types (user symbols), so a type we don't know is not necessarily wrong — it may be valid
        // in context. We deliberately keep this a lenient WARNING (never an error in lenient mode)
        // rather than expanding the built-in known-type list with project-specific names. A future
        // pass could suppress this warning when the enclosing scrap/project declares the matching
        // custom symbol, instead of flagging every unrecognized type. Until then this is signal, not
        // a hard failure.
        if (type.Length > 0 && !Th2Symbols.IsKnownLineType(type))
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2UnknownLineType,
                options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                $"Unknown line type '{type}'.", header.Tokens[1].Span));

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
            // Vertex: <x> <y> [options...] � first token is a number, not a keyword.
            if (ln.Tokens.Length >= 2 &&
                double.TryParse(ln.Tokens[0].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vx) &&
                double.TryParse(ln.Tokens[1].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var vy))
            {
                vertices.Add(new LineVertex(ln.Span, vx, vy, JoinFrom(ln, 2))
                {
                    Options = Th2OptionList.Parse(ln.Tokens, 2),
                });
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
        var lineOpts = Th2OptionList.Parse(header.Tokens, lineOptStart);
        ValidateOptions(lineOpts, options, diags);
        return new LineObject(SpanUnion(header.Span, endSpan), type, optsRaw,
            vertices.ToImmutable(), terminated)
        {
            Options = lineOpts,
        };
    }

    /// <summary>Parses <c>join a b [...] [-opt val]</c>: leading non-option tokens are targets.</summary>
    private static JoinCommand ParseJoin(LogicalLine line)
    {
        var targets = ImmutableArray.CreateBuilder<string>();
        int i = 1;
        for (; i < line.Tokens.Length; i++)
        {
            if (line.Tokens[i].Text.StartsWith('-')) break;
            targets.Add(line.Tokens[i].Text);
        }
        return new JoinCommand(line.Span, targets.ToImmutable(), JoinFrom(line, i));
    }

    private AreaObject ParseArea(
        LogicalLine header, ImmutableArray<LogicalLine> lines, ref int cursor,
        ParserOptions options, ImmutableArray<Diagnostic>.Builder diags)
    {
        var (type, areaOptStart) = header.Tokens.Length > 1
            ? CoalesceFrom(header.Tokens, 1) : (string.Empty, header.Tokens.Length);
        string optsRaw = JoinFrom(header, areaOptStart);

        if (type.Length > 0 && !Th2Symbols.IsKnownAreaType(type))
            diags.Add(Diagnostic.Create(DiagnosticCodes.Th2UnknownAreaType,
                options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                $"Unknown area type '{type}'.", header.Tokens[1].Span));

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
            // Each body line lists a border line identifier (skip option/keyword tokens like `-id`).
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

        var areaOpts = Th2OptionList.Parse(header.Tokens, areaOptStart);
        ValidateOptions(areaOpts, options, diags);
        return new AreaObject(header.Span, type, optsRaw, borders.ToImmutable(), terminated)
        {
            Options = areaOpts,
        };
    }

    // ---- helpers --------------------------------------------------------

    /// <summary>
    /// Coalesces adjacent (no-whitespace-gap) tokens starting at <paramref name="start"/> into one
    /// string, returning it and the index of the next whitespace-separated token. Recovers types
    /// the lexer split at punctuation (e.g. <c>station:fixed</c>, <c>u:splay</c>, <c>wall:blocks</c>).
    /// </summary>
    private static (string Text, int Next) CoalesceFrom(ImmutableArray<TherionToken> tokens, int start)
    {
        if (tokens.Length <= start) return (string.Empty, start);
        var sb = new System.Text.StringBuilder(tokens[start].Text);
        int prevEnd = tokens[start].Span.StartOffset + tokens[start].Span.Length;
        int i = start + 1;
        for (; i < tokens.Length; i++)
        {
            if (tokens[i].Span.StartOffset != prevEnd) break;
            sb.Append(tokens[i].Text);
            prevEnd = tokens[i].Span.StartOffset + tokens[i].Span.Length;
        }
        return (sb.ToString(), i);
    }

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

    private static bool IsNumeric(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>Warns on any <c>-option</c> name that isn't a known point/line/area option.</summary>
    private static void ValidateOptions(
        Th2OptionList opts, ParserOptions options, ImmutableArray<Diagnostic>.Builder diags)
    {
        var sev = options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
        foreach (var o in opts.Options)
            if (!Th2Symbols.IsKnownOption(o.Name))
                diags.Add(Diagnostic.Create(DiagnosticCodes.Th2UnknownOption, sev,
                    $"Unknown option '-{o.Name}'.", o.Span));
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    /// <summary>
    /// <c>comment … endcomment</c> multi-line comment block — body is free text, consumed opaquely
    /// and preserved as one <see cref="TrivialComment"/>. Scans for an <c>endcomment</c> line (the
    /// body may contain <c>end…</c> prose); if none exists ahead the bare <c>comment</c> line is kept.
    /// </summary>
    private static TrivialComment ParseCommentBlock(
        LogicalLine line, ImmutableArray<LogicalLine> lines, ref int cursor)
    {
        for (int i = cursor; i < lines.Length; i++)
            if (!lines[i].IsEmpty &&
                string.Equals(lines[i].Keyword, "endcomment", StringComparison.OrdinalIgnoreCase))
            {
                cursor = i + 1;
                return new TrivialComment(SpanUnion(line.Span, lines[i].Span), "comment");
            }
        return new TrivialComment(line.Span, "comment");
    }

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
