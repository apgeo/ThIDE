// The terrain surface in the player, in a real Blender, on an invented cave under an invented
// terrain with an invented four-colour image: it is the terrain the generated script makes, it
// renders the same, a draft and a saved scene deal with it, and one file can carry it. Opt-in
// like the other Blender tests (THIDE_BLENDER_SMOKE=1).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlayerTerrainBlenderTests
{
    private static readonly Lazy<ProbeAssets> WithImage = new(() => SceneProbeRunner.Prepare(
        "player-terrain-image", InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small)), includeSurface: true));

    private static readonly Lazy<ProbeAssets> WithoutImage = new(() => SceneProbeRunner.Prepare(
        "player-terrain-bare", InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small), withImage: false), includeSurface: true));

    private static readonly ProbeOptions OneFrame = new() { Samples = 1 };

    private static SceneSpec Orbit(SurfaceMode mode, double opacity = 0.4) => PlanFixtures.Preset("Orbit showcase") with
    {
        Labels = new LabelsSpec(),
        Surface = new SurfaceSpec { Mode = mode, Opacity = opacity },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
    };

    [BlenderFact]
    public async Task TheTerrain_IsTheOneTheGeneratedScriptMakes()
    {
        var cases = new (string Name, ProbeAssets Assets, SceneSpec Spec)[]
        {
            ("textured", WithImage.Value, Orbit(SurfaceMode.Textured)),
            ("solid", WithImage.Value, Orbit(SurfaceMode.Solid, opacity: 1.0)),
            ("no-image", WithoutImage.Value, Orbit(SurfaceMode.Textured)),
        };
        foreach (var (name, assets, spec) in cases)
        {
            var generated = await SceneProbeRunner.RunAsync("terrain-generated-" + name, assets, spec, OneFrame);
            var (plan, job) = PlayerRunner.WriteJob("terrain-" + name, assets, spec);
            Assert.Empty(ScenePlanBuilder.NotCarried(spec));
            var played = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);

            // The same objects under the same names, and the terrain alike in every way the
            // probe can tell.
            Assert.Equal(generated.Objects.Select(o => o.Name), played.Objects.Select(o => o.Name));
            var (g, p) = (generated.Object("Terrain"), played.Object("Terrain"));
            Assert.Equal((ProbeRoles.Terrain, "MESH", null, false), (p.Role, p.Type, p.Parent, p.HideRender));
            Assert.Equal((g.Role, g.Type, g.Parent, g.HideRender, g.Animated), (p.Role, p.Type, p.Parent, p.HideRender, p.Animated));
            Assert.Equal(g.Materials, p.Materials);
            Assert.Equal(["Terrain"], p.Materials);
            Assert.Equal((g.Vertices, g.Polygons, g.VisibleShadow), (p.Vertices, p.Polygons, p.VisibleShadow));
            Assert.False(p.VisibleShadow);
            Assert.Equal(g.UvMaps, p.UvMaps);
            Near(g.BoundsMin!, p.BoundsMin!, name);
            Near(g.BoundsMax!, p.BoundsMax!, name);
            Near(g.Location, p.Location, name);

            // The image, and what it shows at the terrain's vertices.
            Assert.Equal(generated.Images.Select(i => (i.Size[0], i.Size[1], i.Loaded)), played.Images.Select(i => (i.Size[0], i.Size[1], i.Loaded)));
            Assert.Equal(plan.TexturedMaterials![0].Image is null ? 0 : 1, played.Images.Count);
            Assert.Equal(g.UvSamples?.Count, p.UvSamples?.Count);
            foreach (var (seen, shown) in (g.UvSamples ?? []).Zip(p.UvSamples ?? []))
            {
                Near(seen.Position, shown.Position, name);
                Near(seen.Uv, shown.Uv, name);
                Assert.Equal(seen.ImageColour is null, shown.ImageColour is null);
                if (seen.ImageColour is not null) Near(seen.ImageColour, shown.ImageColour!, name, 1e-3);
            }

            // And the camera is where it is without a terrain: on the cave.
            Near(generated.Frames[0].Camera!.Location, played.Frames[0].Camera!.Location, name);
            Assert.Equal(generated.Frames[0].Model!.CornersInFrame, played.Frames[0].Model!.CornersInFrame);
            string log = await File.ReadAllTextAsync(Path.Combine(job.Folder, "run.log"));
            Assert.Empty(PlayerRunner.Reported(log, "warning"));
        }
    }

    /// <summary>
    /// The cave flat red, from straight above and from straight below, on a transparent
    /// background, under a terrain that is half see-through: rendered by the generated script and
    /// by the player. From above the terrain covers the picture and the cave shows through it;
    /// from below the terrain is not there. The two renders are the same picture.
    /// </summary>
    [BlenderFact]
    public async Task TheTerrain_RendersAsTheGeneratedScriptsDoes_SeeThroughFromAbove_NotThereFromBelow()
    {
        SceneSpec Spec(SurfaceMode mode) => new()
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 8, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = true },
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.8, 0.02, 0.02) },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.StillSet,
                Stills = new StillSetParams { Views = [StandardView.Top, StandardView.Bottom] },
            },
            Surface = new SurfaceSpec { Mode = mode, Opacity = 0.5 },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 96, Height = 54, BaseName = "view" },
        };
        var assets = WithImage.Value;

        string generatedDir = await SceneProbeRunner.RenderAsync("terrain-render-generated", assets, Spec(SurfaceMode.Textured));
        var (_, job) = PlayerRunner.WriteJob("terrain-render", assets, Spec(SurfaceMode.Textured));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        var (_, bareJob) = PlayerRunner.WriteJob("terrain-render-bare", assets, Spec(SurfaceMode.Off));
        (exitCode, log) = await PlayerRunner.RunAsync(bareJob.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {bareJob.Folder}");

        foreach (string view in new[] { "view_0001.png", "view_0002.png" })
        {
            var generated = Png.Decode(await File.ReadAllBytesAsync(Path.Combine(generatedDir, view)));
            var played = Png.Decode(await File.ReadAllBytesAsync(Path.Combine(job.Folder, view)));
            Assert.Equal((generated.Width, generated.Height), (played.Width, played.Height));
            double difference = Enumerable.Range(0, played.Rgba.Length).Average(i => Math.Abs(played.Rgba[i] - generated.Rgba[i]));
            Assert.True(difference < 2.0, $"{view}: the player's picture differs from the generated script's by {difference:F2} of 255 a channel");
        }

        static int Covered(byte[] rgba) => Enumerable.Range(0, rgba.Length / 4).Count(p => rgba[p * 4 + 3] >= 100);
        var top = Png.Decode(await File.ReadAllBytesAsync(Path.Combine(job.Folder, "view_0001.png"))).Rgba;
        var bottom = Png.Decode(await File.ReadAllBytesAsync(Path.Combine(job.Folder, "view_0002.png"))).Rgba;
        var bareTop = Png.Decode(await File.ReadAllBytesAsync(Path.Combine(bareJob.Folder, "view_0001.png"))).Rgba;
        var bareBottom = Png.Decode(await File.ReadAllBytesAsync(Path.Combine(bareJob.Folder, "view_0002.png"))).Rgba;
        Assert.True(Covered(top) > Covered(bareTop) * 2, $"from above the terrain covers {Covered(top)} pixels, the bare cave {Covered(bareTop)}");
        int slack = bottom.Length / 4 / 100;
        Assert.InRange(Covered(bottom), Covered(bareBottom) - slack, Covered(bareBottom) + slack);
    }

    [BlenderFact]
    public async Task ADraft_LeavesASeeThroughTerrainOut_AndDrawsASolidOneInItsColour()
    {
        var (_, seeThrough) = PlayerRunner.WriteJob("terrain-draft", WithImage.Value, Orbit(SurfaceMode.Textured), plan => ScenePlanBuilder.Draft(plan));
        var report = await PlayerRunner.ProbeAsync(seeThrough.ScriptPath, OneFrame);
        string log = await File.ReadAllTextAsync(Path.Combine(seeThrough.Folder, "run.log"));

        Assert.True(report.Object("Terrain").HideRender);
        Assert.False(report.Object("CaveModel").HideRender);
        Assert.Contains("the draft leaves \"Terrain\" out", Assert.Single(PlayerRunner.Reported(log, "warning")));

        var (_, solid) = PlayerRunner.WriteJob("terrain-draft-solid", WithImage.Value, Orbit(SurfaceMode.Solid, opacity: 1.0), plan => ScenePlanBuilder.Draft(plan));
        report = await PlayerRunner.ProbeAsync(solid.ScriptPath, OneFrame);
        log = await File.ReadAllTextAsync(Path.Combine(solid.Folder, "run.log"));

        Assert.False(report.Object("Terrain").HideRender);
        Assert.Contains("ThideDraft", report.Object("Terrain").ColourAttributes!);
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
    }

    [BlenderFact]
    public async Task ASavedScene_CarriesTheTerrainsImageInside_SoTheFileCanBeMovedAlone()
    {
        var (_, job) = PlayerRunner.WriteJob("terrain-blend", WithImage.Value, Orbit(SurfaceMode.Textured),
            plan => ScenePlanBuilder.SaveBlend(plan, "scene.blend", render: false));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        Assert.Empty(PlayerRunner.Reported(log, "warning"));

        string elsewhere = PlayerRunner.NewCase("terrain-blend-moved");
        string moved = Path.Combine(elsewhere, "scene.blend");
        File.Move(Path.Combine(job.Folder, "scene.blend"), moved);
        File.Delete(Path.Combine(job.Folder, "surface.png"));
        var report = await PlayerRunner.ProbeBlendAsync(moved, OneFrame);

        var image = Assert.Single(report.Images);
        Assert.True(image.Loaded, "the image did not travel with the file");
        Assert.Equal([InventedTerrain.ImageWidth, InventedTerrain.ImageHeight], image.Size);
        Assert.Contains(report.Object("Terrain").UvSamples!, sample => sample.ImageColour is { } c
            && Math.Abs(c[0] - InventedTerrain.SouthWest.R / 255.0) < 0.02 && Math.Abs(c[1]) < 0.02 && Math.Abs(c[2]) < 0.02);
    }

    [BlenderFact]
    public async Task ASingleFile_CarriesTheTerrainAndItsImage()
    {
        var assets = WithImage.Value;
        var plan = PlayerRunner.PlanFor(assets, Orbit(SurfaceMode.Textured));
        string dir = PlayerRunner.NewCase("terrain-single-file");
        string script = Path.Combine(dir, "everything.py");
        var files = PlayerRunner.FilesOf(assets, plan).ToDictionary(f => f.Key, f => File.ReadAllBytes(f.Value));
        Assert.Equal(["model.ply", "surface.ply", "surface.png"], files.Keys);
        await File.WriteAllTextAsync(script, PlayerWriter.SingleFile(plan, files));

        var report = await PlayerRunner.ProbeAsync(script, OneFrame);

        Assert.Equal(ProbeRoles.Terrain, report.Object("Terrain").Role);
        Assert.Equal(["UVMap"], report.Object("Terrain").UvMaps);
        Assert.True(Assert.Single(report.Images).Loaded);
        Assert.Equal(["everything.py", "run.log", "scene-report.json"], Directory.GetFiles(dir).Select(Path.GetFileName).Order());
    }

    [BlenderFact]
    public async Task AMaterialForAModelThatIsNotThere_IsRefusedByName()
    {
        var (_, job) = PlayerRunner.WriteJob("terrain-no-model", WithImage.Value, Orbit(SurfaceMode.Solid));
        // Written by hand, past the validator: the material names a model the plan does not have.
        string planPath = Path.Combine(job.Folder, "scene-plan.json");
        await File.WriteAllTextAsync(planPath, (await File.ReadAllTextAsync(planPath)).Replace("\"models\": [\n        \"Terrain\"", "\"models\": [\n        \"Hill\""));

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");

        Assert.Equal(64, exitCode);
        Assert.Equal(["texturedMaterials[0].models: there is no model \"Hill\""], PlayerRunner.Reported(log, "error"));
    }

    private static void Near(double[] expected, double[] actual, string what, double tolerance = 1e-4)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, $"{what}: {string.Join(", ", actual)} is not {string.Join(", ", expected)}");
    }
}
