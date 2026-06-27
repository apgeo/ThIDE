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
    public void Large_surface_body_is_collapsed_to_few_tokens()
    {
        // A DEM surface body has one number per line and can be tens of MB; the lexer must collapse
        // it instead of emitting a token per number (which previously OOM'd on the corpus DEM files).
        var sb = new System.Text.StringBuilder();
        sb.Append("survey s\n  surface\n    grid 0 0 1 1 1000 1000\n");
        for (int i = 0; i < 5000; i++) sb.Append("    ").Append(i).Append(".0\n");
        sb.Append("  endsurface\nendsurvey\n");

        var tokens = new TherionTokenizer().Tokenize("dem.th", sb.ToString());
        // Far fewer than the ~5000 numbers: header + one opaque body token + endsurface + endsurvey.
        Assert.True(tokens.Length < 60, $"expected the body collapsed, got {tokens.Length} tokens");

        var r = new ThParser().Parse("dem.th", sb.ToString());
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(r.Value!.Children.OfType<SurveyCommand>().Single()
            .Children.OfType<SurfaceCommand>().Single().IsTerminated);
    }

    [Fact]
    public void Surface_without_endsurface_is_not_collapsed()
    {
        // No matching endsurface ⇒ no collapse (so a stray `surface` can't swallow a whole file).
        var tokens = new TherionTokenizer().Tokenize("x.th", "surface\n1.0\n2.0\n3.0\n");
        Assert.Contains(tokens, t => t.Kind == TherionTokenKind.Number);
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
