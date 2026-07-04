using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

// the exploration-leads register.
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
    public void Dig_and_air_draught_flags_become_station_flag_leads_with_the_flag_in_the_label()
    {
        const string src = """
            survey cave
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 0 0
                station 2 "" dig
                station 3 "" air-draught:winter
              endcentreline
            endsurvey
            """;

        var leads = LeadAnalysis.Analyze(Build(src));

        Assert.Contains(leads, l => l.Kind == LeadKind.StationFlag && l.FlagLabel == "dig" && l.IsStationFlag);
        // The :winter qualifier collapses to the "air-draught" head for display.
        Assert.Contains(leads, l => l.Kind == LeadKind.StationFlag && l.FlagLabel == "air-draught");
    }

    [Fact]
    public void Not_prefix_cancels_a_flag_so_it_is_not_a_lead()
    {
        // `not continuation` removes the flag → station 2 must not surface as a flag lead.
        const string src = """
            survey cave
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 0 0
                station 2 "" continuation
                station 2 "" not continuation
              endcentreline
            endsurvey
            """;

        var leads = LeadAnalysis.Analyze(Build(src));

        Assert.DoesNotContain(leads, l => l.IsStationFlag && l.Location == "2");
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
