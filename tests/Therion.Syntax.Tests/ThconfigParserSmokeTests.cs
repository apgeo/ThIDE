using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ThconfigParserSmokeTests
{
    [Fact]
    public void Parses_known_top_level_commands_without_warnings()
    {
        const string text = """
            # sample project
            source cave.th
            layout default
            export model -fmt lox -o cave.lox
            """;

        var parser = new ThconfigParser();
        var result = parser.Parse("sample.thconfig", text);

        Assert.NotNull(result.Value);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(4, result.Value!.Children.Length); // 1 comment + 3 commands
    }

    [Fact]
    public void Unknown_command_emits_warning_in_lenient_mode()
    {
        var parser = new ThconfigParser();
        var result = parser.Parse("x.thconfig", "frobnicate foo bar");

        Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnknownCommand, result.Diagnostics[0].Code.Value);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Unknown_command_is_error_in_strict_mode()
    {
        var parser = new ThconfigParser();
        var result = parser.Parse("x.thconfig", "frobnicate foo", new ParserOptions(ParserMode.Strict));

        Assert.True(result.HasErrors);
    }
}