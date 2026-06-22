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
            "source", "layout", "lookup", "export", "select", "cs",
            "input", "system-charset", "encoding", "language", "lang",
            "translate", "revise", "group", "endgroup");

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

                    // A `layout … endlayout` (or `lookup … endlookup`) block is consumed
                    // opaquely: its body is metapost / tex / lookup code, not Therion syntax,
                    // so we neither validate nor warn about the lines inside it. (A bare
                    // single-line opener with no matching closer falls through to normal
                    // handling.)
                    if (OpaqueBlockEnd(keyword) is { } endKeyword &&
                        TryFindBlockEnd(tokens, lineEnd, endKeyword, out int blockNext, out int blockLastToken))
                    {
                        var blockSpan = SpanFromTo(commandStart, tokens[blockLastToken]);
                        children.Add(new UnknownCommand(blockSpan, keyword, rawArgs));
                        i = blockNext;
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
        string.Equals(keyword, "layout", StringComparison.OrdinalIgnoreCase) ? "endlayout" :
        string.Equals(keyword, "lookup", StringComparison.OrdinalIgnoreCase) ? "endlookup" :
        null;

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
