// Parser gaps found by the real-world corpus validation (see docs/corpus-validation-report.md):
//   * `scan … endscan`   — a survey-level block (attaches 3D scan files). Previously its `endscan`
//                          collided with the enclosing survey → TH0021 error.
//   * `grade … endgrade` — a grade DEFINITION block (vs. the single-line grade reference).
//   * `import <file.3d>`  — Survex/Compass import; must not be flagged as an unknown command.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class BlockGapTests
{
    private static bool HasError(ParseResult<TherionFile> r) =>
        r.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    private static IEnumerable<TherionNode> Descend(ImmutableArray<TherionNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n is BlockCommand b)
                foreach (var c in Descend(b.Children)) yield return c;
        }
    }

    [Fact]
    public void Scan_endscan_block_inside_survey_parses_without_error()
    {
        // Shape from therion's own samples/scan/scan.th.
        var r = new ThParser().Parse("/p/scan.th", """
            survey vysoky
              centerline
                0 1 5.0 90 0
              endcenterline
              scan
                file scan.stl
              endscan
            endsurvey
            """);

        Assert.False(HasError(r));
        var scan = Descend(r.Value!.Children).OfType<ScanCommand>().Single();
        Assert.True(scan.IsTerminated);
        // The survey must still close cleanly and not be swallowed by the scan block.
        Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
    }

    [Fact]
    public void Grade_definition_block_parses_as_terminated_block()
    {
        // Shape from therion-librarydata/grades.th.
        var r = new ThParser().Parse("/p/grades.th", """
            grade BCRA3 -title "BCRA grade 3"
              sd length 0.05 metres
              sd compass 2.5 degrees
            endgrade
            """);

        Assert.False(HasError(r));
        var grade = r.Value!.Children.OfType<GradeCommand>().Single();
        Assert.True(grade.IsBlockDefinition);
        // `endgrade` must be consumed, not left as an unknown command.
        Assert.DoesNotContain(r.Value!.Children.OfType<UnknownCommand>(),
            c => c.Keyword.Equals("endgrade", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Grade_reference_stays_a_single_line_command()
    {
        // A `grade` reference whose next terminator is NOT endgrade must not become a block.
        var r = new ThParser().Parse("/p/ref.th", """
            grade BCRA5
            survey s
            endsurvey
            """);

        Assert.False(HasError(r));
        var grade = r.Value!.Children.OfType<GradeCommand>().Single();
        Assert.False(grade.IsBlockDefinition);
        Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
    }

    [Fact]
    public void Import_command_is_not_flagged_unknown()
    {
        var r = new ThParser().Parse("/p/i.th", "import cave.3d -surveys use\n");
        Assert.DoesNotContain(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnknownCommand);
    }

    [Fact]
    public void Comment_block_in_th_is_consumed_and_following_survey_survives()
    {
        // The body may contain prose that *starts* with an end-keyword; it must NOT terminate
        // anything — only `endcomment` closes the block.
        var r = new ThParser().Parse("/p/c.th", """
            comment
            The survey below is complex.
            endsurvey here is just prose, not a real terminator.
            endcomment
            survey s
            endsurvey
            """);

        Assert.False(HasError(r));
        Assert.Single(r.Value!.Children.OfType<SurveyCommand>());
        Assert.Contains(r.Value!.Children, n => n is TrivialComment);
        // The prose 'endsurvey' inside the comment must not leak in as a command/terminator.
        Assert.DoesNotContain(r.Value!.Children.OfType<UnknownCommand>(),
            c => c.Keyword.StartsWith("end", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Comment_block_inside_th2_scrap_is_consumed()
    {
        // Shape from Mapiah's comment_block_complex fixture.
        var r = new Th2Parser().Parse("/p/s.th2", """
            comment
            The scrap below is really complex.
            endcomment
            scrap poco -scale [0 0 1 0 0 0 1 0 m]
            comment
            Another comment block, inside a scrap!
            endcomment
            point 0 0 station
            endscrap
            """);

        Assert.False(HasError(r));
        Assert.Single(r.Value!.Children.OfType<ScrapBlock>());
    }
}
