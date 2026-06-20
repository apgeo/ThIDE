// Implementation Plan §4.3 (AST shape — granular, immutable).
// Only the bare-minimum hierarchy is materialized in M1.
// .th / .th2 / .xvi specific nodes land in M2 / M4.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>Root of every Therion AST node. Carries a <see cref="SourceSpan"/>.</summary>
public abstract record TherionNode(SourceSpan Span);

/// <summary>A line comment (<c># ...</c>) preserved as trivia for round-tripping.</summary>
public sealed record TrivialComment(SourceSpan Span, string Text) : TherionNode(Span);

/// <summary>Base class for any top-level Therion command (<c>survey</c>, <c>source</c>, ...).</summary>
public abstract record TherionCommand(SourceSpan Span, string Keyword) : TherionNode(Span);

/// <summary>
/// Generic command produced when a specific <c>ICommandHandler</c> hasn't been
/// implemented yet. Holds the keyword and the raw remaining tokens / text so
/// nothing is silently dropped in lenient mode.
/// </summary>
public sealed record UnknownCommand(
    SourceSpan Span,
    string Keyword,
    string RawArguments) : TherionCommand(Span, Keyword);

/// <summary>A parsed file (any format): its sequence of top-level children + diagnostics.</summary>
public sealed record TherionFile(
    SourceSpan Span,
    string Path,
    ImmutableArray<TherionNode> Children,
    TherionSyntaxVersion Version) : TherionNode(Span);
