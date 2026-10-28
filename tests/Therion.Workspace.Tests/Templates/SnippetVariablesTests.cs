using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

public class SnippetVariablesTests
{
    private const string Text =
        "survey cave\n" +
        "  survey north\n" +
        "    centreline\n" +
        "      date 2026.10.01\n" +
        "      team \"Ana\" instruments\n" +
        "      team \"Bo\" notes\n" +
        "      units length meters\n" +
        "      data normal from to length compass clino\n" +
        "      A07 A08 5 10 0\n" +
        "      A08 - 2 90 0\n" +
        "    endcentreline\n" +
        "    # CARET\n" +
        "  endsurvey\n" +
        "endsurvey\n";

    private static Func<string, string?> At(string marker = "# CARET") =>
        SnippetVariables.Standard(new SnippetContext("/caves/north.th", Text, Text.IndexOf(marker), new DateTime(2026, 10, 28, 9, 5, 0)));

    [Fact]
    public void Clock_and_file_variables()
    {
        var v = At();

        Assert.Equal("2026.10.28", v("DATE"));
        Assert.Equal("09:05", v("TIME"));
        Assert.Equal("2026", v("YEAR"));
        Assert.Equal("north.th", v("FILE_NAME"));
        Assert.Equal("north", v("FILE_BASENAME"));
    }

    [Fact]
    public void Survey_variables_follow_the_caret()
    {
        var v = At();

        Assert.Equal("north", v("CURRENT_SURVEY"));
        Assert.Equal("cave", v("PARENT_SURVEY"));
    }

    [Fact]
    public void Last_commands_and_team_before_the_caret()
    {
        var v = At();

        Assert.Equal("units length meters", v("LAST_UNITS"));
        Assert.Equal("data normal from to length compass clino", v("LAST_DATA"));
        Assert.Equal("team \"Ana\" instruments\nteam \"Bo\" notes", v("TEAM_RECENT"));
        Assert.Null(v("LAST_CALIBRATE"));
    }

    [Fact]
    public void Last_and_next_station_skip_splays_and_keep_padding()
    {
        var v = At();

        Assert.Equal("A08", v("LAST_STATION"));
        Assert.Equal("A09", v("NEXT_STATION"));
    }

    [Theory]
    [InlineData("7", "8")]
    [InlineData("A07", "A08")]
    [InlineData("1.9", "1.10")]
    [InlineData("099", "100")]
    [InlineData("entrance", null)]
    public void Next_station(string station, string? expected)
    {
        Assert.Equal(expected, SnippetVariables.NextStation(station));
    }

    [Fact]
    public void Nothing_before_the_caret_means_null()
    {
        var v = At("survey cave");

        Assert.Null(v("LAST_STATION"));
        Assert.Null(v("CURRENT_SURVEY"));
    }
}
