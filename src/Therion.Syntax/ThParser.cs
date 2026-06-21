// Implementation Plan �4 � .th parser (M2).
// Recognizes: survey/centreline (block), data, fix, equate, input, team, date.
// Unknown commands fall back to UnknownCommand (lenient) or produce an error (strict).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// Parser for Therion <c>.th</c> survey-data files. Returns a <see cref="TherionFile"/>
/// whose <see cref="TherionFile.Children"/> is a mix of block commands, line commands,
/// and preserved <see cref="TrivialComment"/> trivia.
/// </summary>
public sealed class ThParser
{
    private readonly TherionTokenizer _tokenizer = new();
    private readonly ICommandRegistry? _registry;

    public ThParser() { }

    /// <summary>
    /// Plugin-aware ctor (Plan �4.4 / D1 follow-up A). Any keyword not handled
    /// by the built-in switch is dispatched to a registered <see cref="ICommandHandler"/>
    /// before falling back to <see cref="UnknownCommand"/>.
    /// </summary>
    public ThParser(ICommandRegistry? registry) { _registry = registry; }

    public ParseResult<TherionFile> Parse(
        string filePath, string sourceText, ParserOptions? options = null)
    {
        options ??= ParserOptions.Default;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var tokens = _tokenizer.Tokenize(filePath, sourceText);
        var lines = LogicalLineReader.Split(tokens);

        int cursor = 0;
        var children = ParseBlockBody(
            lines, ref cursor, filePath, options,
            blockTerminator: null, blockKeyword: null, parentBlock: null, diagnostics);

        var fileSpan = sourceText.Length == 0
            ? SourceSpan.None with { FilePath = filePath }
            : new SourceSpan(filePath, SourceLocation.Start,
                new SourceLocation(1, sourceText.Length + 1), 0, sourceText.Length);

        var file = new TherionFile(fileSpan, filePath, children,
            options.Version ?? TherionSyntaxVersion.Default);

        return new ParseResult<TherionFile>(file, diagnostics.ToImmutable());
    }

    private ImmutableArray<TherionNode> ParseBlockBody(
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        string filePath,
        ParserOptions options,
        string? blockTerminator,
        string? blockKeyword,
        string? parentBlock,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var children = ImmutableArray.CreateBuilder<TherionNode>();

        while (cursor < lines.Length)
        {
            var line = lines[cursor];

            // Preserve leading comments as nodes.
            foreach (var c in line.LeadingComments)
                children.Add(new TrivialComment(c.Span, c.Text));

            if (line.IsEmpty)
            {
                cursor++;
                continue;
            }

            var kw = line.Keyword;

            // Block terminators. Normalize centerline/centreline aliases before comparing.
            if (blockTerminator is not null &&
                string.Equals(NormalizeCentrelineAlias(kw), NormalizeCentrelineAlias(blockTerminator), StringComparison.OrdinalIgnoreCase))
            {
                cursor++;
                return children.ToImmutable();
            }

            // If we see a different "end..." while parsing a block, surface it
            // and stop � caller will report a missing-terminator diagnostic.
            if (blockTerminator is not null && kw.StartsWith("end", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.MismatchedBlockTerminator,
                    DiagnosticSeverity.Error,
                    $"Expected '{blockTerminator}' to close '{blockKeyword}' but found '{kw}'.",
                    line.Span));
                return children.ToImmutable();
            }

            cursor++;
            var node = ParseLine(line, lines, ref cursor, filePath, options, parentBlock: blockKeyword, diagnostics);
            if (node is not null) children.Add(node);
        }

        if (blockTerminator is not null)
        {
            var severity = options.Mode == ParserMode.Strict
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.UnterminatedBlock,
                severity,
                $"Block '{blockKeyword}' is missing its '{blockTerminator}' terminator.",
                children.Count > 0 ? children[^1].Span : SourceSpan.None));
        }

        return children.ToImmutable();
    }

    private TherionNode? ParseLine(
        LogicalLine line,
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        string filePath,
        ParserOptions options,
        string? parentBlock,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var kw = line.Keyword;

        return kw.ToLowerInvariant() switch
        {
            "survey"                       => ParseSurvey(line, lines, ref cursor, filePath, options, diagnostics),
            "centreline" or "centerline"   => ParseCentreline(line, lines, ref cursor, filePath, options, diagnostics),
            "map"                          => ParseMap(line, lines, ref cursor, diagnostics, options),
            "data"                         => ParseData(line, diagnostics),
            "flags"                        => ParseFlags(line),
            "fix"                          => ParseFix(line, diagnostics),
            "equate"                       => ParseEquate(line, diagnostics),
            "input" or "load"              => ParseInput(line, diagnostics),
            "team"                         => ParseTeam(line),
            "date"                         => ParseDate(line),
            // Recognized file-header directive (charset declaration). Carries no
            // semantics for us, but must not be flagged as an unknown command.
            "encoding"                     => new UnknownCommand(line.Span, line.Keyword, JoinFrom(line, 1)),
            _ => ParseViaRegistryOrFallback(line, parentBlock, options, filePath, diagnostics),
        };
    }

    /// <summary>
    /// Plugin dispatch (Plan �4.4 / D1 follow-up A): consult <see cref="ICommandRegistry"/>
    /// before treating the line as a centreline data row or an unknown command.
    /// </summary>
    private TherionNode ParseViaRegistryOrFallback(
        LogicalLine line, string? parentBlock, ParserOptions options, string filePath,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (_registry is not null && _registry.TryGet(line.Keyword, out var handler))
        {
            try
            {
                var ctx = new ParseContext(filePath, options, line.Tokens, 0);
                var result = handler.Parse(ctx);
                foreach (var d in result.Diagnostics) diagnostics.Add(d);
                if (result.Value is { } node) return node;
            }
            catch (Exception ex)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.PluginHandlerFailed,
                    DiagnosticSeverity.Warning,
                    $"Command handler for '{line.Keyword}' threw: {ex.Message}",
                    line.Span));
            }
        }
        return ParseUnknownOrDataRow(line, parentBlock, options, diagnostics);
    }

    /// <summary>
    /// Inside a <c>centreline</c> block, any non-keyword line is treated as a
    /// data row (e.g., <c>0 1 12.5 0 -5</c>) instead of an unknown command.
    /// </summary>
    private TherionNode ParseUnknownOrDataRow(
        LogicalLine line, string? parentBlock, ParserOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (parentBlock is not null &&
            (string.Equals(parentBlock, "centreline", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(parentBlock, "centerline", StringComparison.OrdinalIgnoreCase)))
        {
            var b = ImmutableArray.CreateBuilder<string>();
            foreach (var t in line.Tokens) b.Add(t.Text);
            return new DataRow(line.Span, b.ToImmutable(),
                TrailingComment: CleanComment(line.TrailingComment?.Text));
        }
        return ParseUnknown(line, options, diagnostics);
    }

    private SurveyCommand ParseSurvey(
        LogicalLine line,
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        string filePath,
        ParserOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var (name, rest) = HeadAndRest(line, fromIndex: 1);
        var inner = ParseBlockBody(lines, ref cursor, filePath, options,
            blockTerminator: "endsurvey", blockKeyword: "survey", parentBlock: "survey", diagnostics);

        var endSpan = inner.Length > 0 ? inner[^1].Span : line.Span;
        var fullSpan = SpanUnion(line.Span, endSpan);
        bool terminated = cursor <= lines.Length &&
                          (inner.Length == 0 ||
                           lines.Length > 0 && cursor - 1 < lines.Length); // best-effort marker
        return new SurveyCommand(fullSpan, name ?? string.Empty, rest, inner, terminated);
    }

    private CentrelineCommand ParseCentreline(
        LogicalLine line,
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        string filePath,
        ParserOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var rest = JoinFrom(line, 1);
        var inner = ParseBlockBody(lines, ref cursor, filePath, options,
            blockTerminator: "endcentreline", blockKeyword: line.Keyword, parentBlock: "centreline", diagnostics);

        var endSpan = inner.Length > 0 ? inner[^1].Span : line.Span;
        return new CentrelineCommand(SpanUnion(line.Span, endSpan), rest, inner, IsTerminated: true);
    }

    /// <summary>
    /// Parses a <c>map &lt;id&gt; ... endmap</c> block. The body is consumed opaquely
    /// (its lines are scrap/map references, not commands) so they don't surface as
    /// "unknown command" diagnostics.
    /// </summary>
    private MapCommand ParseMap(
        LogicalLine line,
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserOptions options)
    {
        var (id, opts) = HeadAndRest(line, fromIndex: 1);

        bool terminated = false;
        var lastSpan = line.Span;
        while (cursor < lines.Length)
        {
            var ln = lines[cursor];
            if (!ln.IsEmpty &&
                string.Equals(ln.Keyword, "endmap", StringComparison.OrdinalIgnoreCase))
            {
                lastSpan = ln.Span;
                cursor++;
                terminated = true;
                break;
            }
            if (!ln.IsEmpty) lastSpan = ln.Span;
            cursor++;
        }

        if (!terminated)
        {
            var severity = options.Mode == ParserMode.Strict
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning;
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.UnterminatedBlock,
                severity,
                "Block 'map' is missing its 'endmap' terminator.",
                line.Span));
        }

        return new MapCommand(SpanUnion(line.Span, lastSpan), id ?? string.Empty, opts, terminated);
    }

    private DataCommand ParseData(LogicalLine line, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (line.Tokens.Length < 2)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.MalformedData,
                DiagnosticSeverity.Warning,
                "'data' requires at least a style name.",
                line.Span));
            return new DataCommand(line.Span, string.Empty, ImmutableArray<string>.Empty);
        }

        var style = line.Tokens[1].Text;
        var fields = ImmutableArray.CreateBuilder<string>();
        for (int i = 2; i < line.Tokens.Length; i++)
            fields.Add(line.Tokens[i].Text);

        return new DataCommand(line.Span, style, fields.ToImmutable());
    }

    private FlagsCommand ParseFlags(LogicalLine line)
    {
        var b = ImmutableArray.CreateBuilder<string>();
        for (int i = 1; i < line.Tokens.Length; i++) b.Add(line.Tokens[i].Text);
        return new FlagsCommand(line.Span, b.ToImmutable());
    }

    private StationFix ParseFix(LogicalLine line, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // fix <station> <x> <y> <z> [stdev...]
        if (line.Tokens.Length < 5)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.MalformedFix,
                DiagnosticSeverity.Warning,
                "'fix' requires station and x y z coordinates.",
                line.Span));
            return new StationFix(line.Span,
                line.Tokens.Length > 1 ? line.Tokens[1].Text : string.Empty,
                0, 0, 0, string.Empty);
        }

        var station = line.Tokens[1].Text;
        double x = ParseDouble(line.Tokens[2].Text);
        double y = ParseDouble(line.Tokens[3].Text);
        double z = ParseDouble(line.Tokens[4].Text);
        var opts = JoinFrom(line, 5);
        return new StationFix(line.Span, station, x, y, z, opts);
    }

    private EquateCommand ParseEquate(LogicalLine line, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (line.Tokens.Length < 3)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.MalformedEquate,
                DiagnosticSeverity.Warning,
                "'equate' requires at least two stations.",
                line.Span));
        }

        var b = ImmutableArray.CreateBuilder<string>();
        for (int i = 1; i < line.Tokens.Length; i++) b.Add(line.Tokens[i].Text);
        return new EquateCommand(line.Span, b.ToImmutable());
    }

    private InputCommand ParseInput(LogicalLine line, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var path = line.Tokens.Length > 1 ? Unquote(line.Tokens[1].Text) : string.Empty;
        return new InputCommand(line.Span, path);
    }

    private TeamCommand ParseTeam(LogicalLine line)
    {
        var name = line.Tokens.Length > 1 ? Unquote(line.Tokens[1].Text) : string.Empty;
        var rest = JoinFrom(line, 2);
        return new TeamCommand(line.Span, name, rest);
    }

    private DateCommand ParseDate(LogicalLine line)
    {
        var value = JoinFrom(line, 1);
        return new DateCommand(line.Span, value);
    }

    private TherionCommand ParseUnknown(
        LogicalLine line, ParserOptions options, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var severity = options.Mode == ParserMode.Strict
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning;

        diagnostics.Add(Diagnostic.Create(
            DiagnosticCodes.UnknownCommand,
            severity,
            $"Unknown command '{line.Keyword}'.",
            line.Head.Span));

        return new UnknownCommand(line.Span, line.Keyword, JoinFrom(line, 1));
    }

    // ----- helpers --------------------------------------------------------

    private static (string? HeadValue, string RestValue) HeadAndRest(LogicalLine line, int fromIndex)
    {
        if (line.Tokens.Length <= fromIndex) return (null, string.Empty);
        var head = line.Tokens[fromIndex].Text;
        var rest = JoinFrom(line, fromIndex + 1);
        return (head, rest);
    }

    private static string JoinFrom(LogicalLine line, int fromIndex)
    {
        if (line.Tokens.Length <= fromIndex) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int i = fromIndex; i < line.Tokens.Length; i++)
        {
            if (i > fromIndex) sb.Append(' ');
            sb.Append(line.Tokens[i].Text);
        }
        return sb.ToString();
    }

    private static double ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d;

    /// <summary>Strips the leading <c>#</c> and surrounding whitespace from a comment token.</summary>
    internal static string? CleanComment(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var s = raw.TrimStart();
        if (s.StartsWith('#')) s = s[1..];
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1]
            : text;

    // Therion treats "centerline" and "centreline" (and their "end*" forms) as equivalent.
    private static string NormalizeCentrelineAlias(string kw) =>
        kw.Replace("centerline", "centreline", StringComparison.OrdinalIgnoreCase);

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
