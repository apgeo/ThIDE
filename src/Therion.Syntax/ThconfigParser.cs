// Implementation Plan �3 / �4.2 (.thconfig parser). M1 minimal: recognizes
// `source`, `layout`, `export`, `select`, `cs` at top level + line comments;
// anything else becomes an UnknownCommand so nothing is silently dropped.
//
// Therion source-of-truth: therion/src/thconfig.cxx. thbook �3 "Configuration".

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// Minimal <c>.thconfig</c> parser. Builds a <see cref="TherionFile"/> populated
/// with <see cref="TrivialComment"/> and <see cref="UnknownCommand"/> children.
/// </summary>
public sealed class ThconfigParser
{
    /// <summary>Top-level keywords that may appear in a <c>.thconfig</c> (per thbook �3).</summary>
    public static readonly ImmutableHashSet<string> TopLevelKeywords =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "source", "layout", "lookup", "export", "select", "unselect", "cs",
            "input", "load", "system-charset", "encoding", "language", "lang",
            "translate", "revise", "group", "endgroup",
            // additional documented .thconfig commands (thbook §"Processing data")
            "maps", "maps-offset", "log", "text", "system",
            "scrap-sort", "sketch-warp", "sketch-colors", "setup");

    private readonly TherionTokenizer _tokenizer = new();

    /// <summary>Parse the given source text as a <c>.thconfig</c> file.</summary>
    public ParseResult<TherionFile> Parse(
        string filePath,
        string sourceText,
        ParserOptions? options = null)
    {
        options ??= ParserOptions.Default;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var children = ImmutableArray.CreateBuilder<TherionNode>();

        var tokens = _tokenizer.Tokenize(filePath, sourceText);

        int i = 0;
        while (i < tokens.Length)
        {
            var tok = tokens[i];

            switch (tok.Kind)
            {
                case TherionTokenKind.LineComment:
                    children.Add(new TrivialComment(tok.Span, tok.Text));
                    i++;
                    break;

                case TherionTokenKind.Whitespace:
                case TherionTokenKind.NewLine:
                case TherionTokenKind.LineContinuation:
                    i++;
                    break;

                case TherionTokenKind.Identifier:
                {
                    var commandStart = tok;
                    int lineEnd = i + 1;
                    while (lineEnd < tokens.Length &&
                           tokens[lineEnd].Kind != TherionTokenKind.NewLine)
                    {
                        if (tokens[lineEnd].Kind == TherionTokenKind.LineContinuation &&
                            lineEnd + 1 < tokens.Length)
                        {
                            lineEnd += 2;
                            continue;
                        }
                        lineEnd++;
                    }

                    var keyword = commandStart.Text;
                    var fullSpan = SpanFromTo(commandStart, tokens[lineEnd - 1]);
                    var rawArgs = ExtractRawArguments(sourceText, tokens, i + 1, lineEnd);
                    var lineTokens = CollectSignificant(tokens, i, lineEnd);

                    // Bare `source … endsource` (no filename on the opener) is the inline-source
                    // multi-line form (thbook §source): its body is inline Therion data, not a file
                    // path, so consume it opaquely. A single-line `source <file>` keeps its
                    // UnknownCommand shape so SourceGraph still follows the include.
                    if (string.Equals(keyword, "source", StringComparison.OrdinalIgnoreCase) &&
                        lineTokens.Count == 1 &&
                        TryFindBlockEnd(tokens, lineEnd, "endsource", out int srcNext, out int srcEnd))
                    {
                        var srcSpan = SpanFromTo(commandStart, tokens[srcEnd]);
                        children.Add(new UnknownCommand(srcSpan, keyword, rawArgs));
                        i = srcNext;
                        break;
                    }

                    // `lookup … endlookup` stays opaque (its body is lookup-table code, not syntax).
                    if (OpaqueBlockEnd(keyword) is { } endKeyword &&
                        TryFindBlockEnd(tokens, lineEnd, endKeyword, out int blockNext, out int blockLastToken))
                    {
                        var blockSpan = SpanFromTo(commandStart, tokens[blockLastToken]);
                        children.Add(new UnknownCommand(blockSpan, keyword, rawArgs));
                        i = blockNext;
                        break;
                    }

                    // `layout … endlayout` is now parsed into a typed LayoutCommand (LANG-02),
                    // skipping embedded `code … endcode` blocks opaquely. A header-only `layout id`
                    // with no matching endlayout is treated as a single line (no body consumed).
                    if (string.Equals(keyword, "layout", StringComparison.OrdinalIgnoreCase))
                    {
                        var layout = ParseLayout(commandStart, lineTokens, fullSpan, rawArgs,
                            tokens, lineEnd, out int next);
                        children.Add(layout);
                        i = next;
                        break;
                    }

                    // Typed non-traversal commands (cs / select / unselect / export / maps).
                    var typed = ParseTypedConfigCommand(keyword, lineTokens, fullSpan, rawArgs, options, diagnostics);
                    if (typed is not null)
                    {
                        children.Add(typed);
                        i = lineEnd;
                        break;
                    }

                    if (!TopLevelKeywords.Contains(keyword))
                    {
                        var severity = options.Mode == ParserMode.Strict
                            ? DiagnosticSeverity.Error
                            : DiagnosticSeverity.Warning;

                        diagnostics.Add(Diagnostic.Create(
                            DiagnosticCodes.UnknownCommand,
                            severity,
                            $"Unknown top-level command '{keyword}'.",
                            commandStart.Span,
                            hint: "Expected one of: " + string.Join(", ", TopLevelKeywords)));
                    }

                    children.Add(new UnknownCommand(fullSpan, keyword, rawArgs));
                    i = lineEnd;
                    break;
                }

                default:
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.UnexpectedToken,
                        DiagnosticSeverity.Warning,
                        $"Unexpected token '{tok.Text}'.",
                        tok.Span));
                    i++;
                    break;
            }
        }

        var fileSpan = sourceText.Length == 0
            ? SourceSpan.None with { FilePath = filePath }
            : new SourceSpan(filePath, SourceLocation.Start,
                new SourceLocation(1, sourceText.Length + 1), 0, sourceText.Length);

        var file = new TherionFile(
            fileSpan,
            filePath,
            children.ToImmutable(),
            options.Version ?? TherionSyntaxVersion.Default);

        return new ParseResult<TherionFile>(file, diagnostics.ToImmutable());
    }

    /// <summary>The closing keyword for an opaque (unparsed-body) block opener, or null.</summary>
    private static string? OpaqueBlockEnd(string keyword) =>
        string.Equals(keyword, "lookup", StringComparison.OrdinalIgnoreCase) ? "endlookup" :
        null;

    /// <summary>Collects the significant (non-trivia) tokens of a line in <c>[start, end)</c>.</summary>
    private static List<TherionToken> CollectSignificant(
        ImmutableArray<TherionToken> tokens, int start, int end)
    {
        var list = new List<TherionToken>();
        for (int k = start; k < end && k < tokens.Length; k++)
        {
            var t = tokens[k];
            if (t.Kind is TherionTokenKind.Whitespace or TherionTokenKind.NewLine
                or TherionTokenKind.LineContinuation or TherionTokenKind.LineComment) continue;
            list.Add(t);
        }
        return list;
    }

    /// <summary>The whitespace-joined text of <paramref name="line"/> tokens from <paramref name="from"/>.</summary>
    private static string JoinFrom(IReadOnlyList<TherionToken> line, int from)
    {
        if (from >= line.Count) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int k = from; k < line.Count; k++)
        {
            if (k > from) sb.Append(' ');
            sb.Append(line[k].Text);
        }
        return sb.ToString();
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    /// <summary>
    /// Dispatches the typed (non-traversal) .thconfig commands. Returns null for keywords that
    /// should keep their existing UnknownCommand handling (source/input/load/system/…).
    /// </summary>
    private static TherionCommand? ParseTypedConfigCommand(
        string keyword, List<TherionToken> line, SourceSpan span, string rawArgs,
        ParserOptions options, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        switch (keyword.ToLowerInvariant())
        {
            case "cs":
            {
                // Rejoin adjacent tokens so "EPSG:3794" (split at ':') is recovered.
                var system = JoinAdjacent(line, 1);
                if (system.Length > 0 && !CoordinateSystems.IsKnown(system))
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.UnknownCoordinateSystem,
                        options.Mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        $"Unknown coordinate system '{system}'.", line.Count > 1 ? line[1].Span : span,
                        hint: "Use UTM<zone>, EPSG:<n>, lat-long, S-MERC, …"));
                return new CsCommand(span, system);
            }
            case "select":
            case "unselect":
            {
                var obj = line.Count > 1 ? line[1].Text : string.Empty;
                return new SelectCommand(span, obj, JoinFrom(line, 2),
                    IsUnselect: string.Equals(keyword, "unselect", StringComparison.OrdinalIgnoreCase));
            }
            case "export":
            {
                var what = line.Count > 1 ? line[1].Text : string.Empty;
                var (fmt, output) = ExtractExportOptions(line);
                return new ExportCommand(span, what, JoinFrom(line, 2)) { Format = fmt, Output = output };
            }
            case "maps":
            case "maps-offset":
            {
                bool on = line.Count <= 1 ||
                          !string.Equals(line[1].Text, "off", StringComparison.OrdinalIgnoreCase);
                return new MapsCommand(span, on,
                    IsOffset: string.Equals(keyword, "maps-offset", StringComparison.OrdinalIgnoreCase));
            }
            default:
                return null;
        }
    }

    /// <summary>Joins line tokens from <paramref name="from"/> that touch in source (no whitespace).</summary>
    private static string JoinAdjacent(List<TherionToken> line, int from)
    {
        if (from >= line.Count) return string.Empty;
        var sb = new System.Text.StringBuilder(line[from].Text);
        int prevEnd = line[from].Span.StartOffset + line[from].Span.Length;
        for (int k = from + 1; k < line.Count; k++)
        {
            if (line[k].Span.StartOffset != prevEnd) break;
            sb.Append(line[k].Text);
            prevEnd = line[k].Span.StartOffset + line[k].Span.Length;
        }
        return sb.ToString();
    }

    private static (string? Format, string? Output) ExtractExportOptions(List<TherionToken> line)
    {
        string? fmt = null, output = null;
        for (int k = 2; k < line.Count - 1; k++)
        {
            var t = line[k].Text;
            if (string.Equals(t, "-fmt", StringComparison.OrdinalIgnoreCase)) fmt = line[k + 1].Text;
            else if (t is "-o" || string.Equals(t, "-output", StringComparison.OrdinalIgnoreCase))
                output = Unquote(line[k + 1].Text);
        }
        return (fmt, output);
    }

    /// <summary>
    /// Parses a <c>layout &lt;id&gt; [opts] … endlayout</c> block (LANG-02). When no matching
    /// <c>endlayout</c> exists the layout is treated as a single header line (no body consumed),
    /// matching the previous lenient behaviour. <paramref name="next"/> is where parsing resumes.
    /// </summary>
    private static LayoutCommand ParseLayout(
        TherionToken commandStart, List<TherionToken> headerLine, SourceSpan headerSpan, string rawArgs,
        ImmutableArray<TherionToken> tokens, int headerEnd, out int next)
    {
        var id = headerLine.Count > 1 ? headerLine[1].Text : string.Empty;

        if (!TryFindBlockEnd(tokens, headerEnd, "endlayout", out int blockNext, out int endTokenIdx))
        {
            next = headerEnd;
            return new LayoutCommand(headerSpan, id, rawArgs,
                ImmutableArray<LayoutOption>.Empty, ImmutableArray<LayoutCodeBlock>.Empty,
                IsTerminated: false);
        }

        next = blockNext;
        var blockSpan = SpanFromTo(commandStart, tokens[endTokenIdx]);

        // Split the body (between the header line and endlayout) into logical lines.
        int bodyStart = headerEnd;                 // first token after the header line
        int bodyEnd = endTokenIdx;                 // the endlayout token (exclusive)
        var bodySlice = bodyStart < bodyEnd
            ? ImmutableArray.Create(tokens, bodyStart, bodyEnd - bodyStart)
            : ImmutableArray<TherionToken>.Empty;
        var bodyLines = LogicalLineReader.Split(bodySlice);
        var parts = LayoutBodyParser.Parse(bodyLines);

        return new LayoutCommand(blockSpan, id, rawArgs,
            parts.Options, parts.CodeBlocks, IsTerminated: true)
        {
            CopyFrom = parts.CopyFrom,
            CoordinateSystem = parts.CoordinateSystem,
            SymbolSet = parts.SymbolSet,
            SymbolDirectives = parts.SymbolDirectives,
        };
    }

    /// <summary>
    /// Scans for the <paramref name="endKeyword"/> that closes an opaque block, ignoring the
    /// body in between (including any nested <c>endcode</c>/<c>enddef</c>).
    /// </summary>
    /// <param name="start">Index to begin scanning (just past the opener's header line).</param>
    /// <param name="next">Index just past the closing line (where parsing resumes).</param>
    /// <param name="lastContentToken">Index of the closing token (for the block span).</param>
    private static bool TryFindBlockEnd(
        ImmutableArray<TherionToken> tokens, int start, string endKeyword, out int next, out int lastContentToken)
    {
        next = tokens.Length;
        lastContentToken = -1;

        int i = start;
        bool atLineStart = true;
        while (i < tokens.Length)
        {
            var t = tokens[i];
            if (t.Kind == TherionTokenKind.NewLine) { atLineStart = true; i++; continue; }
            if (t.Kind is TherionTokenKind.Whitespace or TherionTokenKind.LineContinuation) { i++; continue; }

            if (atLineStart)
            {
                atLineStart = false;
                if (t.Kind == TherionTokenKind.Identifier &&
                    string.Equals(t.Text, endKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    lastContentToken = i;
                    int k = i + 1;
                    while (k < tokens.Length && tokens[k].Kind != TherionTokenKind.NewLine) k++;
                    next = k < tokens.Length ? k + 1 : k;
                    return true;
                }
            }
            i++;
        }
        return false;
    }

    private static SourceSpan SpanFromTo(TherionToken from, TherionToken to)
    {
        int startOffset = from.Span.StartOffset;
        int endOffset = to.Span.StartOffset + to.Span.Length;
        return new SourceSpan(
            from.Span.FilePath,
            from.Span.Start,
            to.Span.End,
            startOffset,
            endOffset - startOffset);
    }

    private static string ExtractRawArguments(
        string source, ImmutableArray<TherionToken> tokens, int startIndex, int endIndexExclusive)
    {
        if (startIndex >= endIndexExclusive) return string.Empty;
        int s = tokens[startIndex].Span.StartOffset;
        var last = tokens[endIndexExclusive - 1].Span;
        int e = last.StartOffset + last.Length;
        return source.Substring(s, e - s).Trim();
    }
}
