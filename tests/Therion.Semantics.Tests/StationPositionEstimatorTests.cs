// Dead-reckoning station positions (solver A): geometry fixtures with hand-computable truth.
// X=east, Y=north, Z=up, metres; depth positive going down from the datum.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class StationPositionEstimatorTests
{
    private static WorkspaceSemanticModel Ws(params (string Path, string Text)[] files)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            dict[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private static PositionSet Positions(string source) =>
        StationPositionEstimator.Estimate(Ws(("/c.th", source)));

    private static StationPosition At(PositionSet set, string name)
    {
        var p = set.For(QualifiedName.Parse(name));
        Assert.NotNull(p);
        return p!;
    }

    [Fact]
    public void Straight_shaft_measures_depth_down_from_the_entrance()
    {
        // Two 10 m plumbs down from an entrance at the top: depths 0, 10, 20.
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 -90
                2 3 10 0 -90
                station 1 "" entrance
              endcentreline
            endsurvey
            """);

        Assert.Equal(0.0, At(set, "s.1").Depth, 6);
        Assert.Equal(10.0, At(set, "s.2").Depth, 6);
        Assert.Equal(20.0, At(set, "s.3").Depth, 6);
        // Z is up, so descending stations have decreasing Z.
        Assert.Equal(-20.0, At(set, "s.3").Z, 6);
        Assert.True(At(set, "s.3").VerticalReliable);
        Assert.True(At(set, "s.3").HorizontalReliable);
    }

    [Fact]
    public void L_traverse_places_east_and_north_exactly()
    {
        // North 10, then east 10 — both horizontal.
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                2 3 10 90 0
              endcentreline
            endsurvey
            """);

        Assert.Equal(0.0, At(set, "s.1").Y, 6);
        Assert.Equal(10.0, At(set, "s.2").Y, 6);   // north
        Assert.Equal(0.0, At(set, "s.2").X, 6);
        Assert.Equal(10.0, At(set, "s.3").X, 6);   // east
        Assert.Equal(10.0, At(set, "s.3").Y, 6);
        Assert.Equal(0.0, At(set, "s.3").Z, 6);
        Assert.Equal(0.0, At(set, "s.3").Depth, 6);
    }

    [Fact]
    public void A_non_closing_loop_reports_its_misclosure()
    {
        // A 10×10 box where the far corner is reached two ways, 2 m apart.
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 90 0
                1 3 10 0 0
                2 4 10 0 0
                3 4 12 90 0
              endcentreline
            endsurvey
            """);

        Assert.Equal(2.0, At(set, "s.1").MisclosureHint!.Value, 6);
        // Every station in the component shares the component error bar.
        Assert.Equal(2.0, At(set, "s.4").MisclosureHint!.Value, 6);
    }

    [Fact]
    public void A_clean_traverse_has_no_misclosure_hint()
    {
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 90 0
                2 3 10 0 0
              endcentreline
            endsurvey
            """);

        Assert.Null(At(set, "s.3").MisclosureHint);
    }

    [Fact]
    public void Equated_stations_share_one_frame_across_surveys()
    {
        // Two east-running legs joined end-to-start by an equate → one 30 m line.
        var set = Positions("""
            survey cave
              survey a
                centreline
                  data normal from to length compass clino
                  1 2 10 90 0
                endcentreline
              endsurvey
              survey b
                centreline
                  data normal from to length compass clino
                  1 2 10 90 0
                endcentreline
              endsurvey
              equate 2@a 1@b
            endsurvey
            """);

        // a.2 and b.1 are the same physical point.
        Assert.Equal(At(set, "cave.a.2").X, At(set, "cave.b.1").X, 6);
        Assert.Equal(10.0, At(set, "cave.a.2").X, 6);
        Assert.Equal(20.0, At(set, "cave.b.2").X, 6);
        // One connected piece.
        Assert.Equal(At(set, "cave.a.1").ComponentId, At(set, "cave.b.2").ComponentId);
    }

    [Fact]
    public void A_georeferenced_fix_yields_absolute_altitude_but_not_absolute_easting()
    {
        var set = Positions("""
            survey s
              centreline
                cs UTM33
                fix 1 400000 5000000 800
                data normal from to length compass clino
                1 2 10 0 -90
              endcentreline
            endsurvey
            """);

        Assert.Equal(800.0, At(set, "s.1").AbsoluteAltitude!.Value, 6);
        Assert.Equal(790.0, At(set, "s.2").AbsoluteAltitude!.Value, 6);   // 10 m below the fix
        // Horizontal easting/northing from the fix are deliberately NOT folded in (degrees-vs-metres
        // trap): the frame stays anchor-local.
        Assert.Equal(0.0, At(set, "s.1").X, 6);
        Assert.Equal(0.0, At(set, "s.1").Y, 6);
    }

    [Fact]
    public void Without_a_fix_there_is_no_absolute_altitude()
    {
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 -90
              endcentreline
            endsurvey
            """);

        Assert.Null(At(set, "s.1").AbsoluteAltitude);
        Assert.Null(At(set, "s.2").AbsoluteAltitude);
    }

    [Fact]
    public void Disagreeing_fixes_feed_the_misclosure_hint()
    {
        // Fix 1 at 100 m, fix 3 at 50 m, but the geometry puts 3 twenty metres below 1 → 30 m gap.
        var set = Positions("""
            survey s
              centreline
                cs UTM33
                fix 1 0 0 100
                fix 3 0 0 50
                data normal from to length compass clino
                1 2 10 0 -90
                2 3 10 0 -90
              endcentreline
            endsurvey
            """);

        Assert.Equal(30.0, At(set, "s.1").MisclosureHint!.Value, 6);
    }

    [Fact]
    public void A_plumbed_shot_missing_its_compass_stays_vertically_reliable()
    {
        // A vertical shot with no bearing: depth is exact, horizontal is not.
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 - -90
              endcentreline
            endsurvey
            """);

        var two = At(set, "s.2");
        Assert.Equal(10.0, two.Depth, 6);
        Assert.True(two.VerticalReliable);
        Assert.False(two.HorizontalReliable);
    }

    [Fact]
    public void A_shot_missing_its_clino_is_vertically_unreliable()
    {
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 -
              endcentreline
            endsurvey
            """);

        var two = At(set, "s.2");
        Assert.False(two.VerticalReliable);
        Assert.False(two.HorizontalReliable);
    }

    [Fact]
    public void Splay_endpoints_are_not_placed()
    {
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                flags splay
                2 9 3 45 20
                flags not splay
              endcentreline
            endsurvey
            """);

        Assert.NotNull(set.For(QualifiedName.Parse("s.2")));
        Assert.Null(set.For(QualifiedName.Parse("s.9")));   // wall point, off the skeleton
    }

    [Fact]
    public void Disconnected_pieces_get_distinct_component_ids()
    {
        var set = Positions("""
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 0
                10 11 10 0 0
              endcentreline
            endsurvey
            """);

        Assert.NotEqual(At(set, "s.1").ComponentId, At(set, "s.10").ComponentId);
    }

    [Fact]
    public void An_empty_model_yields_an_empty_set()
    {
        var set = StationPositionEstimator.Estimate(WorkspaceSemanticModel.Empty);
        Assert.Equal(PositionSource.None, set.Source);
        Assert.Empty(set.Positions);
    }

    [Fact]
    public void Get_caches_per_model_instance()
    {
        var model = Ws(("/c.th", """
            survey s
              centreline
                data normal from to length compass clino
                1 2 10 0 0
              endcentreline
            endsurvey
            """));

        Assert.Same(StationPositionEstimator.Get(model), StationPositionEstimator.Get(model));
    }
}
