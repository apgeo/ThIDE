using Therion.Blender.Emit;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class RoofFinderTests
{
    /// <summary>A closed box from (0,0,0) to (10,10,h): twelve triangles.</summary>
    private static CaveMesh Box(double h)
    {
        var v = new List<CaveVector3>();
        for (int i = 0; i < 8; i++)
            v.Add(new CaveVector3((i & 1) * 10.0, ((i >> 1) & 1) * 10.0, ((i >> 2) & 1) * h));
        uint[][] quads = [[0, 1, 3, 2], [4, 5, 7, 6], [0, 1, 5, 4], [2, 3, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5]];
        var t = new List<CaveTriangle>();
        foreach (var q in quads)
        {
            t.Add(new CaveTriangle(q[0], q[1], q[2]));
            t.Add(new CaveTriangle(q[0], q[2], q[3]));
        }
        return new CaveMesh { Vertices = v, Triangles = t };
    }

    [Fact]
    public void InsideABox_TheRoofIsTheLidAbove()
    {
        var finder = new RoofFinder(Box(4.0));

        Assert.Equal(3.0, finder.HeightAbove(new CaveVector3(3.0, 7.0, 1.0))!.Value, 9);
        Assert.Equal(4.0, finder.HeightAbove(new CaveVector3(5.0, 5.0, 0.0))!.Value, 9); // on the floor
        Assert.Equal(4.0, finder.HeightAbove(new CaveVector3(10.0, 10.0, 0.0))!.Value, 9); // on an edge
    }

    [Fact]
    public void BesideOrAboveTheBox_NothingIsAbove()
    {
        var finder = new RoofFinder(Box(4.0));

        Assert.Null(finder.HeightAbove(new CaveVector3(12.0, 5.0, 1.0)));
        Assert.Null(finder.HeightAbove(new CaveVector3(5.0, 5.0, 4.5)));
    }

    [Fact]
    public void EveryStationOfAnInventedCave_HasItsPassageRoofAboveIt()
    {
        var model = InventedCave.Build(InventedCaveSize.Small);
        var meta = SceneMeta.Build(GeometryStage.Build(model));

        // The invented passages are 1-4 m high above the station (LRUD up).
        Assert.All(meta.Stations, s => Assert.InRange(s.Roof!.Value, 0.5, 4.0));
    }

    [Theory]
    [InlineData(InventedCaveSize.Medium)]
    [InlineData(InventedCaveSize.Large)]
    public void EveryStationOfALargerInventedCave_HasARoof_ShaftsIncluded(InventedCaveSize size)
    {
        // A pitch head has no floor under it, so the walls give it no roof; the surveyed up
        // distance of its level legs does. A station halfway down a shaft has only steep legs
        // and no roof above it at all.
        var geometry = GeometryStage.Build(InventedCave.Build(size));
        var meta = SceneMeta.Build(geometry);
        var level = geometry.TubeLegs.SelectMany(l => new[] { l.From, l.To })
            .Where(s => s.UpAxis.Z >= 0.5).Select(s => s.Station).ToHashSet();

        var missing = meta.Stations.Where(s => s.Roof is null && level.Contains(Local(s))).Select(s => s.Name).ToList();
        Assert.True(missing.Count == 0, $"no roof at {string.Join(", ", missing)}");
        var odd = meta.Stations.Where(s => s.Roof is { } r && (r < 0.2 || r > 12.0)).Select(s => $"{s.Name} {s.Roof:F2}").ToList();
        Assert.True(odd.Count == 0, $"roofs out of range: {string.Join(", ", odd)}");
    }

    [Fact]
    public void StationLabels_GoAboveTheRoof_ClearOfIt()
    {
        var meta = SceneMeta.Build(GeometryStage.Build(InventedCave.Build(InventedCaveSize.Small)));
        var spec = new SceneSpec { Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named } } };

        var plan = LabelPlanner.Plan(spec, meta);

        Assert.Equal(meta.Stations.Count, plan.Stations.Count);
        foreach (var label in plan.Stations)
        {
            var station = meta.Stations.Single(s => s.Name == label.Text);
            double lift = label.Position.Z - station.Position.Z;
            Assert.True(lift >= station.Roof!.Value + label.Size / 2.0,
                $"{label.Text}: label {lift:F2} m above the station, roof {station.Roof:F2} m, label size {label.Size:F2} m");
            Assert.Equal(station.Position.X, label.Position.X);
        }
    }

    private static CaveVector3 Local(SceneMetaStation s) => new(s.Position.X, s.Position.Y, s.Position.Z);
}
