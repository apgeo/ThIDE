// `point@inner.outer` names one station; the outer part of the path must disambiguate two
// sub-surveys that share a name under different parents.

using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class NestedSurveyPathResolutionTests
{
    private const string TwoCaves = """
        survey c1
          survey ent
            centreline
              data normal from to length compass clino
              1 2 8 0 0
            endcentreline
          endsurvey
        endsurvey
        survey c2
          survey ent
            centreline
              data normal from to length compass clino
              1 2 6 0 0
            endcentreline
          endsurvey
        endsurvey
        equate 1@ent.c1 1@ent.c2
        """;

    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = files.ToDictionary(f => f.Path, f => new ThParser().Parse(f.Path, f.Text));
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    [Fact]
    public void The_outer_survey_of_an_at_path_selects_between_same_named_sub_surveys()
    {
        var ws = Build(("/p/caves.th", TwoCaves));

        Assert.Equal("c1.ent.1", ws.ResolveStationSymbol("1@ent.c1")?.Name.ToString());
        Assert.Equal("c2.ent.1", ws.ResolveStationSymbol("1@ent.c2")?.Name.ToString());
    }

    [Fact]
    public void An_equate_between_same_named_sub_surveys_of_two_caves_joins_them()
    {
        var ws = Build(("/p/caves.th", TwoCaves));

        var g = WorkspaceEquates.Build(ws);

        Assert.True(
            g.Find(QualifiedName.Of("c1", "ent", "1")).Equals(g.Find(QualifiedName.Of("c2", "ent", "1"))),
            "`equate 1@ent.c1 1@ent.c2` did not join c1.ent.1 and c2.ent.1");
    }
}
