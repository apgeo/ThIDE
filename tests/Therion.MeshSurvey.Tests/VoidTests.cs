using Therion.Cave3D;
using Therion.MeshSurvey.Model;
using Therion.MeshSurvey.Tests.Synthetic;
using Therion.MeshSurvey.Volume;

namespace Therion.MeshSurvey.Tests;

public class VoidTests
{
    internal static (MeshSurveySession Session, VoidGrid Grid, VoidReport Report) Build(SyntheticCave cave, double voxel = 0.15, VoidOptions? options = null, double meshSpacing = 0.2)
    {
        var session = MeshSurveySession.Open(new RawModel(cave.Mesh(meshSpacing), null, false, true, "test"));
        var o = new MeshSurveyOptions { Void = (options ?? new VoidOptions()) with { VoxelSize = voxel } };
        var (grid, report) = session.BuildVoid(o, [], null, CancellationToken.None);
        return (session, grid, report);
    }

    [Fact]
    public void Volume_and_distance_of_a_straight_passage()
    {
        var cave = SyntheticCave.Straight(20, 1.5);
        var (session, grid, report) = Build(cave);
        double expected = Math.PI * 1.5 * 1.5 * 20 + 4.0 / 3 * Math.PI * Math.Pow(1.5, 3);
        Assert.InRange(report.Volume, expected * 0.9, expected * 1.1);
        Assert.Equal(1, report.Components);
        Assert.Equal(0, report.Loops);
        // Internal frame = model − offset; the synthetic model's coordinates are model coordinates.
        int id = grid.IdAt(new CaveVector3(10, 0, 0) - session.Model.Offset);
        Assert.True(id >= 0);
        Assert.InRange(grid.Distance[id], 1.3, 1.6);
    }

    [Fact]
    public void Distance_transform_is_exact_against_brute_force()
    {
        var (_, grid, _) = Build(SyntheticCave.Bend(), voxel: 0.3);
        var random = new Random(5);
        // Brute force over the grid's own voxels: nearest non-void voxel centre.
        var nonVoid = new HashSet<(int, int, int)>();
        for (int id = 0; id < grid.Count; id++)
        {
            var (x, y, z) = grid.Coordinates(id);
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (grid.Id(x + dx, y + dy, z + dz) < 0) nonVoid.Add((x + dx, y + dy, z + dz));
        }
        var rim = nonVoid.ToArray();
        for (int i = 0; i < 60; i++)
        {
            int id = random.Next(grid.Count);
            var (x, y, z) = grid.Coordinates(id);
            long best = long.MaxValue;
            foreach (var (rx, ry, rz) in rim)
            {
                long d = (long)(rx - x) * (rx - x) + (long)(ry - y) * (ry - y) + (long)(rz - z) * (rz - z);
                if (d < best) best = d;
            }
            Assert.Equal(best, grid.SquaredDistance[id]);
        }
    }

    [Theory]
    [InlineData("straight", 0)]
    [InlineData("y", 0)]
    [InlineData("loop", 1)]
    [InlineData("chamber", 0)]
    public void Loops_of_the_void(string name, int loops)
    {
        var cave = name switch
        {
            "straight" => SyntheticCave.Straight(),
            "y" => SyntheticCave.YJunction(),
            "loop" => SyntheticCave.Loop(),
            _ => SyntheticCave.Chamber(),
        };
        var (_, _, report) = Build(cave, voxel: 0.25);
        Assert.Equal(loops, report.Loops);
        Assert.Equal(0, report.Cavities);
    }

    [Fact]
    public void Two_components_are_counted()
    {
        var (_, _, report) = Build(SyntheticCave.TwoComponents(), voxel: 0.25);
        Assert.Equal(2, report.Components);
    }

    [Fact]
    public void A_thin_pillar_is_closed_and_a_thick_one_kept()
    {
        // A 0.3 m column in the middle of a 4 m passage, and a 2 m one further along.
        var cave = SyntheticCave.Straight(30, 2.0)
            .Carve(new Capsule(new(8, 0, -3), new(8, 0, 3), 0.15))
            .Carve(new Capsule(new(20, 0, -3), new(20, 0, 3), 1.0));
        var (_, _, report) = Build(cave, voxel: 0.1, new VoidOptions { MinPillarSize = 0.4 }, meshSpacing: 0.1);
        Assert.Equal(1, report.Loops);
        Assert.True(report.ClosingAdded > 0);
    }

    [Fact]
    public void A_squeeze_narrower_than_the_minimum_width_is_cut_and_reported()
    {
        var (_, _, report) = Build(SyntheticCave.Squeeze(), voxel: 0.1, new VoidOptions { MinPassageWidth = 0.8, MinPillarSize = 0 }, meshSpacing: 0.1);
        Assert.NotEmpty(report.Cuts);
        Assert.Equal(2, report.Components);

        var (_, _, kept) = Build(SyntheticCave.Squeeze(), voxel: 0.1, new VoidOptions { MinPassageWidth = 0.3, MinPillarSize = 0 }, meshSpacing: 0.1);
        Assert.Empty(kept.Cuts);
        Assert.Equal(1, kept.Components);
    }

    [Fact]
    public void Euler_characteristic_of_simple_voxel_sets()
    {
        // A solid block has χ = 1 and no loops.
        var cube = new List<(int, int, int)>();
        for (int z = 0; z < 3; z++) for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) cube.Add((x, y, z));
        var g = new VoidGrid(CaveVector3.Zero, 1, cube.ToArray());
        Assert.Equal(8, VoidTopology.EulerTimes8(g));
        // A ring (3×3 with the middle missing) has χ = 0: one loop.
        var ring = cube.Where(v => v.Item3 == 0 && !(v.Item1 == 1 && v.Item2 == 1)).ToArray();
        var r = new VoidGrid(CaveVector3.Zero, 1, ring);
        Assert.Equal(0, VoidTopology.EulerTimes8(r));
        Assert.Equal((1, 0), VoidTopology.LoopsAndCavities(r));
        // A hollow cube (3×3×3 without the centre): one cavity, no loop.
        var hollow = cube.Where(v => v != (1, 1, 1)).ToArray();
        Assert.Equal((0, 1), VoidTopology.LoopsAndCavities(new VoidGrid(CaveVector3.Zero, 1, hollow)));
    }
}
