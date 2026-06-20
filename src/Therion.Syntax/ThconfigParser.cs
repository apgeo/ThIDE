// Implementation Plan §3 / §4.2 (.thconfig parser). M1 minimal: recognizes
// `source`, `layout`, `export`, `select`, `cs` at top level + line comments;
// anything else becomes an UnknownCommand so nothing is silently dropped.
//
// Therion source-of-truth: therion/src/thconfig.cxx. thbook §3 "Configuration".

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// Minimal <c>.thconfig</c> parser. Builds a <see cref="TherionFile"/> populated
/// with <see cref="TrivialComment"/> and <see cref="UnknownCommand"/> children.
/// </summary>
public sealed class ThconfigParser
{
    /// <summary>Top-level keywords that may appear in a <c>.thconfig</c> (per thbook §3).</summary>
    public static readonly ImmutableHashSet<string> TopLevelKeywords =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "source", "layout", "export", "select", "cs",
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
