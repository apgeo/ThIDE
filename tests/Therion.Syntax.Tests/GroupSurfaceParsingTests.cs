// Therion `group ... endgroup` and `surface ... endsurface` blocks (used by the
// migresurvey corpus: agartha.th and the DEM s404124.th). Both are valid syntax.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class GroupSurfaceParsingTests
{
    [Fact]
    public void Surface_block_is_consumed_opaquely_without_errors()
    {
        var r = new ThParser().Parse("/p/dem.th", """
            survey s
              surface
                grid 0 0 1 1 2 2
                1.0
                2.0
                3.0
                4.0
              endsurface
            endsurvey
            """);

        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var surface = r.Value!.Children.OfType<SurveyCommand>().Single()
            .Children.OfType<SurfaceCommand>().Single();
        Assert.True(surface.IsTerminated);
    }

    [Fact]
    public void Group_inside_centreline_parses_its_shots_as_data_rows()
    {
        var r = new ThParser().Parse("/p/a.th", """
            survey s
              centreline
                group
                  data normal from to length compass clino
                  1 2 5.0 010 0
                  2 3 6.0 020 0
                endgroup
              endcentreline
            endsurvey
            """);

        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var group = r.Value!.Children.OfType<SurveyCommand>().Single()
            .Children.OfType<CentrelineCommand>().Single()
            .Children.OfType<GroupCommand>().Single();
        Assert.True(group.IsTerminated);
        Assert.Equal(2, group.Children.OfType<DataRow>().Count());
    }

    [Fact]
    public void Group_at_survey_level_parses_without_errors()
    {
        var r = new ThParser().Parse("/p/g.th", """
            survey s
              group
                team "Alice"
              endgroup
            endsurvey
            """);

        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Single(r.Value!.Children.OfType<SurveyCommand>().Single().Children.OfType<GroupCommand>());
    }
}
