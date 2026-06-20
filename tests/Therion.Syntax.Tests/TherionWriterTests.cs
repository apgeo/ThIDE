// M6 — ITherionWriter round-trip smoke tests.

using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class TherionWriterTests
{
    [Fact]
    public void Roundtrips_survey_with_centreline()
    {
        const string src = """
            survey upper -title "Upper"
              centreline
                data normal from to length compass clino
                  1 2 10.0 90 0
              endcentreline
            endsurvey
            """;
        var parse = new ThParser().Parse("a.th", src);
        var written = new TherionWriter().Write(parse.Value!);
        var reparse = new ThParser().Parse("a.th", written);

        Assert.NotNull(reparse.Value);
        Assert.DoesNotContain(reparse.Diagnostics,
            d => d.Severity == Core.DiagnosticSeverity.Error);
        Assert.Single(reparse.Value!.Children.OfType<SurveyCommand>());
    }

    [Fact]
    public void Roundtrips_th2_scrap_with_line()
    {
        const string src = """
            scrap s1
              line wall
                0 0
                1 1
              endline
            endscrap
            """;
        var parse = new Th2Parser().Parse("a.th2", src);
        var written = new TherionWriter().Write(parse.Value!);
        var reparse = new Th2Parser().Parse("a.th2", written);

        var scrap = Assert.Single(reparse.Value!.Children.OfType<ScrapBlock>());
        var line = Assert.Single(scrap.Children.OfType<LineObject>());
        Assert.Equal(2, line.Vertices.Length);
    }
}
