using Therion.Workspace.Fixes;
using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests;

// A regular expression's digit class matches Arabic-Indic and Persian digits too, which number parsing does
// not accept: a station named in those digits must be left alone, not make the call throw.
public class NonAsciiDigitStationTests
{
    [Theory]
    [InlineData("١٢")]            // Arabic-Indic digits
    [InlineData("۱۲")]            // Persian digits
    [InlineData("A٣")]
    [InlineData("１２")]            // full-width digits
    public void A_renumbering_with_an_offset_leaves_a_station_in_non_ascii_digits_alone(string name)
    {
        string? renamed = null;
        var error = Record.Exception(() => renamed = new RenumberRule("", "", 5).Apply(name));

        Assert.Null(error);
        Assert.Null(renamed);
    }

    [Theory]
    [InlineData("١٢")]
    [InlineData("S۹")]
    [InlineData("1.٥")]
    public void The_next_station_after_non_ascii_digits_is_unknown_not_an_error(string name)
    {
        string? next = "unset";
        var error = Record.Exception(() => next = SnippetVariables.NextStation(name));

        Assert.Null(error);
        Assert.Null(next);
    }

    [Fact]
    public void Ascii_digits_still_count()
    {
        Assert.Equal("17", new RenumberRule("", "", 5).Apply("12"));
        Assert.Equal("A08", SnippetVariables.NextStation("A07"));
        Assert.Equal("1.10", SnippetVariables.NextStation("1.9"));
        Assert.Equal("p١٢", new RenumberRule("x", "p").Apply("x١٢"));   // no offset: the digits are just text
    }
}
