// DATA-01/02/05/06/08 — analytics over a small workspace built from .th source.
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class DataAnalyticsTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            parsed[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    private const string Cave = """
        survey cave -title "Test"
          team "Alice Smith"
          team "Bob Jones"
          date 2024.07.01
          cs UTM33
          fix 0 350000 5000000 1200
          centreline
            data normal from to length compass clino left right up down
            0 1 10 0 0 1 1 2 2
            1 2 10 90 45 1 1 2 2
            2 3 10 0 -90 - - - -
          endcentreline
        endsurvey
        """;

    [Fact]
    public void DetailedTotals_breaks_down_length_and_extents()
    {
        var t = DataAnalytics.ComputeDetailedTotals(Build(("cave.th", Cave)));
        Assert.Equal(1, t.Surveys);
        Assert.Equal(3, t.Shots);                       // 3 legs (none splay here)
        Assert.Equal(30, t.TotalLength, 3);
        Assert.Equal(30, t.UndergroundLength, 3);
        Assert.True(t.VerticalRange > 0);               // the -90 leg drops 10 m
        Assert.NotNull(t.HighestStation);
        Assert.NotNull(t.LowestStation);
        Assert.Equal(1, t.FixedPoints);
    }

    [Fact]
    public void FixedPoints_carry_coordinates_and_cs()
    {
        var rows = DataAnalytics.FixedPoints(Build(("cave.th", Cave)));
        var fix = Assert.Single(rows, r => r.IsFixed);
        Assert.Equal("UTM33", fix.Cs);
        Assert.Equal(350000, fix.X!.Value, 3);
        Assert.Equal(1200, fix.Z!.Value, 3);
    }

    [Fact]
    public void TeamMembers_aggregate_across_surveys()
    {
        var team = DataAnalytics.TeamMembers(Build(("cave.th", Cave)));
        Assert.Contains(team, m => m.Name == "Alice Smith" && m.Surveys == 1);
        Assert.Contains(team, m => m.Name == "Bob Jones");
    }

    [Fact]
    public void Expeditions_group_by_date()
    {
        var exp = DataAnalytics.Expeditions(Build(("cave.th", Cave)));
        var e = Assert.Single(exp);
        Assert.Equal("2024.07.01", e.Date);
        Assert.Equal(2, e.Members.Length);
    }

    [Fact]
    public void DataQuality_counts_lrud_and_backsight_absence()
    {
        var q = DataAnalytics.DataQuality(Build(("cave.th", Cave)));
        Assert.Equal(3, q.TotalShots);
        Assert.Equal(0, q.NoLrud);          // the style has left/right/up/down
        Assert.Equal(3, q.NoBacksight);     // no back* readings in the style
        Assert.Equal(0, q.UndatedSurveys);
        Assert.Equal(0, q.TeamlessSurveys);
    }

    [Fact]
    public void DataQuality_flags_undated_and_teamless()
    {
        var q = DataAnalytics.DataQuality(Build(("b.th", """
            survey bare
              centreline
                data normal from to length compass clino
                0 1 5 10 0
              endcentreline
            endsurvey
            """)));
        Assert.Equal(1, q.UndatedSurveys);
        Assert.Equal(1, q.TeamlessSurveys);
        Assert.Equal(1, q.NoLrud);
    }

    [Fact]
    public void LengthBySurvey_orders_longest_first()
    {
        var ws = Build(("m.th", """
            survey big
              centreline
                data normal from to length compass clino
                0 1 100 0 0
              endcentreline
            endsurvey
            survey small
              centreline
                data normal from to length compass clino
                0 1 5 0 0
              endcentreline
            endsurvey
            """));
        var series = DataAnalytics.LengthBySurvey(ws);
        Assert.Equal("big", series[0].Key);
        Assert.True(series[0].Length >= series[^1].Length);
    }
}
