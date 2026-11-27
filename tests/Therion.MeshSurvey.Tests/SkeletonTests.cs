using Therion.Cave3D;
using Therion.MeshSurvey.Skeleton;
using Therion.MeshSurvey.Tests.Synthetic;

namespace Therion.MeshSurvey.Tests;

public class SkeletonTests
{
    internal static SkeletonResult Build(SyntheticCave cave, double voxel = 0.2, SkeletonOptions? skeleton = null)
    {
        var (session, grid, _) = VoidTests.Build(cave, voxel, meshSpacing: 0.2);
        var options = new MeshSurveyOptions { Skeleton = skeleton ?? new SkeletonOptions() };
        return session.BuildSkeleton(grid, options, [], null, CancellationToken.None);
    }

    [Fact]
    public void Simple_point_test_basics()
    {
        // A voxel between two others on a line is not simple (removing it disconnects them).
        int Bit(int dx, int dy, int dz)
        {
            int i = 0;
            for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    if (x == dx && y == dy && z == dz) return 1 << i;
                    i++;
                }
            throw new InvalidOperationException();
        }
        Assert.False(SimplePoint.IsSimple(Bit(-1, 0, 0) | Bit(1, 0, 0)));
        // A voxel on the face of a block is simple.
        int block = 0;
        for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 0; x++)
                if (!(x == 0 && y == 0 && z == 0)) block |= Bit(x, y, z);
        Assert.True(SimplePoint.IsSimple(block));
        // Fully surrounded: removing it makes a cavity.
        Assert.False(SimplePoint.IsSimple((1 << 26) - 1));
    }

    [Fact]
    public void A_straight_passage_is_one_edge_along_its_axis()
    {
        var r = Build(SyntheticCave.Straight(20, 1.5));
        var edges = r.Graph.LiveEdges.ToList();
        Assert.Single(edges);
        Assert.Equal(0, r.Report.Loops);
        var e = edges[0];
        Assert.InRange(e.Length, 18, 23.5);
        // The axis runs down the middle (internal frame = model − offset; check the shape of the line).
        double ys = e.Points.Skip(5).SkipLast(5).Max(p => Math.Abs(p.Y - e.Points[e.Points.Count / 2].Y));
        Assert.True(ys < 0.3, $"axis wanders {ys:0.00} m");
        Assert.InRange(e.MedianRadius, 1.25, 1.6); // voxel distances are within half a voxel
    }

    [Fact]
    public void A_y_junction_has_three_ends_and_one_junction()
    {
        var r = Build(SyntheticCave.YJunction());
        Assert.Equal(3, r.Report.Ends);
        Assert.Equal(1, r.Report.Junctions);
        Assert.Equal(0, r.Report.Loops);
    }

    [Fact]
    public void The_loop_keeps_its_loop()
    {
        var r = Build(SyntheticCave.Loop());
        Assert.Equal(1, r.Report.RawLoops);
        Assert.Equal(1, r.Report.Loops);
        Assert.Equal(1, r.Report.Ends); // the passage leading in from x = 0
    }

    [Fact]
    public void A_chamber_becomes_a_hub_with_two_portals()
    {
        var r = Build(SyntheticCave.Chamber(), voxel: 0.25);
        Assert.Single(r.Report.Chambers);
        Assert.Equal(2, r.Report.Chambers[0].Portals);
        var hub = r.Graph.LiveNodes.Single(n => n.Kind == SkeletonNodeKind.Chamber);
        Assert.Equal(2, hub.Degree);
        Assert.Equal(0, r.Report.Loops);
        Assert.Equal(2, r.Report.Ends);
    }

    [Theory]
    [InlineData(ChamberPolicy.Axis)]
    [InlineData(ChamberPolicy.Perimeter)]
    [InlineData(ChamberPolicy.Keep)]
    public void Other_chamber_policies_connect_both_passages(ChamberPolicy policy)
    {
        var r = Build(SyntheticCave.Chamber(), voxel: 0.25, new SkeletonOptions { ChamberPolicy = policy });
        Assert.Single(r.Graph.Components());
        // The axis policy's line may end inside the chamber, adding up to two ends of its own.
        if (policy == ChamberPolicy.Axis) Assert.InRange(r.Report.Ends, 2, 4);
        else Assert.Equal(2, r.Report.Ends);
        Assert.DoesNotContain(r.Graph.LiveEdges, e => e.Length < 0.05);
        if (policy == ChamberPolicy.Perimeter) Assert.True(r.Report.Loops >= 1);
    }

    [Fact]
    public void A_vertical_shaft_is_followed()
    {
        var r = Build(SyntheticCave.Shaft());
        Assert.Equal(2, r.Report.Ends);
        Assert.Equal(0, r.Report.Junctions);
        var e = r.Graph.LiveEdges.Single();
        double dz = e.Points.Max(p => p.Z) - e.Points.Min(p => p.Z);
        Assert.InRange(dz, 18, 23);
    }
}
