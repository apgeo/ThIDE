// #6 — `join` must be a recognized command (no more TH0010 "Unknown command 'join'").

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class JoinParsingTests
{
    [Fact]
    public void Th_join_parses_to_JoinCommand_without_unknown_command_diagnostic()
    {
        var result = new ThParser().Parse("/p/all.th", """
            survey s
              join SP_ps110@SV-a SP_ps111@SV-b -smooth on
            endsurvey
            """);

        var join = result.Value!.Children
            .OfType<SurveyCommand>().Single()
            .Children.OfType<JoinCommand>().Single();

        Assert.Equal(new[] { "SP_ps110@SV-a", "SP_ps111@SV-b" }, join.Targets);
        Assert.Contains("-smooth", join.OptionsRaw);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.UnknownCommand);
    }

    [Fact]
    public void Th2_join_parses_to_JoinCommand_without_unknown_command_diagnostic()
    {
        var result = new Th2Parser().Parse("/p/s.th2", """
            scrap sc1
              join L1 L2
            endscrap
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.UnknownCommand);
    }
}
