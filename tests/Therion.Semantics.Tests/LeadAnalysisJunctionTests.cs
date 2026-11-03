// A station that joins two surveys through an `@`-equate is an interior point, not a dead-end lead.

using System.Collections.Generic;
using System.Linq;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class LeadAnalysisJunctionTests
{
    private static WorkspaceSemanticModel Build(string src, string path = "cave.th")
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            [path] = new ThParser().Parse(path, src),
        };
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    [Fact]
    public void At_equated_junction_is_not_a_dead_end()
    {
        // a.1 — a.2 == b.1 — b.2 : only a.1 and b.2 are leaves.
        const string src = """
            survey cave
              survey a
                centreline
                  data normal from to length compass clino
                    1 2 10 0 0
                endcentreline
              endsurvey
              survey b
                centreline
                  data normal from to length compass clino
                    1 2 10 0 0
                endcentreline
              endsurvey
              equate 2@a 1@b
            endsurvey
            """;

        var leads = LeadAnalysis.Analyze(Build(src));
        var deadEnds = leads.Where(l => l.Kind == LeadKind.DeadEnd).Select(l => l.Location).ToArray();

        Assert.Contains("cave.a.1", deadEnds);
        Assert.Contains("cave.b.2", deadEnds);
        Assert.DoesNotContain(deadEnds, s => s is "cave.a.2" or "cave.b.1");
    }
}
