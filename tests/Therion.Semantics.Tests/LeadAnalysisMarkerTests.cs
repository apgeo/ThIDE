// Leads register: which station comments are leads, how `not` toggles fold, the description and
// order of the leads, and a dead end covered by a flag on an equated alias. Every survey is invented.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class LeadAnalysisMarkerTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>();
        foreach (var (path, text) in files) parsed[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>());
    }

    // A loop 1-2-3-1 (no dead ends) with station 2 carrying the given comment.
    private static Lead[] CommentLeads(string comment) =>
        LeadAnalysis.Analyze(Build(("/p/c.th", $$"""
            survey c
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 120 0
                  3 1 10 240 0
                station 2 "{{comment}}"
              endcentreline
            endsurvey
            """))).ToArray();

    [Theory]
    [InlineData("QM side passage")]
    [InlineData("qm12: low crawl")]
    [InlineData("lead to the north")]
    [InlineData("two leads here")]
    [InlineData("passage continues")]
    [InlineData("dig")]
    [InlineData("worth a dig, draughting")]
    [InlineData("where does this go?")]
    public void Marked_comment_is_a_comment_lead(string comment)
    {
        var lead = Assert.Single(CommentLeads(comment));
        Assert.Equal(("c.2", LeadKind.CommentMarker, comment), (lead.Location, lead.Kind, lead.Description));
        Assert.Equal("comment", lead.KindLabel);
        Assert.False(lead.IsStationFlag);
    }

    [Theory]
    [InlineData("big chamber")]
    [InlineData("digital disto used")]
    [InlineData("leader of the party")]
    [InlineData("misleading survey mark")]
    [InlineData("equal spacing")]
    public void Unmarked_comment_is_not_a_lead(string comment)
    {
        Assert.Empty(CommentLeads(comment));
    }

    [Fact]
    public void Not_negates_only_the_next_flag()
    {
        // `not dig continuation`: dig is cleared, continuation is set.
        var leads = LeadAnalysis.Analyze(Build(("/p/c.th", """
            survey c
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 120 0
                  3 1 10 240 0
                station 2 "" not dig continuation
              endcentreline
            endsurvey
            """)));
        var lead = Assert.Single(leads);
        Assert.Equal((LeadKind.ContinuationFlag, "continuation"), (lead.Kind, lead.FlagLabel));
    }

    [Fact]
    public void Flag_lead_is_described_by_its_comment_or_else_its_flags()
    {
        var leads = LeadAnalysis.Analyze(Build(("/p/c.th", """
            survey c
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 120 0
                  3 1 10 240 0
                station 1 "squeeze at the end" continuation
                station 2 "  " dig air-draught
                station 3 "" air-draught:summer continuation
              endcentreline
            endsurvey
            """)));
        Assert.Equal(new[]
        {
            ("c.1", LeadKind.ContinuationFlag, "squeeze at the end", "continuation"),
            ("c.3", LeadKind.ContinuationFlag, "continuation, air-draught", "continuation, air-draught"),
            ("c.2", LeadKind.StationFlag, "dig, air-draught", "dig, air-draught"),
        }, leads.Select(l => (l.Location, l.Kind, l.Description, l.KindLabel)).ToArray());
    }

    [Fact]
    public void Leads_are_ordered_by_kind_then_location()
    {
        // Chain 1-2-3-4-5 with a branch 3-6: dead ends 1, 5 and 6.
        var leads = LeadAnalysis.Analyze(Build(("/p/c.th", """
            survey c
              centreline
                data normal from to length compass clino
                  1 2 10 0 0
                  2 3 10 0 0
                  3 4 10 0 0
                  4 5 10 0 0
                  3 6 10 90 0
                station 4 "QM up the aven"
                station 6 "" dig
                station 5 "" continuation
              endcentreline
            endsurvey
            """)));
        Assert.Equal(new[]
        {
            ("c.5", LeadKind.ContinuationFlag),
            ("c.4", LeadKind.CommentMarker),
            ("c.1", LeadKind.DeadEnd),
            ("c.6", LeadKind.StationFlag),
        }, leads.Select(l => (l.Location, l.Kind)).ToArray());
        Assert.Equal("leaf node (no continuation flag)", leads[2].Description);
        Assert.Equal("dead-end (unmarked)", leads[2].KindLabel);
    }

    [Fact]
    public void Flag_on_an_equated_alias_covers_the_dead_end()
    {
        // a.3 is a dead end; the continuation flag sits on b.1, which is equated to it.
        var leads = LeadAnalysis.Analyze(Build(
            ("/p/a.th", """
                survey a
                  centreline
                    data normal from to length compass clino
                      1 2 10 0 0
                      2 3 10 0 0
                  endcentreline
                endsurvey
                """),
            ("/p/b.th", """
                survey b
                  centreline
                    fix 1 0 0 0
                    station 1 "" continuation
                  endcentreline
                endsurvey
                """),
            ("/p/link.th", "equate 3@a 1@b\n")));
        Assert.Equal(new[]
        {
            ("b.1", LeadKind.ContinuationFlag),
            ("a.1", LeadKind.DeadEnd),
        }, leads.Select(l => (l.Location, l.Kind)).ToArray());
    }
}
