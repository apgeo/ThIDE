// VIS-02 (debug overlays) — pure logic behind the live centreline preview:
//   * ComputeLayout: spanning-tree positions + per-station connected-component assignment. It does
//     NOT resolve equates/fixes, so disconnected surveys all start at the origin (the cause of the
//     "superimposed tracks" the debug overlays diagnose).
//   * TileComponents: spreads those disconnected pieces into a grid so they stop overlapping.
//   * SketchColors: deterministic, process-independent colour palette for provenance colouring.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
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
