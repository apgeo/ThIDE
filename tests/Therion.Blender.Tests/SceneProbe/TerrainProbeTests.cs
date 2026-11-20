// The terrain surface in a real Blender, on an invented cave under an invented terrain with an
// invented four-colour image. Opt-in like the other probe tests (THIDE_BLENDER_SMOKE=1).

using Therion.Blender.Emit;
using Therion.Blender.Presets;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.SceneProbe;

public class TerrainProbeTests
{
    private static readonly Lazy<CaveModel> Model = new(() => InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small)));

    private static readonly Lazy<ProbeAssets> WithImage = new(() =>
        SceneProbeRunner.Prepare("terrain-image", Model.Value, includeSurface: true));

    private static readonly Lazy<ProbeAssets> WithoutImage = new(() =>
        SceneProbeRunner.Prepare("terrain-bare", InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small), withImage: false), includeSurface: true));

    private static SceneSpec Orbit(SurfaceMode mode) => BuiltInPresets.All.Single(p => p.Name == "Orbit showcase").Spec with
    {
        Surface = new SurfaceSpec { Mode = mode },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
    };

    [BlenderFact]
    public async Task Textured_DrapesTheImage_NorthUp_AndCastsNoShadow()
    {
        var assets = WithImage.Value;
        var report = await SceneProbeRunner.RunAsync("terrain-textured", assets, Orbit(SurfaceMode.Textured), new ProbeOptions { Samples = 1 });

        var terrain = report.Object("Terrain");
        Assert.Equal(ProbeRoles.Terrain, terrain.Role);
        Assert.Equal(["Terrain"], terrain.Materials);
        Assert.False(terrain.VisibleShadow);
        Assert.Equal(["UVMap"], terrain.UvMaps);
        var expected = TerrainMesher.Build(Model.Value.Surfaces[0], assets.Conversion.Geometry.Offset)!.Mesh;
        Assert.Equal(expected.Triangles.Count, terrain.Polygons);
        Assert.Equal(expected.Vertices.Count, terrain.Vertices);
        var image = Assert.Single(report.Images);
        Assert.Equal([InventedTerrain.ImageWidth, InventedTerrain.ImageHeight], image.Size);
        Assert.True(image.Loaded);

        // Each sampled vertex shows the quadrant of the image it lies in: red south-west, green
        // south-east, blue north-west, white north-east. Vertices on the dividing lines are skipped.
        double midX = (terrain.BoundsMin![0] + terrain.BoundsMax![0]) / 2, midY = (terrain.BoundsMin[1] + terrain.BoundsMax[1]) / 2;
        double spanX = terrain.BoundsMax[0] - terrain.BoundsMin[0], spanY = terrain.BoundsMax[1] - terrain.BoundsMin[1];
        int checkedSamples = 0;
        foreach (var sample in terrain.UvSamples!)
        {
            double dx = sample.Position[0] - midX, dy = sample.Position[1] - midY;
            if (Math.Abs(dx) < spanX * 0.02 || Math.Abs(dy) < spanY * 0.02) continue;
            var want = dy < 0 ? (dx < 0 ? InventedTerrain.SouthWest : InventedTerrain.SouthEast)
                              : (dx < 0 ? InventedTerrain.NorthWest : InventedTerrain.NorthEast);
            var got = sample.ImageColour!;
            Assert.True(
                Math.Abs(got[0] - want.R / 255.0) < 0.02 && Math.Abs(got[1] - want.G / 255.0) < 0.02 && Math.Abs(got[2] - want.B / 255.0) < 0.02,
                $"the vertex at ({dx:F1}, {dy:F1}) m from the terrain's centre shows ({got[0]}, {got[1]}, {got[2]}), wants {want}");
            checkedSamples++;
        }
        Assert.True(checkedSamples >= 20, $"only {checkedSamples} samples were off the dividing lines");
    }

    [BlenderFact]
    public async Task AScriptCarryingItsMesh_DrawsTheTerrainAndItsImage_WithoutTheirFiles()
    {
        var assets = SceneProbeRunner.Prepare("terrain-embedded", Model.Value, includeSurface: true);
        var manifest = assets.Conversion.Manifest;
        var embedded = new ScriptAssets
        {
            SceneMetaJson = await File.ReadAllTextAsync(manifest.SceneMetaPath),
            PlyBytes = await File.ReadAllBytesAsync(manifest.ModelPath),
            SurfacePlyBytes = await File.ReadAllBytesAsync(manifest.SurfacePath!),
            SurfaceImageBytes = await File.ReadAllBytesAsync(manifest.SurfaceImagePath!),
        };
        // The script is all there is: the files it was made from are gone.
        foreach (string file in new[] { manifest.ModelPath, manifest.SceneMetaPath, manifest.SurfacePath!, manifest.SurfaceImagePath! })
            File.Delete(file);
        var spec = Orbit(SurfaceMode.Textured) with { Source = new SourceSpec { SelfContained = true, EmbedMesh = true } };

        var report = await SceneProbeRunner.RunAsync("terrain-embedded", assets, spec, new ProbeOptions { Samples = 1 }, embedded);

        var terrain = report.Object("Terrain");
        Assert.Equal(["Terrain"], terrain.Materials);
        Assert.Equal(["UVMap"], terrain.UvMaps);
        var expected = TerrainMesher.Build(Model.Value.Surfaces[0], assets.Conversion.Geometry.Offset)!.Mesh;
        Assert.Equal((expected.Vertices.Count, expected.Triangles.Count), (terrain.Vertices, terrain.Polygons));
        var image = Assert.Single(report.Images);
        Assert.Equal([InventedTerrain.ImageWidth, InventedTerrain.ImageHeight], image.Size);
        Assert.True(image.Loaded);
        Assert.Contains(terrain.UvSamples!, sample => sample.ImageColour is { } c && Math.Abs(c[0] - InventedTerrain.SouthWest.R / 255.0) < 0.02
            && Math.Abs(c[1] - InventedTerrain.SouthWest.G / 255.0) < 0.02 && Math.Abs(c[2] - InventedTerrain.SouthWest.B / 255.0) < 0.02);
        string log = await File.ReadAllTextAsync(Path.Combine(SceneProbeRunner.Root, "terrain-embedded", "probe.log"));
        Assert.DoesNotContain("THIDE:warning", log);
    }

    [BlenderFact]
    public async Task Terrain_LeavesTheCameraOnTheCave()
    {
        var off = await SceneProbeRunner.RunAsync("terrain-framing-off", WithImage.Value, Orbit(SurfaceMode.Off), new ProbeOptions { Samples = 5 });
        var on = await SceneProbeRunner.RunAsync("terrain-framing-on", WithImage.Value, Orbit(SurfaceMode.Textured), new ProbeOptions { Samples = 5 });

        Assert.DoesNotContain(off.Objects, o => o.Name == "Terrain");
        Assert.Contains(on.Objects, o => o.Name == "Terrain");
        Assert.Equal(off.Frames.Count, on.Frames.Count);
        foreach (var (a, b) in off.Frames.Zip(on.Frames))
        {
            Assert.Equal(a.Camera!.Location, b.Camera!.Location);
            Assert.Equal(a.Camera.Forward, b.Camera.Forward);
            Assert.Equal(a.Camera.ClipEnd, b.Camera.ClipEnd);
            Assert.Equal(8, b.Model!.CornersInFrame);
        }
    }

    [BlenderFact]
    public async Task Textured_WithoutAnImage_IsDrawnSolid()
    {
        var report = await SceneProbeRunner.RunAsync("terrain-no-image", WithoutImage.Value, Orbit(SurfaceMode.Textured), new ProbeOptions { Samples = 1 });

        var terrain = report.Object("Terrain");
        Assert.Equal(["Terrain"], terrain.Materials);
        Assert.Empty(terrain.UvMaps!);
        Assert.Empty(report.Images);
    }

    /// <summary>
    /// Renders the cave flat red from straight above and straight below, with no terrain, a
    /// half see-through one and an opaque one, on a transparent background. From above, the cave
    /// shows through the half see-through terrain and the opaque one hides it (so the terrain
    /// lies over the cave, not somewhere else). From below, the terrain is not there at all.
    /// </summary>
    [BlenderFact]
    public async Task Render_SeeThroughFromAbove_InvisibleFromBelow()
    {
        var off = await RenderTopAndBottom("terrain-render-off", SurfaceMode.Off, 1.0);
        var half = await RenderTopAndBottom("terrain-render-half", SurfaceMode.Solid, 0.5);
        var opaque = await RenderTopAndBottom("terrain-render-opaque", SurfaceMode.Solid, 1.0);

        // From above: where the bare cave is, how red the picture is, and how much is covered.
        var cave = Enumerable.Range(0, off.Top.Pixels).Where(p => off.Top.Alpha(p) > 200).ToList();
        Assert.True(cave.Count > 30, $"the cave covers only {cave.Count} pixels from above");
        double Redness(Picture picture) => cave.Average(p => picture.Red(p) - (picture.Green(p) + picture.Blue(p)) / 2.0);
        double bare = Redness(off.Top), through = Redness(half.Top), hidden = Redness(opaque.Top);
        Assert.True(bare > 80, $"the bare cave is not red: {bare:F0}");
        Assert.True(through > bare * 0.2, $"half see-through: the cave's redness drops from {bare:F0} to {through:F0}");
        Assert.True(Math.Abs(hidden) < bare * 0.1, $"opaque: the cave's redness is still {hidden:F0} of {bare:F0}");
        Assert.True(opaque.Top.Covered > off.Top.Covered * 2, "from above, the terrain covers more of the picture than the cave");

        // From below: the same pixels are covered, with and without the terrain.
        int slack = off.Bottom.Pixels / 100;
        Assert.InRange(opaque.Bottom.Covered, off.Bottom.Covered - slack, off.Bottom.Covered + slack);
        Assert.InRange(half.Bottom.Covered, off.Bottom.Covered - slack, off.Bottom.Covered + slack);
    }

    private sealed record Picture(byte[] Rgba)
    {
        public int Pixels => Rgba.Length / 4;
        public int Red(int p) => Rgba[p * 4];
        public int Green(int p) => Rgba[p * 4 + 1];
        public int Blue(int p) => Rgba[p * 4 + 2];
        public int Alpha(int p) => Rgba[p * 4 + 3];
        public int Covered => Enumerable.Range(0, Pixels).Count(p => Alpha(p) >= 128);
    }

    private static async Task<(Picture Top, Picture Bottom)> RenderTopAndBottom(string caseName, SurfaceMode mode, double opacity)
    {
        var spec = new SceneSpec
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 8, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = true },
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.8, 0.02, 0.02) },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.StillSet,
                Stills = new StillSetParams { Views = [StandardView.Top, StandardView.Bottom] },
            },
            Surface = new SurfaceSpec { Mode = mode, Opacity = opacity },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 96, Height = 54, BaseName = "view" },
        };
        string dir = await SceneProbeRunner.RenderAsync(caseName, WithImage.Value, spec);
        return (Read(Path.Combine(dir, "view_0001.png")), Read(Path.Combine(dir, "view_0002.png")));
    }

    private static Picture Read(string png) => new(Png.Decode(File.ReadAllBytes(png)).Rgba);
}
