// The workspace equate graph must keep an equate whose bare member was resolved in its own survey
// scope, even when that member's name is reused by sibling surveys in the same file.

using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class WorkspaceEquateScopeTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = files.ToDictionary(f => f.Path, f => new ThParser().Parse(f.Path, f.Text));
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    [Fact]
    public void A_bare_equate_member_reused_in_sibling_surveys_still_joins_the_parent_series()
    {
        var ws = Build(("/p/cave.th", """
            survey cave
              survey t1
                centreline
                  data normal from to length compass clino
                  0 1 5 0 0
                endcentreline
              endsurvey
              survey t2
                centreline
                  data normal from to length compass clino
                  0 1 6 90 0
                endcentreline
              endsurvey
              centreline
                data normal from to length compass clino
                0 1 12 0 0
              endcentreline
              equate 0 0@t1
              equate 1 0@t2
            endsurvey
            """));

        var g = WorkspaceEquates.Build(ws);

        Assert.True(
            g.Find(QualifiedName.Of("cave", "0")).Equals(g.Find(QualifiedName.Of("cave", "t1", "0"))),
            "`equate 0 0@t1` did not join cave.0 and cave.t1.0 in the workspace equate graph");
    }
}
