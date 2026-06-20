// Implementation Plan �4.2 � line-oriented cursor over the token stream.
// Most Therion commands are line-based (with backslash continuations); this
// helper splits a token list into "logical lines" so parsers can match on
// keyword + tail without rewriting iteration logic each time.

using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// A "logical line": a contiguous slice of significant tokens (no whitespace,
/// no newlines, no line-continuations) that originally lived on the same
/// physical line (with backslash continuations collapsed).
/// Comments encountered before / on this line are exposed as <see cref="LeadingComments"/>;
/// an inline comment after the line's tokens is exposed as <see cref="TrailingComment"/>.
/// </summary>
public readonly record struct LogicalLine(
    ImmutableArray<TherionToken> Tokens,
    ImmutableArray<TherionToken> LeadingComments,
    SourceSpan Span,
    TherionToken? TrailingComment = null)
{
    public bool IsEmpty => Tokens.IsDefaultOrEmpty;
    public TherionToken Head => Tokens[0];
    public string Keyword => Tokens.IsDefaultOrEmpty ? string.Empty : Tokens[0].Text;
}

internal static class LogicalLineReader
{
    /// <summary>Split a flat token list into <see cref="LogicalLine"/>s.</summary>
    public static ImmutableArray<LogicalLine> Split(ImmutableArray<TherionToken> tokens)
    {
        var lines = ImmutableArray.CreateBuilder<LogicalLine>();
        var current = new List<TherionToken>();
        var leadingComments = new List<TherionToken>();
        TherionToken? lineStart = null;
        TherionToken? trailingComment = null;

        void Flush(TherionToken? lineEnd)
        {
            if (current.Count == 0)
            {
                // Pure-comment / blank line: only emit if comments were present
                // so they aren't lost (round-trip).
                if (leadingComments.Count > 0)
                {
                    var firstC = leadingComments[0];
                    var lastC = leadingComments[^1];
                    lines.Add(new LogicalLine(
                        ImmutableArray<TherionToken>.Empty,
                        leadingComments.ToImmutableArray(),
                        SpanFromTo(firstC, lastC)));
                    leadingComments.Clear();
                }
                trailingComment = null;
                return;
            }

            var first = current[0];
            var last = lineEnd ?? current[^1];
            lines.Add(new LogicalLine(
                current.ToImmutableArray(),
                leadingComments.ToImmutableArray(),
                SpanFromTo(first, last),
                trailingComment));
            current.Clear();
            leadingComments.Clear();
            trailingComment = null;
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            switch (t.Kind)
            {
                case TherionTokenKind.Whitespace:
                case TherionTokenKind.LineContinuation:
                    continue;

                case TherionTokenKind.NewLine:
                    Flush(t);
                    continue;

                case TherionTokenKind.LineComment:
                    if (current.Count == 0)
                        leadingComments.Add(t);
                    else
                        // Inline trailing comment: kept off the token stream but
                        // attached to the line so data rows can surface it.
                        trailingComment = t;
                    continue;

                default:
                    if (current.Count == 0) lineStart = t;
                    current.Add(t);
                    continue;
            }
        }

        Flush(null);
        return lines.ToImmutable();
    }

    internal static SourceSpan SpanFromTo(TherionToken from, TherionToken to)
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
}
