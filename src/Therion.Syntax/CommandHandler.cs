// Implementation Plan §4.4 / §5.3 — extensibility hooks.
// Stub interfaces ready for DI-based registration in M6+.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>Context passed to a command handler during parsing.</summary>
public readonly record struct ParseContext(
    string FilePath,
    ParserOptions Options,
    ImmutableArray<TherionToken> Tokens,
    int Cursor);

/// <summary>
/// Pluggable handler for a single Therion command keyword (Implementation Plan §4.4).
/// Implementations are registered via DI (<c>AddTherionCommand&lt;THandler&gt;()</c>).
/// </summary>
public interface ICommandHandler
{
    string Keyword { get; }
    TherionSyntaxVersion MinVersion { get; }
    ParseResult<TherionCommand> Parse(ParseContext ctx);
}

/// <summary>A bundle of command handlers + semantic rules describing a Therion dialect.</summary>
public interface IDialect
{
    string Name { get; }
    TherionSyntaxVersion Version { get; }
    ImmutableArray<ICommandHandler> CommandHandlers { get; }
}

/// <summary>Registry of all command handlers known to the parser.</summary>
public interface ICommandRegistry
{
    bool TryGet(string keyword, out ICommandHandler handler);
    void Register(ICommandHandler handler);
}

/// <summary>Default thread-safe registry implementation.</summary>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ICommandHandler> _map
        = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string keyword, out ICommandHandler handler)
        => _map.TryGetValue(keyword, out handler!);

    public void Register(ICommandHandler handler)
        => _map[handler.Keyword] = handler;
}
