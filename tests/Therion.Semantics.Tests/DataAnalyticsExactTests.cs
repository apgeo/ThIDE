// Analytics with exact numbers: which legs count towards a survey's length, the extents of each
// piece, the data-quality thresholds and the order of the team, trip and fixed-point rows. Every
// survey, name and date here is invented.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class DataAnalyticsExactTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            parsed[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    // One plain leg (10 m), one duplicate (5 m), one surface (7 m) and one splay (3 m).
    private const string Flagged = """
        survey s
          centreline
            data normal from to length compass clino
            1 2 10 0 0
            flags duplicate
            2 3 5 90 0
            flags not duplicate
            flags surface
            3 4 7 180 0
            flags not surface
            4 - 3 270 0
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Length_by_survey_counts_shots_but_leaves_splay_duplicate_and_surface_length_out()
    {
        var series = DataAnalytics.LengthBySurvey(Build(("f.th", Flagged)));
        var b = Assert.Single(series);
        Assert.Equal(("s", 10.0, 3), (b.Key, b.Length, b.Shots));
    }

    [Fact]
    public void Direct_length_by_survey_leaves_splay_duplicate_and_surface_length_out()
    {
        var direct = DataAnalytics.DirectLengthBySurvey(Build(("f.th", Flagged)));
        Assert.Equal(new Dictionary<string, double> { ["s"] = 10.0 }, direct);
    }

    [Fact]
    public void Detailed_totals_split_length_by_flag()
    {
        var t = DataAnalytics.ComputeDetailedTotals(Build(("f.th", Flagged)));
        Assert.Equal((3, 1, 1, 1), (t.Shots, t.SplayShots, t.DuplicateShots, t.SurfaceShots));
        Assert.Equal(17.0, t.TotalLength, 9);        // the surface leg is part of the total
        Assert.Equal(10.0, t.UndergroundLength, 9);
        Assert.Equal(7.0, t.SurfaceLength, 9);
        Assert.Equal(5.0, t.DuplicateLength, 9);
        Assert.Equal(3.0, t.SplayLength, 9);
    }

    [Fact]
    public void Length_by_survey_keys_nested_surveys_by_full_name_and_root_shots_by_root()
    {
        var ws = Build(("n.th", """
            centreline
              data normal from to length compass clino
              r1 r2 4 0 0
            endcentreline
            survey cave
              centreline
                data normal from to length compass clino
                1 2 6 0 0
              endcentreline
              survey upper
                centreline
                  data normal from to length compass clino
                  1 2 20 0 0
                  2 3 30 0 0
                endcentreline
              endsurvey
            endsurvey
            """));
        var series = DataAnalytics.LengthBySurvey(ws).Select(b => (b.Key, b.Length, b.Shots)).ToArray();
        Assert.Equal(new[] { ("cave.upper", 50.0, 2), ("cave", 6.0, 1), ("(root)", 4.0, 1) }, series);
        // The direct (non-rolled-up) table has no entry for shots outside every survey.
        var direct = DataAnalytics.DirectLengthBySurvey(ws);
        Assert.Equal(new[] { "cave", "cave.upper" }, direct.Keys.OrderBy(k => k, System.StringComparer.Ordinal));
        Assert.Equal(6.0, direct["cave"], 9);
    }

    [Fact]
    public void Extents_come_from_the_positions_of_each_piece()
    {
        // Piece A: 1 (0,0,0), 2 (0,10,0), 3 (7.071,10,7.071), 4 (7.071,10,-2.929): range 10, NS 10.
        // Piece B: a 20 m eastward leg, flat — the widest east-west.
        var t = DataAnalytics.ComputeDetailedTotals(Build(("e.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                2 3 10 90 45
                3 4 10 0 -90
              endcentreline
            endsurvey
            survey b
              centreline
                data normal from to length compass clino
                1 2 20 90 0
              endcentreline
            endsurvey
            """)));
        Assert.Equal(10.0, t.VerticalRange, 6);
        Assert.Equal("a.3", t.HighestStation);
        Assert.Equal("a.4", t.LowestStation);
        Assert.Equal(20.0, t.EastWestExtent, 6);
        Assert.Equal(10.0, t.NorthSouthExtent, 6);
    }

    [Fact]
    public void Extents_ignore_a_piece_offset_from_another()
    {
        // Two pieces each 5 m tall; positions are piece-local, so the range is 5, not the sum.
        var t = DataAnalytics.ComputeDetailedTotals(Build(("p.th", """
            survey a
              centreline
                data normal from to length compass clino
                1 2 5 0 -90
              endcentreline
            endsurvey
            survey b
              centreline
                data normal from to length compass clino
                1 2 5 0 90
              endcentreline
            endsurvey
            """)));
        Assert.Equal(5.0, t.VerticalRange, 6);
        Assert.Equal(0.0, t.EastWestExtent, 6);
        Assert.Equal(0.0, t.NorthSouthExtent, 6);
    }

    [Fact]
    public void Entrance_and_fixed_counts_come_from_station_declarations()
    {
        var t = DataAnalytics.ComputeDetailedTotals(Build(("c.th", """
            survey s
              centreline
                fix 1 0 0 0
                data normal from to length compass clino
                1 2 5 0 0
                2 3 5 0 0
                station 2 "" entrance
                station 3 "" entrance
              endcentreline
            endsurvey
            """)));
        Assert.Equal(2, t.Entrances);
        Assert.Equal(1, t.FixedPoints);
    }

    // ---- data quality -------------------------------------------------------------------------

    [Fact]
    public void Steep_means_80_up_to_but_not_including_90_degrees_either_way()
    {
        var q = DataAnalytics.DataQuality(Build(("q.th", """
            survey q
              centreline
                data normal from to length compass clino
                1 2 5 0 79.9
                2 3 5 0 80
                3 4 5 0 89.9
                4 5 5 0 90
                5 6 5 0 -85
                6 7 5 0 -90
              endcentreline
            endsurvey
            """)));
        Assert.Equal(3, q.SteepLegs);
        Assert.Equal(6, q.TotalLegs);
    }

    [Theory]
    [InlineData("left right up down", 0)]
    [InlineData("left right ceiling floor", 0)]
    [InlineData("left right up", 1)]
    [InlineData("left up down", 1)]
    [InlineData("right up down", 1)]
    [InlineData("left right down", 1)]
    public void Lrud_counts_only_when_all_four_dimensions_are_read(string lrud, int noLrud)
    {
        int extra = lrud.Split(' ').Length;
        var values = string.Join(" ", Enumerable.Repeat("1", extra));
        var q = DataAnalytics.DataQuality(Build(("l.th",
            $"survey l\n  centreline\n    data normal from to length compass clino {lrud}\n    1 2 5 0 0 {values}\n  endcentreline\nendsurvey\n")));
        Assert.Equal(noLrud, q.NoLrud);
    }

    [Fact]
    public void Missing_readings_zero_length_and_duplicates_are_counted_per_leg()
    {
        var q = DataAnalytics.DataQuality(Build(("m.th", """
            survey m
              centreline
                data normal from to length compass backcompass clino
                1 2 0 0 180 0
                2 3 - 0 180 0
                3 4 5 - - 0
                4 5 5 - - -90
                5 6 5 0 180 -
                flags duplicate
                6 7 5 0 180 0
                7 - 2 0 180 0
              endcentreline
            endsurvey
            """)));
        Assert.Equal(7, q.TotalShots);
        Assert.Equal(6, q.TotalLegs);
        Assert.Equal(1, q.SplayShots);
        Assert.Equal(1, q.ZeroLength);
        Assert.Equal(1, q.MissingLength);
        Assert.Equal(1, q.MissingCompass);     // the plumbed leg has no bearing to read
        Assert.Equal(1, q.MissingClino);
        Assert.Equal(2, q.DuplicateShots);     // the duplicate flag also covers the splay after it
        Assert.Equal(0, q.NoBacksight);
        Assert.Equal(6, q.NoLrud);
    }

    [Fact]
    public void Survey_prefix_takes_the_survey_and_its_children_only()
    {
        var ws = Build(("p.th", """
            survey cave
              centreline
                data normal from to length compass clino
                1 2 5 0 0
              endcentreline
              survey upper
                centreline
                  data normal from to length compass clino
                  1 2 5 0 0
                endcentreline
                survey deep
                  centreline
                    data normal from to length compass clino
                    1 2 5 0 0
                  endcentreline
                endsurvey
              endsurvey
              survey upperx
                centreline
                  date 2024.01.01
                  data normal from to length compass clino
                  1 2 5 0 0
                endcentreline
              endsurvey
            endsurvey
            """));
        var all = DataAnalytics.DataQuality(ws);
        Assert.Equal((4, 3), (all.TotalShots, all.UndatedSurveys));
        var upper = DataAnalytics.DataQuality(ws, "cave.upper");
        Assert.Equal((2, 2, 2), (upper.TotalShots, upper.UndatedSurveys, upper.TeamlessSurveys));
        var empty = DataAnalytics.DataQuality(ws, "");
        Assert.Equal(all, empty);
    }

    // ---- team, trips, dates, fixed points -----------------------------------------------------

    private const string Teams = """
        survey t
          survey a
            centreline
              team "Zoe"
              team "bob"
              team "Bob"
              date 2024.03.02 - 2024.03.04
              data normal from to length compass clino
              1 2 30 0 0
            endcentreline
          endsurvey
          survey b
            centreline
              team "Amy"
              team "Bob"
              date 2024.03.02@10:30
              data normal from to length compass clino
              1 2 10 0 0
            endcentreline
          endsurvey
          survey c
            centreline
              team "Cy"
              date 2023.12.31
              data normal from to length compass clino
              1 2 10 0 0
            endcentreline
          endsurvey
          survey d
            centreline
              data normal from to length compass clino
              1 2 1 0 0
            endcentreline
          endsurvey
        endsurvey
        """;

    [Fact]
    public void Team_members_are_ordered_by_length_then_name_and_counted_once_per_survey()
    {
        var team = DataAnalytics.TeamMembers(Build(("t.th", Teams)))
            .Select(m => (m.Name, m.Surveys, m.Length)).ToArray();
        Assert.Equal(new[]
        {
            ("bob", 2, 40.0),
            ("Zoe", 1, 30.0),
            ("Amy", 1, 10.0),
            ("Cy", 1, 10.0),
        }, team);
    }

    [Fact]
    public void Trips_are_chronological_with_their_surveys_length_and_sorted_members()
    {
        var trips = DataAnalytics.Trips(Build(("t.th", Teams)))
            .Select(e => (e.Date, e.Surveys, e.Length, string.Join("|", e.Members))).ToArray();
        Assert.Equal(new[]
        {
            ("2023.12.31", 1, 10.0, "Cy"),
            ("2024.03.02", 2, 40.0, "Amy|bob|Zoe"),
        }, trips);
    }

    [Fact]
    public void Length_by_date_drops_time_and_interval_tails_and_keeps_undated_surveys()
    {
        var byDate = DataAnalytics.LengthByDate(Build(("t.th", Teams)))
            .Select(b => (b.Key, b.Length, b.Shots)).ToArray();
        // The undated bucket holds survey d and the wrapper survey t (no shots of its own).
        Assert.Equal(new[]
        {
            ("(undated)", 1.0, 2),
            ("2023.12.31", 10.0, 1),
            ("2024.03.02", 40.0, 2),
        }, byDate);
    }

    [Fact]
    public void Fixed_points_list_fixes_first_then_entrances_by_name()
    {
        var rows = DataAnalytics.FixedPoints(Build(("x.th", """
            survey s
              centreline
                fix 9 10 20 30
                fix 3 1 2 3
                data normal from to length compass clino
                1 2 5 0 0
                2 3 5 0 0
                3 9 5 0 0
                station 2 "" entrance
                station 1 "" entrance
                station 3 "" entrance
              endcentreline
            endsurvey
            """)));
        Assert.Equal(new[]
        {
            ("s.3", true, true),
            ("s.9", true, false),
            ("s.1", false, true),
            ("s.2", false, true),
        }, rows.Select(r => (r.Station, r.IsFixed, r.IsEntrance)).ToArray());
        Assert.Equal((10.0, 20.0, 30.0), (rows[1].X!.Value, rows[1].Y!.Value, rows[1].Z!.Value));
        Assert.Null(rows[2].X);
    }
}
