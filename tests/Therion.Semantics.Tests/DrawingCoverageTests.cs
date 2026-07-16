// U-04 drawing coverage: ProjectStatistics.UndrawnSurveys — centreline that nothing has drawn yet,
// the inverse of UnreferencedScraps. Built from in-memory parse results, mirroring
// AggregationNavigationTests.

using System.Collections.Generic;
using System.Linq;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class DrawingCoverageTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>();
        foreach (var (path, text) in files)
            parsed[path] = path.EndsWith(".th2")
                ? new Th2Parser().Parse(path, text)
                : (ParseResult<TherionFile>)new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    private const string TwoSurveys = """
        survey upper
          centreline
            data normal from to length compass clino
            1 2 10 90 0
          endcentreline
        endsurvey
        survey lower
          centreline
            data normal from to length compass clino
            1 2 12 180 0
          endcentreline
        endsurvey
        """;

    [Fact]
    public void A_survey_no_th2_draws_is_reported_and_a_drawn_one_is_not()
    {
        // plan.th2 draws upper's stations only.
        var ws = Build(
            ("/p/m.th", TwoSurveys),
            ("/p/plan.th2", """
                encoding utf-8
                scrap upper-plan -projection plan
                  point 10 20 station -name 1@upper
                  point 30 40 station -name 2@upper
                endscrap
                """));

        var undrawn = ProjectStatistics.UndrawnSurveys(ws);

        Assert.Equal(["lower"], undrawn);
    }

    [Fact]
    public void Every_survey_is_undrawn_when_the_project_has_no_drawings_at_all()
    {
        var ws = Build(("/p/m.th", TwoSurveys));

        var undrawn = ProjectStatistics.UndrawnSurveys(ws);

        Assert.Equal(["lower", "upper"], undrawn);
    }

    [Fact]
    public void A_survey_without_shots_is_not_reported_as_undrawn()
    {
        // Nothing to draw: no shots, so it is not outstanding work.
        var ws = Build(("/p/m.th", """
            survey empty
            endsurvey
            """));

        Assert.Empty(ProjectStatistics.UndrawnSurveys(ws));
    }

    [Fact]
    public void Splays_alone_do_not_make_a_survey_worth_drawing()
    {
        var ws = Build(("/p/m.th", """
            survey splayed
              centreline
                data normal from to length compass clino
                flags splay
                1 - 2.0 90 0
                1 - 2.0 180 0
              endcentreline
            endsurvey
            """));

        Assert.Empty(ProjectStatistics.UndrawnSurveys(ws));
    }

    [Fact]
    public void Only_the_survey_that_owns_the_shots_is_reported_not_its_parent()
    {
        // A parent that owns no shots has nothing of its own to draw; the child is where the work is.
        var ws = Build(("/p/m.th", """
            survey cave
              survey branch
                centreline
                  data normal from to length compass clino
                  1 2 10 90 0
                endcentreline
              endsurvey
            endsurvey
            """));

        var undrawn = ProjectStatistics.UndrawnSurveys(ws);

        Assert.Equal(["cave.branch"], undrawn);
    }

    [Fact]
    public void An_empty_project_reports_nothing()
    {
        Assert.Empty(ProjectStatistics.UndrawnSurveys(WorkspaceSemanticModel.Empty));
    }

    [Fact]
    public void Unreferenced_scraps_and_undrawn_surveys_are_opposite_halves_of_the_same_question()
    {
        // upper is drawn by a scrap, but no map composes that scrap: one is undrawn, the other unused.
        var ws = Build(
            ("/p/m.th", TwoSurveys),
            ("/p/plan.th2", """
                encoding utf-8
                scrap upper-plan -projection plan
                  point 10 20 station -name 1@upper
                endscrap
                """));

        Assert.Equal(["lower"], ProjectStatistics.UndrawnSurveys(ws));
        Assert.Equal(["upper-plan"], ProjectStatistics.UnreferencedScraps(ws));
    }
}
