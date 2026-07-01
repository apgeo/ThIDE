// STRUCT-01 Phase 2 — pure centreline solve: world positions + cave legs, splays excluded.

using Therion.Semantics;
using Therion.Structural;
using static Therion.Structural.Tests.TestModel;

namespace Therion.Structural.Tests;

public class CenterlineGeometryTests
{
    [Fact]
    public void Solve_PlacesStations_AlongTraverse()
    {
        var shots = new[]
        {
            RawShot("A", "B", 10, 0, 0),    // 10 m due north
            RawShot("B", "C", 10, 90, 0),   // 10 m due east
        };
        var sol = CenterlineGeometry.Solve(shots, new Therion.Semantics.EquateGraph());

        var a = sol.PositionOf(QN("A"))!.Value;
        var b = sol.PositionOf(QN("B"))!.Value;
        var c = sol.PositionOf(QN("C"))!.Value;

        Assert.Equal(0, b.E, 6); Assert.Equal(10, b.N, 6); Assert.Equal(0, b.Z, 6);
        Assert.Equal(10, c.E, 6); Assert.Equal(10, c.N, 6); Assert.Equal(0, c.Z, 6);
        Assert.Equal(0, a.E, 6); Assert.Equal(0, a.N, 6);
        Assert.Equal(2, sol.CaveLegs.Length);
        Assert.Equal(1, sol.ComponentCount);
    }

    [Fact]
    public void Solve_ExcludesSplaysFromLegs()
    {
        var shots = new[]
        {
            RawShot("A", "B", 10, 0, 0),
            RawShot("B", "x", 3, 45, 10, ShotFlags.Splay), // wall splay — not a leg
        };
        var sol = CenterlineGeometry.Solve(shots, new Therion.Semantics.EquateGraph());

        Assert.Single(sol.CaveLegs);
        Assert.Null(sol.PositionOf(QN("x")));    // splay endpoint never placed
    }
}
