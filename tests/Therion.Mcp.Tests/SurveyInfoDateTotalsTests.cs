using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

/// <summary>
/// list_survey_info is documented as the answer to "how much was surveyed in year X". The totals it
/// hands back must then describe only the length surveyed inside the range, not whatever happens to sit
/// under a survey that matched.
/// </summary>
public class SurveyInfoDateTotalsTests
{
    // cave has its own 50 m centreline from 2001; ext, nested inside it, adds 70 m in 2010.
    private const string ParentAndLaterChild = """
        survey cave
          date 2001.07.10
          centreline
            data normal from to length compass clino
            1 2 50 0 0
          endcentreline
          survey ext
            date 2010.03.01
            centreline
              data normal from to length compass clino
              1 2 70 0 0
            endcentreline
          endsurvey
        endsurvey
        """;

    // One survey, two trips nine years apart, nothing in between.
    private const string TwoTripsYearsApart = """
        survey cave
          date 2001.07.10
          date 2010.03.01
          centreline
            data normal from to length compass clino
            1 2 50 0 0
          endcentreline
        endsurvey
        """;

    [Fact]
    public async Task Year_total_excludes_a_nested_survey_dated_outside_the_range()
    {
        using var fixture = Dated(ParentAndLaterChild);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListSurveyInfo(dateFrom: "2001", dateTo: "2001");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal(["cave"], result.Data!.Surveys.Select(s => s.Name));
        Assert.Equal(50, result.Data.TotalLength);   // the 70 m of 2010 were not surveyed in 2001
    }

    [Fact]
    public async Task A_survey_with_two_trips_does_not_match_a_year_between_them()
    {
        using var fixture = Dated(TwoTripsYearsApart);
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.ListSurveyInfo(dateFrom: "2005", dateTo: "2005");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.DoesNotContain("cave", result.Data!.Surveys.Select(s => s.Name));
    }

    private static FixtureWorkspace Dated(string survey)
    {
        var fixture = FixtureWorkspace.Create();
        File.WriteAllText(fixture.Thconfig, "source caves/dated.th\n");
        File.WriteAllText(fixture.PathTo("caves", "dated.th"), survey);
        return fixture;
    }

    private static async Task<GraphTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new GraphTools(host);
    }
}
