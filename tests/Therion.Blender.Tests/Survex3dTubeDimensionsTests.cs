// Synthesized tubes for a Survex .3d centerline must use the file's measured passage
// dimensions (XSECT LRUD), not the default radius.

using Therion.Blender.Geometry;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class Survex3dTubeDimensionsTests
{
    [Fact]
    public void Survex3dLeg_WithXSectWalls_TubeReachesTheMeasuredWallDistance()
    {
        const double wall = 3.0; // metres to every wall at both stations
        var bytes = Survex3dFixtureBuilder.V8("cave", "EPSG:31700", '.')
            .V8Label(0, 0, "cave.1", 0, 0, 0)
            .V8Label(0, 1, "2", 10, 0, 0)                   // "cave.1" -1 +"2" → "cave.2"
            .V8Style(0x00)
            .V8Move(0, 0, 0)
            .V8Line(0, (6, "cave"), 10, 0, 0)               // leg cave.1 → cave.2 along +X
            .V8XSect(4, "cave.1", wall, wall, wall, wall)
            .V8XSect(1, "2", wall, wall, wall, wall, last: true)
            .V8Stop()
            .Build();

        var model = Survex3dReader.Read(bytes);
        Assert.Single(model.Shots);
        Assert.Single(model.Passages);
        Assert.Equal(wall, model.Passages[0].Stations[0].Left);

        var geometry = GeometryStage.Build(model, new GeometryOptions { Recenter = false, DepthTint = false });

        // The leg lies on the X axis, so a vertex's radial distance from it is sqrt(y² + z²).
        double maxRadial = geometry.Walls.Vertices.Max(v => Math.Sqrt(v.Y * v.Y + v.Z * v.Z));
        Assert.Equal(wall, maxRadial, 6);
    }
}
