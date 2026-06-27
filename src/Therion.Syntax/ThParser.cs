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
            // Children see the surrounding context. For most blocks parentBlock == blockKeyword;
            // a context-transparent `group` passes its enclosing context through instead.
            var node = ParseLine(line, lines, ref cursor, filePath, options,
                parentBlock: parentBlock ?? blockKeyword, diagnostics);
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
            "group"                        => ParseGroup(line, lines, ref cursor, filePath, options, parentBlock, diagnostics),
            "surface"                      => ParseSurface(line, lines, ref cursor, diagnostics, options),
            "map"                          => ParseMap(line, lines, ref cursor, diagnostics, options),
            // layout … endlayout can appear in `.th` files that are input-ed (LANG-02/10).
            "layout"                       => ParseLayout(line, lines, ref cursor, diagnostics, options),
            "data"                         => ParseData(line, options, diagnostics),
            "flags"                        => ParseFlags(line),
            "fix"                          => ParseFix(line, diagnostics),
            "equate"                       => ParseEquate(line, diagnostics),
            "join"                         => ParseJoin(line),
            "input" or "load"              => ParseInput(line, diagnostics),
            "team"                         => ParseTeam(line),
            "date"                         => ParseDate(line),
            // ---- centreline / survey metadata commands (LANG-04/05/03) ----
            "units"                        => ParseUnits(line, options, diagnostics),
            "calibrate"                    => ParseCalibrate(line, options, diagnostics),
            "declination"                  => ParseDeclination(line, options, diagnostics),
            "grid-angle"                   => ParseGridAngle(line),
            "sd"                           => ParseSd(line),
            "grade"                        => ParseGrade(line),
            "infer"                        => ParseInfer(line),
            "mark"                         => ParseMark(line),
            "station"                      => ParseStation(line),
            "cs"                           => ParseCs(line, options, diagnostics),
            "extend"                       => ParseExtend(line),
            "break"                        => new BreakCommand(line.Span),
            "walls"                        => new WallsCommand(line.Span, JoinFrom(line, 1)),
            "vthreshold"                   => ParseVThreshold(line),
            "station-names"                => ParseStationNames(line),
            "instrument"                   => ParseInstrument(line),
            "explo-date"                   => new ExploDateCommand(line.Span, JoinFrom(line, 1)),
            "explo-team"                   => ParseExploTeam(line),
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
            var (values, valueSpans) = CoalesceValues(line);
            return new DataRow(line.Span, values,
                TrailingComment: CleanComment(line.TrailingComment?.Text))
            {
                ValueSpans = valueSpans,
            };
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
        // LANG-08: capture the scrap / sub-map references on the body lines (each line's first
        // token is the member id; any trailing tokens are placement offsets / comments).
        var members = ImmutableArray.CreateBuilder<MapMemberRef>();
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
            if (!ln.IsEmpty)
            {
                lastSpan = ln.Span;
                var head = ln.Tokens[0];
                // Skip option lines (e.g. a stray "-projection") — members are bare ids.
                if (!head.Text.StartsWith('-'))
                    members.Add(new MapMemberRef(head.Span, head.Text));
            }
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

        return new MapCommand(SpanUnion(line.Span, lastSpan), id ?? string.Empty, opts, terminated)
        {
            Members = members.ToImmutable(),
            Projection = ExtractProjection(opts),
        };
    }

    /// <summary>Extracts the <c>-projection &lt;value&gt;</c> option from a map header's raw options.</summary>
    private static string? ExtractProjection(string optionsRaw)
    {
        if (string.IsNullOrEmpty(optionsRaw)) return null;
        var parts = optionsRaw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
            if (string.Equals(parts[i], "-projection", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        return null;
    }

    /// <summary>
    /// Parses a <c>layout &lt;id&gt; ... endlayout</c> block in a <c>.th</c> file (some projects keep
    /// layouts in input-ed <c>.th</c> files). Reuses <see cref="LayoutBodyParser"/> so the typed
    /// model + opaque <c>code … endcode</c> handling match the <c>.thconfig</c> path (LANG-02/10).
    /// </summary>
    private LayoutCommand ParseLayout(
        LogicalLine line,
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserOptions options)
    {
        var id = line.Tokens.Length > 1 ? line.Tokens[1].Text : string.Empty;
        var rawArgs = JoinFrom(line, 2);
        var body = ImmutableArray.CreateBuilder<LogicalLine>();
        bool terminated = false;
        var lastSpan = line.Span;

        while (cursor < lines.Length)
        {
            var ln = lines[cursor];
            if (!ln.IsEmpty && string.Equals(ln.Keyword, "endlayout", StringComparison.OrdinalIgnoreCase))
            {
                lastSpan = ln.Span;
                cursor++;
                terminated = true;
                break;
            }
            if (!ln.IsEmpty) { lastSpan = ln.Span; body.Add(ln); }
            cursor++;
        }

        if (!terminated)
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.UnterminatedBlock,
                options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                "Block 'layout' is missing its 'endlayout' terminator.",
                line.Span));

        var parts = LayoutBodyParser.Parse(body.ToImmutable());
        return new LayoutCommand(SpanUnion(line.Span, lastSpan), id, rawArgs,
            parts.Options, parts.CodeBlocks, terminated)
        {
            CopyFrom = parts.CopyFrom,
            CoordinateSystem = parts.CoordinateSystem,
            SymbolSet = parts.SymbolSet,
            SymbolDirectives = parts.SymbolDirectives,
        };
    }

    /// <summary>
    /// Parses a <c>group ... endgroup</c> block. The body inherits <paramref name="parentBlock"/>
    /// (the enclosing context) so a group nested in a centreline still parses shot rows as data.
    /// </summary>
    private GroupCommand ParseGroup(
        LogicalLine line,
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        string filePath,
        ParserOptions options,
        string? parentBlock,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var inner = ParseBlockBody(lines, ref cursor, filePath, options,
            blockTerminator: "endgroup", blockKeyword: "group", parentBlock: parentBlock, diagnostics);
        var endSpan = inner.Length > 0 ? inner[^1].Span : line.Span;
        return new GroupCommand(SpanUnion(line.Span, endSpan), inner, IsTerminated: true);
    }

    /// <summary>
    /// Parses a <c>surface ... endsurface</c> block. The body (grid header + elevation values
    /// or bitmaps) is consumed opaquely � it isn't command syntax and can be very large.
    /// </summary>
    private SurfaceCommand ParseSurface(
        LogicalLine line,
        ImmutableArray<LogicalLine> lines,
        ref int cursor,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserOptions options)
    {
        var opts = JoinFrom(line, 1);
        bool terminated = false;
        var lastSpan = line.Span;
        while (cursor < lines.Length)
        {
            var ln = lines[cursor];
            if (!ln.IsEmpty &&
                string.Equals(ln.Keyword, "endsurface", StringComparison.OrdinalIgnoreCase))
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
                "Block 'surface' is missing its 'endsurface' terminator.",
                line.Span));
        }

        return new SurfaceCommand(SpanUnion(line.Span, lastSpan), opts, terminated);
    }

    private DataCommand ParseData(
        LogicalLine line, ParserOptions options, ImmutableArray<Diagnostic>.Builder diagnostics)
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
        var fieldList = fields.ToImmutable();

        // LANG-05: validate the style name + each reading keyword. Lenient by default.
        var warn = options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
        if (DataStyles.ParseStyle(style) == DataStyle.Unknown)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.UnknownDataStyle, warn,
                $"Unknown data style '{style}'.",
                line.Tokens[1].Span,
                hint: "Expected one of: " + string.Join(", ", DataStyles.StyleNames)));
        }
        for (int i = 2; i < line.Tokens.Length; i++)
        {
            var reading = line.Tokens[i].Text;
            if (!DataStyles.IsKnownReading(reading))
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.UnknownDataReading, warn,
                    $"Unknown data reading '{reading}'.",
                    line.Tokens[i].Span));
        }

        return new DataCommand(line.Span, style, fieldList);
    }

    // ===== centreline metadata command parsers (LANG-04/05/03) ============

    private static ImmutableArray<string> TokensFrom(LogicalLine line, int fromIndex)
    {
        if (line.Tokens.Length <= fromIndex) return ImmutableArray<string>.Empty;
        var b = ImmutableArray.CreateBuilder<string>(line.Tokens.Length - fromIndex);
        for (int i = fromIndex; i < line.Tokens.Length; i++) b.Add(line.Tokens[i].Text);
        return b.ToImmutable();
    }

    private static bool LooksNumeric(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static double? TryDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary><c>units &lt;quantity list&gt; [&lt;factor&gt;] &lt;units&gt;</c>.</summary>
    private UnitsCommand ParseUnits(
        LogicalLine line, ParserOptions options, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var quantities = ImmutableArray.CreateBuilder<string>();
        double? factor = null;
        string unit = string.Empty;
        var warn = options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

        for (int i = 1; i < line.Tokens.Length; i++)
        {
            var t = line.Tokens[i].Text;
            if (LooksNumeric(t)) { factor = TryDouble(t); continue; }       // optional scale factor
            if (MeasurementUnits.IsUnit(t)) { unit = t; continue; }          // the units name
            quantities.Add(t);                                               // a quantity keyword
            if (!MeasurementUnits.IsQuantity(t))
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.MalformedUnits, warn,
                    $"Unknown unit quantity '{t}'.", line.Tokens[i].Span));
        }
        if (unit.Length == 0)
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.MalformedUnits, warn,
                "'units' requires a unit name (e.g. metres, degrees).", line.Span));

        return new UnitsCommand(line.Span, quantities.ToImmutable(), factor, unit, JoinFrom(line, 1));
    }

    /// <summary><c>calibrate &lt;quantity list&gt; &lt;zero error&gt; [&lt;scale&gt;]</c>.</summary>
    private CalibrateCommand ParseCalibrate(
        LogicalLine line, ParserOptions options, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var quantities = ImmutableArray.CreateBuilder<string>();
        var numbers = new List<double>();
        for (int i = 1; i < line.Tokens.Length; i++)
        {
            var t = line.Tokens[i].Text;
            if (LooksNumeric(t)) numbers.Add(TryDouble(t)!.Value);
            else quantities.Add(t);
        }
        double zero = numbers.Count > 0 ? numbers[0] : 0d;
        double? scale = numbers.Count > 1 ? numbers[1] : null;
        if (numbers.Count == 0)
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.MalformedCalibrate,
                options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                "'calibrate' requires a zero-error value.", line.Span));
        return new CalibrateCommand(line.Span, quantities.ToImmutable(), zero, scale, JoinFrom(line, 1));
    }

    /// <summary>
    /// <c>declination [&lt;value&gt; &lt;units&gt;]</c> (single value, dated list, or reset).
    /// Only the simple single-value form is decoded; the rest is kept raw.
    /// </summary>
    private DeclinationCommand ParseDeclination(
        LogicalLine line, ParserOptions options, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var raw = JoinFrom(line, 1);
        // reset form: `declination` with no args, or `declination -`
        if (line.Tokens.Length == 1 ||
            (line.Tokens.Length == 2 && line.Tokens[1].Text == "-"))
            return new DeclinationCommand(line.Span, null, null, IsReset: true, raw);

        double? value = null;
        string? unit = null;
        int numericCount = 0;
        foreach (var tok in TokensFrom(line, 1))
        {
            if (LooksNumeric(tok)) { numericCount++; value ??= TryDouble(tok); }
            else if (MeasurementUnits.TryAngle(tok) is not null) unit = tok;
        }
        // A single numeric + unit is the simple form; multiple numerics ⇒ dated list (keep value=null).
        if (numericCount != 1) value = null;
        return new DeclinationCommand(line.Span, value, unit, IsReset: false, raw);
    }

    private static GridAngleCommand ParseGridAngle(LogicalLine line)
    {
        double? value = line.Tokens.Length > 1 ? TryDouble(line.Tokens[1].Text) : null;
        string? unit = line.Tokens.Length > 2 ? line.Tokens[2].Text : null;
        return new GridAngleCommand(line.Span, value, unit);
    }

    /// <summary><c>sd &lt;quantity list&gt; &lt;value&gt; &lt;units&gt;</c>.</summary>
    private static SdCommand ParseSd(LogicalLine line)
    {
        var quantities = ImmutableArray.CreateBuilder<string>();
        double? value = null;
        string? unit = null;
        for (int i = 1; i < line.Tokens.Length; i++)
        {
            var t = line.Tokens[i].Text;
            if (LooksNumeric(t)) value ??= TryDouble(t);
            else if (value is not null && MeasurementUnits.IsUnit(t)) unit = t;
            else quantities.Add(t);
        }
        return new SdCommand(line.Span, quantities.ToImmutable(), value, unit, JoinFrom(line, 1));
    }

    private static GradeCommand ParseGrade(LogicalLine line) =>
        new(line.Span, TokensFrom(line, 1));

    /// <summary><c>infer &lt;what&gt; &lt;on/off&gt;</c>.</summary>
    private static InferCommand ParseInfer(LogicalLine line)
    {
        var what = line.Tokens.Length > 1 ? line.Tokens[1].Text : string.Empty;
        bool on = line.Tokens.Length > 2 &&
                  string.Equals(line.Tokens[2].Text, "on", StringComparison.OrdinalIgnoreCase);
        return new InferCommand(line.Span, what, on);
    }

    /// <summary><c>mark [&lt;station list&gt;] &lt;type&gt;</c> — last token is the type.</summary>
    private static MarkCommand ParseMark(LogicalLine line)
    {
        if (line.Tokens.Length < 2)
            return new MarkCommand(line.Span, ImmutableArray<string>.Empty, string.Empty);
        var markType = line.Tokens[^1].Text;
        var stations = ImmutableArray.CreateBuilder<string>();
        for (int i = 1; i < line.Tokens.Length - 1; i++) stations.Add(line.Tokens[i].Text);
        return new MarkCommand(line.Span, stations.ToImmutable(), markType);
    }

    /// <summary><c>station &lt;station&gt; &lt;comment&gt; [&lt;flags&gt;]</c>.</summary>
    private static StationCommand ParseStation(LogicalLine line)
    {
        var station = line.Tokens.Length > 1 ? line.Tokens[1].Text : string.Empty;
        string? comment = line.Tokens.Length > 2 ? Unquote(line.Tokens[2].Text) : null;
        if (string.IsNullOrEmpty(comment)) comment = null;
        var flags = ImmutableArray.CreateBuilder<string>();
        for (int i = 3; i < line.Tokens.Length; i++) flags.Add(line.Tokens[i].Text);
        return new StationCommand(line.Span, station, comment, flags.ToImmutable());
    }

    /// <summary><c>cs &lt;coordinate system&gt;</c> + validation (LANG-03).</summary>
    private CsCommand ParseCs(
        LogicalLine line, ParserOptions options, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        // The tokenizer breaks "epsg:3794" at the ':'; rejoin adjacent (no-whitespace) tokens
        // so the whole system name ("EPSG:3794", "OSGB:SK") is recovered before validating.
        var (system, span) = ReadAdjacentTokens(line, fromIndex: 1);
        if (system.Length > 0 && !CoordinateSystems.IsKnown(system))
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.UnknownCoordinateSystem,
                options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                $"Unknown coordinate system '{system}'.",
                span,
                hint: "Use UTM<zone>, EPSG:<n>, lat-long, S-MERC, …"));
        return new CsCommand(line.Span, system);
    }

    /// <summary>
    /// Joins tokens starting at <paramref name="fromIndex"/> that touch in the source (no
    /// intervening whitespace) into one string + its covering span. Recovers identifiers the
    /// tokenizer split at punctuation (e.g. <c>EPSG:3794</c>, <c>OSGB:SK</c>).
    /// </summary>
    private static (string Text, SourceSpan Span) ReadAdjacentTokens(LogicalLine line, int fromIndex)
    {
        if (line.Tokens.Length <= fromIndex) return (string.Empty, line.Span);
        var first = line.Tokens[fromIndex];
        var sb = new System.Text.StringBuilder(first.Text);
        int prevEnd = first.Span.StartOffset + first.Span.Length;
        var last = first;
        for (int i = fromIndex + 1; i < line.Tokens.Length; i++)
        {
            var t = line.Tokens[i];
            if (t.Span.StartOffset != prevEnd) break; // whitespace gap → end of the joined word
            sb.Append(t.Text);
            prevEnd = t.Span.StartOffset + t.Span.Length;
            last = t;
        }
        return (sb.ToString(), SpanUnion(first.Span, last.Span));
    }

    /// <summary><c>extend &lt;spec&gt; [&lt;station&gt; …]</c>.</summary>
    private static ExtendCommand ParseExtend(LogicalLine line)
    {
        var spec = line.Tokens.Length > 1 ? line.Tokens[1].Text : string.Empty;
        return new ExtendCommand(line.Span, spec, TokensFrom(line, 2));
    }

    private static VThresholdCommand ParseVThreshold(LogicalLine line)
    {
        double? value = line.Tokens.Length > 1 ? TryDouble(line.Tokens[1].Text) : null;
        string? unit = line.Tokens.Length > 2 ? line.Tokens[2].Text : null;
        return new VThresholdCommand(line.Span, value, unit);
    }

    private static StationNamesCommand ParseStationNames(LogicalLine line)
    {
        var prefix = line.Tokens.Length > 1 ? Unquote(line.Tokens[1].Text) : string.Empty;
        var suffix = line.Tokens.Length > 2 ? Unquote(line.Tokens[2].Text) : string.Empty;
        return new StationNamesCommand(line.Span, prefix, suffix);
    }

    /// <summary><c>instrument &lt;quantity list&gt; &lt;description&gt;</c>.</summary>
    private static InstrumentCommand ParseInstrument(LogicalLine line)
    {
        // The description is a (usually quoted) trailing string; everything before it is quantities.
        var quantities = ImmutableArray.CreateBuilder<string>();
        string description = string.Empty;
        for (int i = 1; i < line.Tokens.Length; i++)
        {
            var t = line.Tokens[i].Text;
            if (t.Length > 0 && t[0] == '"') { description = Unquote(t); break; }
            quantities.Add(t);
        }
        return new InstrumentCommand(line.Span, quantities.ToImmutable(), description);
    }

    private static ExploTeamCommand ParseExploTeam(LogicalLine line)
    {
        var name = line.Tokens.Length > 1 ? Unquote(line.Tokens[1].Text) : string.Empty;
        return new ExploTeamCommand(line.Span, name, JoinFrom(line, 2));
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

    private InputCommand ParseInput(LogicalLine line, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var path = ReadPathArgument(line, fromIndex: 1);
        return new InputCommand(line.Span, path);
    }

    /// <summary>
    /// Reads a filesystem path argument (e.g. for <c>input</c>/<c>load</c>) starting at
    /// <paramref name="fromIndex"/>. The tokenizer splits an unquoted word at digit/letter
    /// boundaries, so a path whose name starts with (or contains) digits — like
    /// <c>20150926_ovi/20150926_ps1.th2</c> — arrives as several <em>adjacent</em> tokens.
    /// Re-join tokens that touch in the source (no intervening whitespace) so the whole path
    /// is recovered; a quoted path is a single string token and is just unquoted.
    /// </summary>
    private static string ReadPathArgument(LogicalLine line, int fromIndex)
    {
        if (line.Tokens.Length <= fromIndex) return string.Empty;
        var first = line.Tokens[fromIndex];
        var sb = new System.Text.StringBuilder(first.Text);
        int prevEnd = first.Span.StartOffset + first.Span.Length;
        for (int i = fromIndex + 1; i < line.Tokens.Length; i++)
        {
            var t = line.Tokens[i];
            if (t.Span.StartOffset != prevEnd) break; // whitespace gap → end of the path word
            sb.Append(t.Text);
            prevEnd = t.Span.StartOffset + t.Span.Length;
        }
        return Unquote(sb.ToString());
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

    /// <summary>
    /// Reconstructs the whitespace-delimited <em>values</em> of a data row from the token stream.
    /// The tokenizer over-splits barewords at digit/letter/punctuation boundaries (e.g. a station
    /// named <c>38a</c> arrives as <c>38</c>+<c>a</c>); this coalesces tokens that touch in the
    /// source (no intervening whitespace) back into one value, so a 5-column row yields 5 values
    /// regardless of station naming. Fixes both data-row arity checks and station binding.
    /// </summary>
    private static (ImmutableArray<string> Values, ImmutableArray<SourceSpan> Spans) CoalesceValues(LogicalLine line)
    {
        if (line.Tokens.IsDefaultOrEmpty)
            return (ImmutableArray<string>.Empty, ImmutableArray<SourceSpan>.Empty);
        var values = ImmutableArray.CreateBuilder<string>();
        var spans = ImmutableArray.CreateBuilder<SourceSpan>();
        var sb = new System.Text.StringBuilder();
        int prevEnd = -1;
        SourceSpan groupStart = default, groupEnd = default;
        foreach (var t in line.Tokens)
        {
            if (prevEnd >= 0 && t.Span.StartOffset != prevEnd)
            {
                values.Add(sb.ToString());
                spans.Add(JoinSpans(groupStart, groupEnd));
                sb.Clear();
            }
            if (sb.Length == 0) groupStart = t.Span;
            groupEnd = t.Span;
            sb.Append(t.Text);
            prevEnd = t.Span.StartOffset + t.Span.Length;
        }
        if (sb.Length > 0)
        {
            values.Add(sb.ToString());
            spans.Add(JoinSpans(groupStart, groupEnd));
        }
        return (values.ToImmutable(), spans.ToImmutable());
    }

    /// <summary>Union of two spans on the same line (first value token → last value token).</summary>
    private static SourceSpan JoinSpans(SourceSpan first, SourceSpan last) =>
        new(first.FilePath, first.Start, last.End, first.StartOffset,
            (last.StartOffset + last.Length) - first.StartOffset);

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
