// Plan §4.4 / D1 follow-up A — verifies ThParser dispatches unknown keywords
// through ICommandRegistry before falling back to UnknownCommand.

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public sealed class CommandRegistryDispatchTests
{
    private sealed record HelloCommand(SourceSpan Span, string Payload)
        : TherionCommand(Span, "hello");

    private sealed class HelloHandler : ICommandHandler
    {
        public string Keyword => "hello";
        public TherionSyntaxVersion MinVersion => TherionSyntaxVersion.Default;
        public ParseResult<TherionCommand> Parse(ParseContext ctx)
        {
            var span = ctx.Tokens.Length > 0 ? ctx.Tokens[0].Span : SourceSpan.None;
            var payload = ctx.Tokens.Length > 1 ? ctx.Tokens[1].Text : string.Empty;
            return new ParseResult<TherionCommand>(
                new HelloCommand(span, payload),
                ImmutableArray<Diagnostic>.Empty);
        }
    }

    private sealed class ThrowingHandler : ICommandHandler
    {
        public string Keyword => "boom";
        public TherionSyntaxVersion MinVersion => TherionSyntaxVersion.Default;
        public ParseResult<TherionCommand> Parse(ParseContext ctx)
            => throw new System.InvalidOperationException("nope");
    }

    [Fact]
    public void Registered_handler_consumes_unknown_keyword()
    {
        var registry = new CommandRegistry();
        registry.Register(new HelloHandler());
        var result = new ThParser(registry).Parse("x.th", "hello world\n");

        Assert.Empty(result.Diagnostics);
        var node = Assert.Single(result.Value!.Children.OfType<HelloCommand>());
        Assert.Equal("world", node.Payload);
    }

    [Fact]
    public void Handler_exceptions_become_plugin_diagnostics_and_fall_back()
    {
        var registry = new CommandRegistry();
        registry.Register(new ThrowingHandler());
        var result = new ThParser(registry).Parse("x.th", "boom\n");

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.PluginHandlerFailed);
        // Falls back to UnknownCommand so the line isn't dropped.
        Assert.Contains(result.Value!.Children.OfType<UnknownCommand>(), c => c.Keyword == "boom");
    }

    [Fact]
    public void Without_registry_unknown_keyword_remains_UnknownCommand()
    {
        var result = new ThParser().Parse("x.th", "hello world\n");
        Assert.Contains(result.Value!.Children.OfType<UnknownCommand>(), c => c.Keyword == "hello");
    }
}
