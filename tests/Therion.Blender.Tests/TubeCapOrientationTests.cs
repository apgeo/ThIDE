// A capped tube must have a consistent orientation: every triangle, side or cap, wound so
// its right-hand normal points away from the tube's interior.

using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class TubeCapOrientationTests
{
    [Fact]
    public void CappedTube_AllTrianglesFaceOutward()
    {
        var shot = new CaveShot
        {
            FromPosition = new CaveVector3(0, 0, 0),
            ToPosition = new CaveVector3(10, 0, 0),
            Flags = CaveShotFlags.None,
        };
        var mesh = TubeMesher.Build([shot], new GeometryOptions { TubeSides = 8, CapTubes = true, DepthTint = false });
        var tubeCentre = new CaveVector3(5, 0, 0);

        var inward = new List<int>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var t = mesh.Triangles[i];
            var a = mesh.Vertices[(int)t.A];
            var b = mesh.Vertices[(int)t.B];
            var c = mesh.Vertices[(int)t.C];
            var normal = (b - a).Cross(c - a);
            var centroid = (a + b + c) * (1.0 / 3.0);
            if (normal.Dot(centroid - tubeCentre) <= 0) inward.Add(i);
        }

        Assert.True(inward.Count == 0,
            $"{inward.Count} of {mesh.Triangles.Count} triangles face into the tube (triangle indices {string.Join(",", inward)})");
    }
}
