using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

// LEAD-01 / LEAD-05 — the exploration-leads register.
public class LeadAnalysisTests
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
    public void Finds_continuation_flag_comment_and_deadend_leads()
    {
        // 1—2—3 chain: 3 is flagged continuation, 2 carries a QM comment, 1 is an unmarked leaf.
        const string src = """
            survey cave
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 0 0
                station 3 "ongoing" continuation
                station 2 "QM side passage"
              endcentreline
            endsurvey
            """;

        var leads = LeadAnalysis.Analyze(Build(src));

        Assert.Contains(leads, l => l.Kind == LeadKind.ContinuationFlag);
        Assert.Contains(leads, l => l.Kind == LeadKind.CommentMarker);
        Assert.Contains(leads, l => l.Kind == LeadKind.DeadEnd);

        // The flagged continuation station is not ALSO reported as an unmarked dead-end.
        var contStation = leads.First(l => l.Kind == LeadKind.ContinuationFlag).Location;
        Assert.DoesNotContain(leads, l => l.Kind == LeadKind.DeadEnd && l.Location == contStation);
    }

    [Fact]
    public void No_leads_for_a_fully_closed_loop()
    {
        // A triangle 1-2-3-1: every node has degree 2, none flagged → no dead-ends, no leads.
        const string src = """
            survey loop
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 0 0
                  3 1 10 0 0
              endcentreline
            endsurvey
            """;

        var leads = LeadAnalysis.Analyze(Build(src));
        Assert.Empty(leads);
    }

    [Fact]
    public void Null_workspace_yields_no_leads()
        => Assert.Empty(LeadAnalysis.Analyze(null));
}
