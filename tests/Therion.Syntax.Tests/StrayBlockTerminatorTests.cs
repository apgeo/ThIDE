// A block terminator that closes nothing open (`endscrap` inside a survey) is reported but does not
// end the block it appears in: the children after it stay with their block instead of moving to
// the enclosing scope. A terminator that closes an enclosing block still ends the inner one.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class StrayBlockTerminatorTests
{
    private const string Shots2 =
        "  centreline\n" +
        "    data normal from to length compass clino\n" +
        "    2 3 5 0 0\n" +
        "  endcentreline\n";

    [Theory]
    [InlineData("endscrap")]
    [InlineData("endline")]
    [InlineData("endmap")]
    public void Terminator_of_no_open_block_keeps_the_rest_inside_the_survey(string stray)
    {
        var text =
            "survey a\n" +
            "  centreline\n" +
            "    data normal from to length compass clino\n" +
            "    1 2 10 0 0\n" +
            "  endcentreline\n" +
            "  " + stray + "\n" +
            Shots2 +
            "endsurvey\n";

        var r = new ThParser().Parse("a.th", text);

        var survey = Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        Assert.Equal(2, survey.Children.OfType<CentrelineCommand>().Count());
        Assert.Empty(r.Value.Children.OfType<CentrelineCommand>());
        Assert.DoesNotContain(r.Value.Children.OfType<UnknownCommand>(), u => u.Keyword == "endsurvey");
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.MismatchedBlockTerminator);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == DiagnosticCodes.UnterminatedBlock);
    }

    [Fact]
    public void Stray_terminator_inside_a_centreline_keeps_the_following_rows()
    {
        var text =
            "survey a\n" +
            "  centreline\n" +
            "    data normal from to length compass clino\n" +
            "    1 2 10 0 0\n" +
            "    endgroup\n" +
            "    2 3 5 0 0\n" +
            "  endcentreline\n" +
            "endsurvey\n";

        var r = new ThParser().Parse("a.th", text);

        var survey = Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        var cl = Assert.Single(survey.Children.OfType<CentrelineCommand>());
        Assert.Equal(2, cl.Children.OfType<DataRow>().Count());
        Assert.Single(r.Value.Children); // nothing leaked to top level
    }

    [Fact]
    public void Terminator_of_an_enclosing_block_still_closes_the_inner_one()
    {
        // The centreline's own terminator is missing; `endsurvey` closes it and then the survey.
        var text =
            "survey a\n" +
            "  centreline\n" +
            "    data normal from to length compass clino\n" +
            "    1 2 10 0 0\n" +
            "endsurvey\n" +
            "survey b\n" +
            "endsurvey\n";

        var r = new ThParser().Parse("a.th", text);

        var surveys = r.Value!.Children.OfType<SurveyCommand>().ToList();
        Assert.Equal(new[] { "a", "b" }, surveys.Select(s => s.Name));
        Assert.Single(surveys[0].Children.OfType<CentrelineCommand>());
        Assert.Contains(r.Diagnostics, d => d.Code == DiagnosticCodes.MismatchedBlockTerminator);
    }

    [Fact]
    public void Centerline_spelling_of_an_enclosing_terminator_is_recognised()
    {
        var text =
            "survey a\n" +
            "  centerline\n" +
            "    group\n" +
            "      data normal from to length compass clino\n" +
            "      1 2 10 0 0\n" +
            "  endcenterline\n" +
            "endsurvey\n";

        var r = new ThParser().Parse("a.th", text);

        var survey = Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        Assert.Single(survey.Children.OfType<CentrelineCommand>());
        Assert.Single(r.Value.Children);
    }
}
