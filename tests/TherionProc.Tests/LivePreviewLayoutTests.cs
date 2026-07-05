// (debug overlays) — pure logic behind the live centreline preview:
//   * ComputeLayout: spanning-tree positions + per-station connected-component assignment. It does
//     NOT resolve equates/fixes, so disconnected surveys all start at the origin (the cause of the
//     "superimposed tracks" the debug overlays diagnose).
//   * TileComponents: spreads those disconnected pieces into a grid so they stop overlapping.
//   * SketchColors: deterministic, process-independent colour palette for provenance colouring.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.ViewModels;
using TherionProc.Views;
using Xunit;

namespace TherionProc.Tests;

public class LivePreviewLayoutTests
{
    // A complete leg between two qualified stations (east-pointing so it has horizontal extent).
    private static ShotSymbol Leg(string from, string to, double length = 10, double compass = 90, double clino = 0) =>
        new(QualifiedName.Parse(from), QualifiedName.Parse(to), length, compass, clino, SourceSpan.None);

    private static List<ShotSymbol> TwoDisconnectedSurveys() => new()
    {
        // survey "cave": a chain a1-a2-a3
        Leg("cave.a1", "cave.a2"),
        Leg("cave.a2", "cave.a3"),
        // survey "deep": a separate chain b1-b2 — joined to "cave" only by an equate in real data,
        // which this lightweight layout doesn't resolve, so it's its own component.
        Leg("deep.b1", "deep.b2"),
    };

    [Fact]
    public void Disconnected_surveys_become_separate_components_stacked_at_the_origin()
    {
        var layout = LivePreviewViewModel.ComputeLayout(TwoDisconnectedSurveys());

        Assert.Equal(2, layout.ComponentCount);

        // Each component is rooted at the world origin → at least two stations share (0,0,0),
        // belonging to different components. That overlap is the "superimposed tracks" symptom.
        var atOrigin = layout.Positions
            .Where(kv => kv.Value == (0.0, 0.0, 0.0))
            .Select(kv => layout.Components[kv.Key])
            .Distinct()
            .ToList();
        Assert.True(atOrigin.Count >= 2, "expected disconnected pieces to overlap at the origin");

        // Every station carries a component id, and the two surveys' stations differ.
        Assert.NotEqual(layout.Components["cave.a1"], layout.Components["deep.b1"]);
    }

    [Fact]
    public void Equated_surveys_merge_into_one_component_in_continuation()
    {
        var shots = TwoDisconnectedSurveys();
        var eq = new EquateGraph();
        eq.Union(QualifiedName.Parse("cave.a3"), QualifiedName.Parse("deep.b1")); // stitch the two surveys

        var layout = LivePreviewViewModel.ComputeLayout(shots, eq);

        Assert.Equal(1, layout.ComponentCount);                       // no longer disconnected
        var repA = eq.Find(QualifiedName.Parse("cave.a3")).ToString();
        var repB = eq.Find(QualifiedName.Parse("deep.b1")).ToString();
        Assert.Equal(repA, repB);                                     // both endpoints collapse to one node
        Assert.True(layout.Positions.ContainsKey(repA));             // and it has a single position
    }

    [Fact]
    public void TileComponents_separates_overlapping_pieces()
    {
        var layout = LivePreviewViewModel.ComputeLayout(TwoDisconnectedSurveys());

        // Project to plan (X=E, Y=-N) like the view-model does.
        var p2d = layout.Positions.ToDictionary(
            kv => kv.Key, kv => (X: kv.Value.E, Y: -kv.Value.N), System.StringComparer.Ordinal);

        LivePreviewViewModel.TileComponents(p2d, layout.Components, layout.ComponentCount);

        var box0 = BBox(p2d, layout.Components, 0);
        var box1 = BBox(p2d, layout.Components, 1);
        Assert.False(Overlaps(box0, box1), "tiled components should not overlap");
    }

    [Fact]
    public void Cross_file_equates_merge_surveys_from_separate_files()
    {
        // Two files: survey "a" and survey "b" live in different files, stitched only by a
        // cross-file equate whose per-file binder can't resolve the foreign endpoint.
        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            ["/proj/a.th"] = new ThParser().Parse("/proj/a.th", """
                survey a
                  centreline
                    data normal from to length compass clino
                    0 1 10 90 0
                  endcentreline
                endsurvey
                """),
            ["/proj/b.th"] = new ThParser().Parse("/proj/b.th", """
                survey b
                  centreline
                    data normal from to length compass clino
                    0 1 10 0 0
                  endcentreline
                  equate 0@a 0@b
                endsurvey
                """),
        };
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => false);
        var models = ws.PerFile.Values.ToList();
        var shots = models.SelectMany(m => m.Shots).ToList();

        var equates = LivePreviewViewModel.BuildEquateGraph(models);
        var layout = LivePreviewViewModel.ComputeLayout(shots, equates);

        Assert.Equal(1, layout.ComponentCount);   // a.0 and b.0 merged across files (via the @ equate)
        Assert.Equal(
            equates.Find(QualifiedName.Parse("a.0")).ToString(),
            equates.Find(QualifiedName.Parse("b.0")).ToString());
    }

    [Fact]
    public void Full_path_cross_file_equate_merges_via_suffix_match()
    {
        // The master uses fully-qualified names (cave.a.0) that the per-file binder stores unprefixed
        // (a.0); the suffix fallback should still merge them.
        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            ["/proj/a.th"] = new ThParser().Parse("/proj/a.th", """
                survey a
                  centreline
                    data normal from to length compass clino
                    0 1 10 90 0
                  endcentreline
                endsurvey
                """),
            ["/proj/b.th"] = new ThParser().Parse("/proj/b.th", """
                survey b
                  centreline
                    data normal from to length compass clino
                    0 1 10 0 0
                  endcentreline
                  equate cave.a.1 cave.b.1
                endsurvey
                """),
        };
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => false);
        var models = ws.PerFile.Values.ToList();
        var shots = models.SelectMany(m => m.Shots).ToList();

        var equates = LivePreviewViewModel.BuildEquateGraph(models);
        var layout = LivePreviewViewModel.ComputeLayout(shots, equates);

        Assert.Equal(1, layout.ComponentCount);
        Assert.Equal(
            equates.Find(QualifiedName.Parse("a.1")).ToString(),
            equates.Find(QualifiedName.Parse("b.1")).ToString());
    }

    [Fact]
    public void Equate_on_dotted_station_names_merges_surveys()
    {
        // Therion station names may contain literal dots (e.g. N32.23). The equate resolver must keep
        // the point name whole; splitting it (survey N32 + station 23) never matches the binder's
        // whole-name station, so the surveys would stay separate overlapping components.
        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            ["/proj/a.th"] = new ThParser().Parse("/proj/a.th", """
                survey a
                  centreline
                    data normal from to length compass clino
                    N32.22 N32.23 10 90 0
                  endcentreline
                endsurvey
                """),
            ["/proj/b.th"] = new ThParser().Parse("/proj/b.th", """
                survey b
                  centreline
                    data normal from to length compass clino
                    N32.23 N32.24 10 0 0
                  endcentreline
                  equate N32.23@a N32.23@b
                endsurvey
                """),
        };
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => false);
        var models = ws.PerFile.Values.ToList();
        var shots = models.SelectMany(m => m.Shots).ToList();

        var equates = LivePreviewViewModel.BuildEquateGraph(models);
        var layout = LivePreviewViewModel.ComputeLayout(shots, equates);

        Assert.Equal(1, layout.ComponentCount);   // both surveys merged via the dotted-name equate
        var a = models.SelectMany(m => m.Stations.Keys).First(q => q.ToString() == "a.N32.23");
        var b = models.SelectMany(m => m.Stations.Keys).First(q => q.ToString() == "b.N32.23");
        Assert.Equal(equates.Find(a).ToString(), equates.Find(b).ToString());
    }

    [Fact]
    public void AnchorByFixes_places_fixed_components_in_a_shared_frame()
    {
        var pos = new Dictionary<string, (double E, double N, double Z)>
        {
            ["x0"] = (0, 0, 0), ["x1"] = (10, 0, 0),   // component 0, fixed at x0
            ["y0"] = (0, 0, 0), ["y1"] = (0, 10, 0),   // component 1, fixed at y0 (50m east of x0)
        };
        var comp = new Dictionary<string, int> { ["x0"] = 0, ["x1"] = 0, ["y0"] = 1, ["y1"] = 1 };
        var fixes = new Dictionary<string, (double X, double Y, double Z, string? Cs)>
        {
            ["x0"] = (100, 200, 5, null),
            ["y0"] = (150, 200, 5, null),
        };

        LivePreviewViewModel.AnchorByFixes(pos, comp, 2, fixes);

        Assert.Equal((0.0, 0.0, 0.0), pos["x0"]);     // reference component anchored at the origin
        Assert.Equal((50.0, 0.0, 0.0), pos["y0"]);    // second system 50 m east — no longer overlapping
        Assert.Equal((50.0, 10.0, 0.0), pos["y1"]);   // its shape preserved (translation only)
    }

    [Fact]
    public void AnchorByFixes_does_not_mix_coordinate_systems()
    {
        var pos = new Dictionary<string, (double E, double N, double Z)>
        {
            ["x0"] = (0, 0, 0), ["y0"] = (0, 0, 0),
        };
        var comp = new Dictionary<string, int> { ["x0"] = 0, ["y0"] = 1 };
        var fixes = new Dictionary<string, (double X, double Y, double Z, string? Cs)>
        {
            ["x0"] = (100, 200, 0, "UTM33N"),
            ["y0"] = (45, 25, 0, "long-lat"),   // different cs → must not be folded into x0's frame
        };

        LivePreviewViewModel.AnchorByFixes(pos, comp, 2, fixes);

        Assert.Equal((0.0, 0.0, 0.0), pos["x0"]);   // reference anchored
        Assert.Equal((0.0, 0.0, 0.0), pos["y0"]);   // incompatible cs → left at its relative origin
    }

    [Fact]
    public void Splays_and_incomplete_legs_are_excluded_from_the_layout()
    {
        var shots = new List<ShotSymbol>
        {
            Leg("s.1", "s.2"),
            new(QualifiedName.Parse("s.2"), QualifiedName.Parse("s.x"), null, null, null, SourceSpan.None), // incomplete
            new(QualifiedName.Parse("s.2"), QualifiedName.Parse("s.y"), 5, 0, 0, SourceSpan.None) { Flags = ShotFlags.Splay },
        };

        var layout = LivePreviewViewModel.ComputeLayout(shots);

        Assert.Equal(1, layout.ComponentCount);
        Assert.True(layout.Positions.ContainsKey("s.1"));
        Assert.True(layout.Positions.ContainsKey("s.2"));
        Assert.False(layout.Positions.ContainsKey("s.x")); // no length/compass/clino
        Assert.False(layout.Positions.ContainsKey("s.y")); // splay
    }

    [Fact]
    public void ShotVector_resolves_compass_clino_into_east_north_up()
    {
        // East-pointing, level, 10 m → straight east.
        var (e, n, z) = LivePreviewViewModel.ShotVector(Leg("a.1", "a.2", length: 10, compass: 90, clino: 0));
        Assert.Equal(10, e, 6);
        Assert.Equal(0, n, 6);
        Assert.Equal(0, z, 6);

        // Straight up, 5 m → all in Z.
        var up = LivePreviewViewModel.ShotVector(Leg("a.1", "a.2", length: 5, compass: 0, clino: 90));
        Assert.Equal(0, up.E, 6);
        Assert.Equal(0, up.N, 6);
        Assert.Equal(5, up.Z, 6);
    }

    [Fact]
    public void ProjectVector_drops_north_in_plan_and_up_in_profile()
    {
        var v = (E: 3.0, N: 4.0, Z: 5.0);
        var plan = LivePreviewViewModel.ProjectVector(v, isElevation: false); // plan: east vs north
        Assert.Equal(3.0, plan.X, 6);
        Assert.Equal(-4.0, plan.Y, 6);
        var profile = LivePreviewViewModel.ProjectVector(v, isElevation: true); // profile: east vs up
        Assert.Equal(3.0, profile.X, 6);   // trig at 90° leaves a sub-ULP residue, so compare with tolerance
        Assert.Equal(-5.0, profile.Y, 6);
    }

    [Theory]
    // A profile's horizontal axis is the distance along the projection bearing; up is unchanged.
    [InlineData(0, 4)]    // north bearing → north component (N=4)
    [InlineData(90, 3)]   // east bearing  → east component  (E=3); the default elevation
    [InlineData(180, -4)] // south bearing → mirror of north
    [InlineData(270, -3)] // west bearing  → mirror of east
    public void ProjectVector_profile_extends_along_the_given_bearing(double azimuth, double expectedX)
    {
        var v = (E: 3.0, N: 4.0, Z: 5.0);
        var (x, y) = LivePreviewViewModel.ProjectVector(v, isElevation: true, azimuthDeg: azimuth);
        Assert.Equal(expectedX, x, 6);
        Assert.Equal(-5.0, y, 6);   // up axis is independent of the bearing
    }

    [Fact]
    public void SplayEndpoint_offsets_origin_by_the_projected_splay_vector()
    {
        var origin = (X: 10.0, Y: 20.0);

        // East splay in plan lands east of the origin (north component is zero here).
        var east = LivePreviewViewModel.SplayEndpoint(origin, (E: 5, N: 0, Z: 0), isElevation: false);
        Assert.Equal(15, east.X, 6);
        Assert.Equal(20, east.Y, 6);

        // North splay in plan moves "up" the screen (Y negated so north points up).
        var north = LivePreviewViewModel.SplayEndpoint(origin, (E: 0, N: 5, Z: 0), isElevation: false);
        Assert.Equal(10, north.X, 6);
        Assert.Equal(15, north.Y, 6);

        // A vertical splay shows in profile but not in plan.
        var upProfile = LivePreviewViewModel.SplayEndpoint(origin, (E: 0, N: 0, Z: 5), isElevation: true);
        Assert.Equal(10, upProfile.X, 6);
        Assert.Equal(15, upProfile.Y, 6);
        var upPlan = LivePreviewViewModel.SplayEndpoint(origin, (E: 0, N: 0, Z: 5), isElevation: false);
        Assert.Equal(origin, upPlan);  // no horizontal extent → coincides with the station in plan
    }

    [Theory]
    [InlineData("cave")]
    [InlineData("cave.upper")]
    [InlineData("")]
    [InlineData(null)]
    public void SketchColors_is_stable_in_range_and_matches_ForKey(string? key)
    {
        int i1 = SketchColors.PaletteIndex(key);
        int i2 = SketchColors.PaletteIndex(key);
        Assert.Equal(i1, i2);                                  // deterministic
        Assert.InRange(i1, 0, SketchColors.Count - 1);        // valid slot
        Assert.Equal(SketchColors.ForKey(key), SketchColors.ForKey(key));
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) BBox(
        Dictionary<string, (double X, double Y)> p2d, IReadOnlyDictionary<string, int> comp, int component)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (key, p) in p2d)
            if (comp[key] == component)
            {
                minX = System.Math.Min(minX, p.X); maxX = System.Math.Max(maxX, p.X);
                minY = System.Math.Min(minY, p.Y); maxY = System.Math.Max(maxY, p.Y);
            }
        return (minX, minY, maxX, maxY);
    }

    private static bool Overlaps(
        (double MinX, double MinY, double MaxX, double MaxY) a,
        (double MinX, double MinY, double MaxX, double MaxY) b) =>
        a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;
}
