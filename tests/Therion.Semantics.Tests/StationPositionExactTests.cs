// Dead-reckoning positions at inclined angles, the 3-D misclosure, which station anchors a piece, and
// the datum wording. Truth is computed by hand from the shot readings. Every survey here is invented.

using System;
using System.Collections.Generic;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class StationPositionExactTests
{
    private static PositionSet Positions(string source)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase)
        { ["/c.th"] = new ThParser().Parse("/c.th", source) };
        return StationPositionEstimator.Estimate(WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>()));
    }

    private static StationPosition At(PositionSet set, string name)
    {
        var p = set.For(QualifiedName.Parse(name));
        Assert.NotNull(p);
        return p!;
    }

    private static string Survey(params string[] lines) =>
        "survey s\n  centreline\n" + string.Concat(Array.ConvertAll(lines, l => "    " + l + "\n")) +
        "  endcentreline\nendsurvey\n";

    [Fact]
    public void Inclined_shot_is_projected_with_the_cosine_of_its_clino()
    {
        var set = Positions(Survey("data normal from to length compass clino", "1 2 10 90 30"));
        var two = At(set, "s.2");
        Assert.Equal(8.660254, two.X, 6);
        Assert.Equal(0.0, two.Y, 6);
        Assert.Equal(5.0, two.Z, 6);
        Assert.Equal(0.0, two.Depth, 6);          // no entrance: datum is the highest point, s.2
        Assert.Equal(5.0, At(set, "s.1").Depth, 6);
    }

    [Fact]
    public void Shot_at_compass_30_and_clino_minus_40_lands_on_all_three_axes()
    {
        // horizontal 10 cos 40 = 7.660444; E = h sin 30, N = h cos 30, Z = -10 sin 40.
        var set = Positions(Survey("data normal from to length compass clino", "1 2 10 30 -40"));
        var two = At(set, "s.2");
        Assert.Equal(3.830222, two.X, 6);
        Assert.Equal(6.634139, two.Y, 6);
        Assert.Equal(-6.427876, two.Z, 6);
        Assert.Equal(6.427876, two.Depth, 6);
    }

    [Fact]
    public void Shot_written_backwards_places_the_far_station_the_same()
    {
        var forward = At(Positions(Survey("data normal from to length compass clino", "1 2 10 30 -40")), "s.2");
        var backward = At(Positions(Survey("data normal from to length compass clino", "2 1 10 210 40")), "s.2");
        Assert.Equal(forward.X, backward.X, 9);
        Assert.Equal(forward.Y, backward.Y, 9);
        Assert.Equal(forward.Z, backward.Z, 9);
    }

    [Fact]
    public void Misclosure_hint_is_the_three_dimensional_gap()
    {
        // 1→2 north 10 flat; 2→1 back south 10 at +30°: lands at (0, 1.339746, 5).
        var set = Positions(Survey("data normal from to length compass clino", "1 2 10 0 0", "2 1 10 180 30"));
        Assert.Equal(Math.Sqrt(1.339746 * 1.339746 + 25), At(set, "s.1").MisclosureHint!.Value, 5);
        Assert.Equal(5.176381, At(set, "s.2").MisclosureHint!.Value, 5);
    }

    [Fact]
    public void Vertical_only_misclosure_is_reported()
    {
        // Same plan position both ways, 2 m apart in height.
        var set = Positions(Survey("data normal from to length compass clino",
            "1 2 10 0 -90", "1 3 5 0 -90", "3 2 7 0 -90"));
        Assert.Equal(2.0, At(set, "s.2").MisclosureHint!.Value, 6);
    }

    // ---- anchor choice -------------------------------------------------------------------------

    private static string Chain(params string[] extra)
    {
        var lines = new List<string>(extra)
        {
            "data normal from to length compass clino",
            "1 2 10 0 -90",
            "2 3 10 0 -90",
            "3 4 10 0 -90",
        };
        return Survey(lines.ToArray());
    }

    private static void AssertAnchor(PositionSet set, string station)
    {
        var p = At(set, station);
        Assert.Equal((0.0, 0.0, 0.0), (p.X, p.Y, p.Z));
    }

    [Fact]
    public void Without_fix_or_entrance_the_lexically_smallest_station_anchors()
    {
        AssertAnchor(Positions(Chain()), "s.1");
    }

    [Fact]
    public void An_entrance_anchors_when_there_is_no_fix()
    {
        AssertAnchor(Positions(Chain("station 3 \"\" entrance", "station 4 \"\" entrance")), "s.3");
    }

    [Fact]
    public void A_bare_fix_beats_an_entrance()
    {
        AssertAnchor(Positions(Chain("station 2 \"\" entrance", "fix 4 0 0 100", "fix 3 0 0 110")), "s.3");
    }

    [Fact]
    public void A_georeferenced_fix_beats_a_bare_fix()
    {
        var set = Positions(Chain(
            "fix 2 0 0 100",
            "cs UTM33",
            "fix 4 400000 5000000 70",
            "fix 3 400000 5000000 80"));
        AssertAnchor(set, "s.3");
        // Altitude comes from the anchoring fix; the other fixes only feed the error bar.
        Assert.Equal(80.0, At(set, "s.3").AbsoluteAltitude!.Value, 6);
        Assert.Equal(100.0, At(set, "s.1").AbsoluteAltitude!.Value, 6);
        // Fix 4 agrees (70); fix 2 says 100 where the geometry says 90.
        Assert.Equal(10.0, At(set, "s.1").MisclosureHint!.Value, 6);
    }

    [Fact]
    public void Depth_is_measured_from_the_highest_entrance()
    {
        var set = Positions(Chain("station 3 \"\" entrance", "station 2 \"\" entrance"));
        Assert.Equal(0.0, At(set, "s.2").Depth, 6);
        Assert.Equal(-10.0, At(set, "s.1").Depth, 6);   // above the datum
        Assert.Equal(20.0, At(set, "s.4").Depth, 6);
    }

    // ---- datum description ---------------------------------------------------------------------

    [Fact]
    public void Datum_description_names_the_entrance_and_the_fix()
    {
        var set = Positions(Chain("station 1 \"\" entrance", "cs UTM33", "fix 1 400000 5000000 900"));
        Assert.Equal(
            "Depth is measured down from the highest entrance; absolute altitude is anchored on a fix. " +
            "Positions are approximate (dead-reckoning, no loop closure).",
            set.DatumDescription);
        Assert.Equal(PositionSource.Approximate, set.Source);
    }

    [Fact]
    public void Datum_description_without_entrance_or_fix()
    {
        var set = Positions(Chain());
        Assert.Equal(
            "Depth is measured down from the highest surveyed point (this piece has no entrance); " +
            "no absolute altitude (no georeferenced fix). Positions are approximate (dead-reckoning, no loop closure).",
            set.DatumDescription);
    }

    [Fact]
    public void Datum_description_follows_the_largest_piece()
    {
        // The small piece has an entrance and a fix; the large piece has neither.
        var set = Positions(Survey(
            "data normal from to length compass clino",
            "1 2 10 0 0", "2 3 10 0 0", "3 4 10 0 0",
            "10 11 5 0 0",
            "station 10 \"\" entrance",
            "cs UTM33",
            "fix 10 400000 5000000 500"));
        Assert.StartsWith("Depth is measured down from the highest surveyed point (this piece has no entrance); no absolute altitude",
            set.DatumDescription);
        Assert.Equal(500.0, At(set, "s.10").AbsoluteAltitude!.Value, 6);
    }

    [Fact]
    public void The_empty_set_says_there_are_no_positions()
    {
        Assert.Equal("no positions", PositionSet.Empty.DatumDescription);
        Assert.Equal(PositionSource.None, PositionSet.Empty.Source);
    }
}
