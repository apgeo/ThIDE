// The player in a real Blender, on invented caves: a plan renders, the camera is where the plan
// says, the scene is the one the generated script builds, and a module from a folder outside the
// player is loaded and run. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1;
// THIDE_BLENDER_PATH picks the Blender). Each case keeps its job folder under
// %TEMP%/thide-blender-probe/player-<case>/.

using System.Text;
using System.Text.Json;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;
using Therion.CaveGeometry.Writers;

namespace Therion.Blender.Tests.Player;

public class PlayerBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("player-small", InventedCave.Build(InventedCaveSize.Small)));

    private static readonly Lazy<ProbeAssets> Medium = new(() =>
        SceneProbeRunner.Prepare("player-medium", InventedCave.Build(InventedCaveSize.Medium)));

    /// <summary>The cheapest render there is: one sample on the CPU.</summary>
    private static readonly EngineSpec Fast = new() { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly };

    private static readonly OutputSpec Wide = new() { Kind = OutputKind.Video, Width = 320, Height = 180 };

    private static readonly ProbeOptions OneFrame = new() { Samples = 1 };

    private static SceneSpec Preset(string name) => PlanFixtures.Preset(name) with { Labels = new LabelsSpec() };

    private static SceneSpec Orbit => Preset("Orbit showcase") with { Engine = Fast, Output = Wide };

    /// <summary>The flythrough preset over <paramref name="frames"/> frames at 10 fps.</summary>
    private static SceneSpec Flythrough(int frames) => Preset("Full flythrough") with
    {
        Engine = Fast,
        Animation = new AnimationSpec { Fps = 10, DurationSeconds = frames / 10.0 },
        Output = Wide,
    };

    // ---- a plan renders ----

    [BlenderFact]
    public async Task APlanOfTheMediumCave_Renders_AndTheRunnerFollowsIt()
    {
        var spec = Flythrough(frames: 4) with { Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "clip" } };
        var (plan, job) = PlayerRunner.WriteJob("render", Medium.Value, spec);

        // The runner and its progress parser, as they run a generated script.
        var ticks = new List<RenderProgress>();
        var result = await new BlenderRunner(new RealBlenderProcessLauncher()).RunAsync(
            SceneProbeRunner.Blender,
            new RenderJob(job.ScriptPath, job.Folder, plan.Timeline.FrameCount, spec.Output with { OutputDirectory = job.Folder }),
            new SyncProgress<RenderProgress>(ticks.Add));

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  logs: {job.Folder}");
        Assert.Equal("CPU", result.Device);
        Assert.Equal(4, result.FrameCount);
        Assert.Equal([Path.Combine(job.Folder, "clip.mp4")], result.OutputPaths);
        Assert.Equal([1, 2, 3, 4], ticks.Where(t => t.Frame is not null).Select(t => t.Frame!.Value));
        Assert.Contains(ticks, t => t.Message == "Importing model");
        Assert.Empty(result.Warnings);

        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths[0]);
        Assert.Equal([96, 64], movie.Size);
        Assert.Equal(4, movie.Frames);
        Assert.Equal(10.0, movie.Fps, 3);
    }

    [BlenderFact]
    public async Task Frames_AndAStill_AreNamedTheWayTheCollectorLooksForThem()
    {
        var stills = Preset("Documentation stills") with
        {
            Engine = Fast,
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 64, Height = 64, BaseName = "view" },
        };
        var (plan, frames) = PlayerRunner.WriteJob("frames", Small.Value, stills);
        var (exitCode, log) = await PlayerRunner.RunAsync(frames.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {frames.Folder}");
        Assert.Equal(["1"], PlayerRunner.Reported(log, "done"));
        var collected = OutputCollector.Collect(stills.Output with { OutputDirectory = frames.Folder }, plan.Timeline.FrameCount);
        Assert.True(collected.Verified, collected.Problem);
        Assert.Equal(plan.Timeline.FrameCount, collected.Files.Count);

        var one = Orbit with { Output = new OutputSpec { Kind = OutputKind.Still, Width = 64, Height = 64, BaseName = "one" } };
        var (_, still) = PlayerRunner.WriteJob("still", Small.Value, one);
        (exitCode, _) = await PlayerRunner.RunAsync(still.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {still.Folder}");
        Assert.True(OutputCollector.Collect(one.Output with { OutputDirectory = still.Folder }, 1).Verified);
    }

    [BlenderFact]
    public async Task BuildOnly_BuildsTheSceneAndWritesNothing()
    {
        var (_, job) = PlayerRunner.WriteJob("build-only", Small.Value, Orbit with { Output = Wide with { BaseName = "never" } });

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");

        Assert.Equal(0, exitCode);
        Assert.Equal(["1"], PlayerRunner.Reported(log, "built"));
        Assert.Empty(PlayerRunner.Reported(log, "done"));
        Assert.Empty(Directory.GetFiles(job.Folder, "never*"));
    }

    // ---- the camera ----

    [BlenderFact]
    public async Task TheCamera_IsAtThePoseOfThePlan_OnEveryFrame()
    {
        var (plan, job) = PlayerRunner.WriteJob("camera-track", Medium.Value, Flythrough(frames: 80));
        Assert.Equal(80, plan.Camera.Track.Count);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = Enumerable.Range(1, 80).ToList() });

        Assert.Equal((1, 80, 10.0), (report.Scene.FrameStart, report.Scene.FrameEnd, report.Scene.Fps));
        Assert.All(plan.Camera.Track, pose =>
        {
            int frame = ScenePlanValidator.FrameAt(pose.T, plan.Timeline.Fps);
            var seen = report.Frame(frame).Camera!;
            AssertNear(pose.Eye, seen.Location, 1e-4, $"frame {frame}: position");
            AssertNear(pose.Target, seen.Target!, 1e-4, $"frame {frame}: target");
            double off = AngleDegrees(Array((pose.Target - pose.Eye).Normalized()), seen.Forward);
            Assert.True(off < 0.05, $"frame {frame}: the view is {off:F3}° off the plan's");
            Assert.Equal(plan.Camera.Lens, seen.Lens, 4);
            Assert.Equal(plan.Camera.SensorWidth, seen.SensorWidth, 4);
            Assert.Equal(plan.Camera.ClipStart, seen.ClipStart, 4);
            Assert.Equal(plan.Camera.ClipEnd, seen.ClipEnd, 2);
        });
    }

    [BlenderFact]
    public async Task TheCamera_PassesThroughEveryPose_WhenBlenderEasesBetweenThem()
    {
        var centre = Small.Value.Framing.LocalBounds.Center;
        double far = Small.Value.Framing.LocalBounds.Diagonal;
        var spec = new SceneSpec
        {
            Engine = Fast,
            Camera = new CameraSpec
            {
                Template = CameraTemplate.Viewpoints,
                Dof = new DepthOfFieldSpec { Enabled = true, FStop = 4.0 },
                Viewpoints = new ViewpointParams
                {
                    Viewpoints =
                    [
                        new CameraViewpoint { Position = centre + new CaveVector3(far, 0, far / 4), Target = centre, HoldSeconds = 0.5 },
                        new CameraViewpoint { Position = centre + new CaveVector3(0, far, far / 2), Target = centre, FocalLength = 50 },
                        new CameraViewpoint { Position = centre + new CaveVector3(-far, 0, far / 4), Target = centre },
                    ],
                },
            },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 4 },
            Output = Wide,
        };
        var (plan, job) = PlayerRunner.WriteJob("camera-eased", Small.Value, spec);
        Assert.Equal(PlanInterpolation.Bezier, plan.Camera.Interpolation);
        var keyFrames = plan.Camera.Track.Select(k => ScenePlanValidator.FrameAt(k.T, 10)).ToList();
        int between = (keyFrames[^2] + keyFrames[^1]) / 2;

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. keyFrames, between] });

        Assert.All(plan.Camera.Track.Zip(keyFrames), pair =>
        {
            var seen = report.Frame(pair.Second).Camera!;
            AssertNear(pair.First.Eye, seen.Location, 1e-4, $"frame {pair.Second}");
            Assert.Equal(pair.First.Lens!.Value, seen.Lens, 4);
        });
        Assert.Equal([35.0, 35.0, 50.0, 35.0], plan.Camera.Track.Select(k => k.Lens!.Value));
        Assert.True(report.Object("Camera").DepthOfField);

        // Between two poses the camera is on its way, and the lens with it.
        var onTheWay = report.Frame(between).Camera!;
        Assert.True(Distance(plan.Camera.Track[^2].Eye, onTheWay.Location) > 1.0);
        Assert.True(Distance(plan.Camera.Track[^1].Eye, onTheWay.Location) > 1.0);
        Assert.InRange(onTheWay.Lens, 35.5, 49.5);
    }

    [BlenderFact]
    public async Task AStaticCamera_ShowsTheWholeCave_FromWhereTheGeneratedScriptPutsIt()
    {
        var spec = new SceneSpec { Engine = Fast, Output = Wide with { Kind = OutputKind.Still } };
        var (plan, job) = PlayerRunner.WriteJob("camera-static", Medium.Value, spec);
        Assert.Single(plan.Camera.Track);

        var played = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);
        var generated = await SceneProbeRunner.RunAsync("player-camera-static-generated", Medium.Value, spec, OneFrame);

        var view = played.Frames[0];
        Assert.Equal(8, view.Model!.CornersInFrame);
        Assert.False(view.Model.BeyondFarClip);
        Assert.False(played.Object("Camera").Animated);
        // The script frames the mesh it imported and the plan the model's bounds, which also hold
        // the stations: the two cameras stand within a hundredth of their distance of each other.
        var scripted = generated.Frames[0].Camera!;
        double distance = Distance(plan.Camera.Track[0].Target, scripted.Location);
        Assert.True(Distance(plan.Camera.Track[0].Eye, scripted.Location) < distance / 100,
            $"{Distance(plan.Camera.Track[0].Eye, scripted.Location):F2} m apart at {distance:F0} m");
        Assert.True(AngleDegrees(scripted.Forward, view.Camera!.Forward) < 0.5);
    }

    // ---- the same scene as the generated script ----

    [BlenderFact]
    public async Task TheScene_IsTheOneTheGeneratedScriptBuilds()
    {
        var cases = new (string Name, SceneSpec Spec)[]
        {
            ("orbit", Preset("Orbit showcase")),                // depth gradient, three suns
            ("helix", Preset("Helix descent")),                 // depth gradient, sun and sky
            ("flythrough", Flythrough(frames: 40)),             // procedural rock, headlamp
            ("stills", Preset("Documentation stills")),         // constant interpolation
        };
        foreach (var (name, preset) in cases)
        {
            var spec = preset with { Output = preset.Output with { Width = 320, Height = 180 } };
            var options = new ProbeOptions { Samples = 7 };

            var generated = await SceneProbeRunner.RunAsync("player-same-generated-" + name, Medium.Value, spec, options);
            var (_, job) = PlayerRunner.WriteJob("same-" + name, Medium.Value, spec);
            var played = await PlayerRunner.ProbeAsync(job.ScriptPath, options);

            Assert.Equal(
                (generated.Scene.FrameStart, generated.Scene.FrameEnd, generated.Scene.Fps, generated.Scene.Engine, generated.Scene.Samples),
                (played.Scene.FrameStart, played.Scene.FrameEnd, played.Scene.Fps, played.Scene.Engine, played.Scene.Samples));
            Assert.Equal(generated.Scene.Resolution, played.Scene.Resolution);
            // Built for inspection, a generated script keeps the world of Blender's start-up file
            // where its light rig has none; rendering, it has none either.
            Assert.Equal(generated.Scene.World == "CaveWorld" ? "CaveWorld" : null, played.Scene.World);
            Assert.Equal(generated.Scene.Device, played.Scene.Device);

            Assert.Equal(generated.Objects.Select(o => o.Name), played.Objects.Select(o => o.Name));
            Assert.All(generated.Objects.Zip(played.Objects), pair =>
            {
                var (g, p) = pair;
                string what = $"{name}: {g.Name}";
                Assert.Equal((g.Role, g.Type, g.Parent, g.Animated, g.HideRender), (p.Role, p.Type, p.Parent, p.Animated, p.HideRender));
                Assert.Equal(g.Constraints, p.Constraints);
                Assert.Equal(g.Materials, p.Materials);
                Assert.Equal((g.Vertices, g.Polygons, g.LightType, g.DepthOfField), (p.Vertices, p.Polygons, p.LightType, p.DepthOfField));
                AssertNear(g.Location, p.Location, 1e-4, what);
                if (g.BoundsMin is not null)
                {
                    AssertNear(g.BoundsMin, p.BoundsMin!, 1e-4, what);
                    AssertNear(g.BoundsMax!, p.BoundsMax!, 1e-4, what);
                }
                if (g.Energy is { } energy)
                {
                    Assert.Equal(energy, p.Energy!.Value, 4);
                    Assert.Equal(g.LightSize, p.LightSize);
                    Assert.True(AngleDegrees(g.Direction!, p.Direction!) < 0.01, $"{what} shines {AngleDegrees(g.Direction!, p.Direction!):F3}° off");
                }
            });

            Assert.Equal(generated.Frames.Select(f => f.Frame), played.Frames.Select(f => f.Frame));
            Assert.All(generated.Frames.Zip(played.Frames), pair =>
            {
                var (g, p) = (pair.First.Camera!, pair.Second.Camera!);
                string what = $"{name}: frame {pair.First.Frame}";
                AssertNear(g.Location, p.Location, 1e-4, what);
                AssertNear(g.Target!, p.Target!, 1e-4, what);
                Assert.True(AngleDegrees(g.Forward, p.Forward) < 0.01, what);
                Assert.Equal((g.Lens, g.SensorWidth, g.ClipStart, g.ClipEnd), (p.Lens, p.SensorWidth, p.ClipStart, p.ClipEnd));
                Assert.Equal(pair.First.Model!.CornersInFrame, pair.Second.Model!.CornersInFrame);
                Assert.Equal(pair.First.Model.VerticesInFrame, pair.Second.Model.VerticesInFrame, 3);
            });
        }
    }

    // ---- models ----

    [BlenderFact]
    public async Task SeveralModels_StandWhereThePlanPutsThem_EachWithItsMaterial()
    {
        var walls = Small.Value.Conversion.Geometry.Walls;
        var move = new CaveVector3(500, -40, 12.5);
        string dir = PlayerRunner.NewCase("several-models");
        PlyWriter.WriteFile(walls, Path.Combine(dir, "a.ply"));
        PlyWriter.WriteFile(walls, Path.Combine(dir, "b.ply"));
        PlyWriter.WriteFile(walls with { VertexColors = null }, Path.Combine(dir, "bare.ply"));
        var first = PlayerRunner.PlanFor(Small.Value, Orbit);
        var stops = first.Look.Materials[0].Stops!;
        var plan = first with
        {
            Models =
            [
                new PlanModel { Id = "A", Path = "a.ply", Material = "ramp" },
                new PlanModel { Id = "B", Path = "b.ply", Material = "baked", Translation = move },
                new PlanModel { Id = "Bare", Path = "bare.ply", Material = "baked", Translation = move * 2 },
                new PlanModel { Id = "Noisy", Path = "a.ply", Material = "noise", Translation = move * 3 },
                new PlanModel { Id = "Plain", Path = "a.ply", Material = "plain", Translation = move * 4 },
                new PlanModel { Id = "AsItCame", Path = "a.ply", Translation = move * 5 },
            ],
            Look = first.Look with
            {
                Materials =
                [
                    first.Look.Materials[0] with { Id = "ramp" },
                    new PlanMaterial { Id = "baked", Kind = PlanMaterialKind.VertexColor },
                    new PlanMaterial { Id = "noise", Kind = PlanMaterialKind.NoiseRamp, Stops = [stops[0], stops[^1]], Scale = 8, Bump = 0.2 },
                    new PlanMaterial { Id = "plain", Kind = PlanMaterialKind.Flat, Color = new ColorRgb(0.2, 0.4, 0.6) },
                ],
                World = new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0.1, 0.1, 0.2), Strength = 0.7 },
            },
        };
        var job = PlayerWriter.WriteJob(plan, dir);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);

        var models = report.WithRole(ProbeRoles.Model);
        Assert.Equal(["A", "AsItCame", "B", "Bare", "Noisy", "Plain"], models.Select(m => m.Name));
        var a = report.Object("A");
        Assert.All(plan.Models, model =>
        {
            var seen = report.Object(model.Id);
            AssertNear(model.Translation, seen.Location, 1e-3, model.Id);
            AssertNear(Vector(a.BoundsMin!) + model.Translation, seen.BoundsMin!, 1e-2, model.Id);
            Assert.Equal(a.Polygons, seen.Polygons);
        });
        Assert.Equal(["ramp"], a.Materials);
        Assert.StartsWith("baked", Assert.Single(report.Object("B").Materials));
        Assert.NotEmpty(report.Object("B").ColourAttributes!);
        // A mesh without colours gets the material's plain colour: a second material of that id.
        Assert.Empty(report.Object("Bare").ColourAttributes!);
        Assert.StartsWith("baked", Assert.Single(report.Object("Bare").Materials));
        Assert.NotEqual(report.Object("B").Materials, report.Object("Bare").Materials);
        Assert.Equal(["noise"], report.Object("Noisy").Materials);
        Assert.Equal(["plain"], report.Object("Plain").Materials);
        Assert.Empty(report.Object("AsItCame").Materials);
        Assert.Equal("CaveWorld", report.Scene.World);
    }

    [BlenderFact]
    public async Task Walls_TheirCentreline_AndASecondCave_ShareOneFrame_EachPlacedOnItsOwn()
    {
        var (small, medium) = (Small.Value, Medium.Value);
        string dir = PlayerRunner.NewCase("models-placed");
        // The centreline as a mesh of its own: the legs again, as thin tubes.
        var legs = small.Conversion.Geometry.RecenteredModel.Shots.Select(s => s with { FromLrud = null, ToLrud = null }).ToList();
        PlyWriter.WriteFile(
            TubeMesher.Build(legs, new GeometryOptions { DefaultTubeRadius = 0.05, TubeSides = 4 }), Path.Combine(dir, "centreline.ply"));
        File.Copy(medium.Conversion.Manifest.ModelPath, Path.Combine(dir, "second.ply"));

        // The second cave was converted on its own, about its own origin. It lies two kilometres
        // east of where it was surveyed, turned a quarter and twice as high.
        var secondOrigin = medium.Conversion.Geometry.Offset + new CaveVector3(2000, 0, 0);
        var turn = new PlanRotation { Axis = new CaveVector3(0, 0, 3), Degrees = 90 };
        var plan = PlayerRunner.PlanFor(small, Orbit);
        plan = ScenePlanBuilder.AddModel(
            plan, new PlanModel { Id = "Centreline", Path = "centreline.ply" }, new PlanMaterial { Id = "Line", Color = new ColorRgb(1, 0.8, 0.1) });
        plan = ScenePlanBuilder.AddModel(
            plan, new PlanModel { Id = "SecondCave", Path = "second.ply", Rotation = turn, Scale = new CaveVector3(1, 1, 2) },
            new PlanMaterial { Id = "SecondRock", Kind = PlanMaterialKind.VertexColor }, secondOrigin);
        var job = PlayerWriter.WriteJob(plan, dir, PlayerRunner.MeshOf(small));

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);

        Assert.Equal(["CaveModel", "Centreline", "SecondCave"], report.WithRole(ProbeRoles.Model).Select(m => m.Name));
        var (walls, line, second) = (report.Object("CaveModel"), report.Object("Centreline"), report.Object("SecondCave"));
        Assert.Equal(["CaveRock", "Line", "SecondRock"], new[] { walls, line, second }.Select(m => Assert.Single(m.Materials)));

        // The centreline runs inside its walls.
        AssertNear(CaveVector3.Zero, line.Location, 1e-6, "the centreline");
        for (int axis = 0; axis < 3; axis++)
        {
            Assert.True(line.BoundsMin![axis] >= walls.BoundsMin![axis] && line.BoundsMax![axis] <= walls.BoundsMax![axis], $"axis {axis}");
        }

        // The second cave: every corner of its box stretched, turned east to north, then moved.
        var move = secondOrigin - small.Conversion.Geometry.Offset;
        AssertNear(move, second.Location, 1e-2, "the second cave");
        var box = medium.Conversion.Geometry.Walls.Bounds;
        var corners = (
            from x in new[] { box.Min.X, box.Max.X }
            from y in new[] { box.Min.Y, box.Max.Y }
            from z in new[] { box.Min.Z, box.Max.Z }
            select move + new CaveVector3(-y, x, 2 * z)).ToList();
        AssertNear(new CaveVector3(corners.Min(c => c.X), corners.Min(c => c.Y), corners.Min(c => c.Z)), second.BoundsMin!, 2e-2, "lowest corner");
        AssertNear(new CaveVector3(corners.Max(c => c.X), corners.Max(c => c.Y), corners.Max(c => c.Z)), second.BoundsMax!, 2e-2, "highest corner");
        Assert.Equal(medium.Conversion.Manifest.WallTriangleCount, second.Polygons);
    }

    /// <summary>Whether the Blender under test can read and write glTF. Its glTF add-on needs
    /// numpy, which the Blender a Linux distribution packages may not have.</summary>
    private static readonly Lazy<Task<bool>> ReadsGltf = new(async () =>
    {
        var (exitCode, _) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path, ["--python-expr", "import numpy"], Path.GetTempPath(), 120);
        return exitCode == 0;
    });

    [BlenderFact]
    public async Task AGlb_ComesInTheSameWayUpAsThePly()
    {
        // glTF has Y up and Blender's importer turns that to Z up; GlbWriter writes Y up.
        var walls = Small.Value.Conversion.Geometry.Walls;
        string dir = PlayerRunner.NewCase("glb");
        PlyWriter.WriteFile(walls, Path.Combine(dir, "walls.ply"));
        GlbWriter.WriteFile(walls, Path.Combine(dir, "walls.glb"));
        var first = PlayerRunner.PlanFor(Small.Value, Orbit);
        var plan = first with
        {
            Models =
            [
                new PlanModel { Id = "FromPly", Path = "walls.ply", Material = "CaveRock" },
                new PlanModel { Id = "FromGlb", Path = "walls.glb", Material = "CaveRock" },
            ],
        };
        var job = PlayerWriter.WriteJob(plan, dir);

        if (!await ReadsGltf.Value)
        {
            // This Blender cannot: the player says so, and names the model and the reason.
            var (error, _) = await Refused(job);
            Assert.StartsWith("models[1]: this Blender cannot import the GLB file (ModuleNotFoundError: No module named 'numpy')", error);
            return;
        }
        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);

        var (ply, glb) = (report.Object("FromPly"), report.Object("FromGlb"));
        Assert.Equal(ProbeRoles.Model, glb.Role);
        Assert.Equal(ply.Polygons, glb.Polygons);
        AssertNear(ply.BoundsMin!, glb.BoundsMin!, 1e-3, "lowest corner");
        AssertNear(ply.BoundsMax!, glb.BoundsMax!, 1e-3, "highest corner");
        Assert.Equal(["CaveRock"], glb.Materials);
        // Nothing else of the file stays in the scene.
        Assert.Equal(["Camera", "CameraTarget", "Fill", "FromGlb", "FromPly", "Key", "Rim"], report.Objects.Select(o => o.Name));
    }

    [BlenderFact]
    public async Task AGlbOfSeveralParts_BecomesOneModel_WithThePartsWhereTheirNodesPutThem()
    {
        if (!await ReadsGltf.Value) return; // no glTF in this Blender: see AGlb_ComesInTheSameWayUpAsThePly

        string dir = PlayerRunner.NewCase("glb-parts");
        var (exitCode, log) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path, ["--python", PlayerRunner.TwoPartGlbScript, "--", Path.Combine(dir, "parts.glb")], dir, 120);
        Assert.True(exitCode == 0 && File.Exists(Path.Combine(dir, "parts.glb")), "Blender wrote no GLB: " + BlenderTestProcess.ExceptionLine(log));
        var plan = PlayerRunner.PlanFor(Small.Value, Orbit) with
        {
            Models = [new PlanModel { Id = "Parts", Path = "parts.glb", Translation = new CaveVector3(100, 0, 0) }],
        };
        var job = PlayerWriter.WriteJob(plan, dir);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);

        // A 2 m cube about (11, 2, 3) and a 4 m cube about (1, 22, 8), moved 100 m east.
        var model = Assert.Single(report.WithRole(ProbeRoles.Model));
        Assert.Equal("Parts", model.Name);
        Assert.Null(model.Parent);
        AssertNear(new CaveVector3(99, 1, 2), model.BoundsMin!, 1e-4, "lowest corner");
        AssertNear(new CaveVector3(112, 24, 10), model.BoundsMax!, 1e-4, "highest corner");
        Assert.DoesNotContain(report.Objects, o => o.Type == "EMPTY" && o.Name != "CameraTarget");
    }

    // ---- a draft ----

    /// <summary>What draft_colours.py reports of one model.</summary>
    private sealed record ShownColours(string? Shown, string[] Attributes, double[]? Lowest, double[]? Highest, int Different);

    [BlenderFact]
    public async Task ADraft_ShowsEveryModelInTheColoursOfItsMaterial()
    {
        var walls = Small.Value.Conversion.Geometry.Walls;
        string dir = PlayerRunner.NewCase("draft-colours");
        PlyWriter.WriteFile(walls, Path.Combine(dir, "a.ply"));
        PlyWriter.WriteFile(walls with { VertexColors = null }, Path.Combine(dir, "bare.ply"));
        var (blue, red, green) = (new ColorRgb(0, 0, 1), new ColorRgb(1, 0, 0), new ColorRgb(0.2, 0.6, 0.3));
        PlanColorStop[] stops = [new(0, blue), new(0.5, green), new(1, red)];
        var up = new CaveVector3(0, 0, 500);
        var first = PlayerRunner.PlanFor(Small.Value, Orbit);
        var plan = ScenePlanBuilder.Draft(first with
        {
            Models =
            [
                new PlanModel { Id = "Ramped", Path = "a.ply", Material = "ramp" },
                new PlanModel { Id = "RampedAbove", Path = "a.ply", Material = "ramp", Translation = up },
                new PlanModel { Id = "Plain", Path = "a.ply", Material = "plain" },
                new PlanModel { Id = "Noisy", Path = "a.ply", Material = "noise" },
                new PlanModel { Id = "Baked", Path = "a.ply", Material = "baked" },
                new PlanModel { Id = "BakedBare", Path = "bare.ply", Material = "baked" },
                new PlanModel { Id = "AsItCame", Path = "a.ply" },
            ],
            Look = first.Look with
            {
                Materials =
                [
                    new PlanMaterial { Id = "ramp", Kind = PlanMaterialKind.HeightRamp, Stops = stops, Low = walls.Bounds.Min.Z, High = walls.Bounds.Max.Z },
                    new PlanMaterial { Id = "plain", Kind = PlanMaterialKind.Flat, Color = green },
                    new PlanMaterial { Id = "noise", Kind = PlanMaterialKind.NoiseRamp, Color = red, Stops = [stops[0], stops[2]] },
                    new PlanMaterial { Id = "baked", Kind = PlanMaterialKind.VertexColor, Color = blue },
                ],
            },
        });
        var job = PlayerWriter.WriteJob(plan, dir);
        string coloursPath = Path.Combine(dir, "colours.json");

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);
        var (exitCode, log) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path,
            ["--python", job.ScriptPath, "--python", PlayerRunner.DraftColoursScript, "--", "--build-only", "--colours", coloursPath], dir, 600);
        Assert.True(exitCode == 0 && File.Exists(coloursPath), "No colours were reported: " + BlenderTestProcess.ExceptionLine(log));
        var colours = JsonSerializer.Deserialize<Dictionary<string, ShownColours>>(
            File.ReadAllText(coloursPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("BLENDER_WORKBENCH", report.Scene.Engine);
        Assert.Equal([160, 90], report.Scene.Resolution);

        // A ramp over height: the colour of each vertex where it stands in the frame.
        var ramped = colours["Ramped"];
        Assert.Equal("ThideDraft", ramped.Shown);
        AssertNear(Rgb(blue), ramped.Lowest!, 2e-3, "the lowest vertex");
        AssertNear(Rgb(red), ramped.Highest!, 2e-3, "the highest vertex");
        Assert.True(ramped.Different > 50, $"only {ramped.Different} colours along the ramp");
        Assert.Equal((1, "ThideDraft"), (colours["RampedAbove"].Different, colours["RampedAbove"].Shown));
        AssertNear(Rgb(red), colours["RampedAbove"].Lowest!, 1e-4, "a model above the ramp");

        // One colour: a flat material's, and a noise ramp's plain one.
        Assert.Equal((1, "ThideDraft"), (colours["Plain"].Different, colours["Plain"].Shown));
        AssertNear(Rgb(green), colours["Plain"].Lowest!, 1e-4, "the flat material");
        Assert.Equal((1, "ThideDraft"), (colours["Noisy"].Different, colours["Noisy"].Shown));
        AssertNear(Rgb(red), colours["Noisy"].Lowest!, 1e-4, "the noise ramp");

        // The mesh's own colours stay what is shown; a mesh without any gets the plain colour.
        Assert.DoesNotContain("ThideDraft", colours["Baked"].Attributes);
        Assert.NotNull(colours["Baked"].Shown);
        Assert.True(colours["Baked"].Different > 50);
        Assert.Equal((1, "ThideDraft"), (colours["BakedBare"].Different, colours["BakedBare"].Shown));
        AssertNear(Rgb(blue), colours["BakedBare"].Lowest!, 1e-4, "the mesh without colours");
        Assert.DoesNotContain("ThideDraft", colours["AsItCame"].Attributes);
    }

    [BlenderFact]
    public async Task ADraft_RendersTheSmallerPicture_InTheSolidView()
    {
        var spec = Flythrough(frames: 4) with { Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180, BaseName = "clip" } };
        var (plan, job) = PlayerRunner.WriteJob("draft-render", Medium.Value, spec, plan => ScenePlanBuilder.Draft(plan));

        var result = await new BlenderRunner(new RealBlenderProcessLauncher()).RunAsync(
            SceneProbeRunner.Blender,
            new RenderJob(job.ScriptPath, job.Folder, plan.Timeline.FrameCount, spec.Output with { OutputDirectory = job.Folder, BaseName = "clip-draft" }),
            new SyncProgress<RenderProgress>(_ => { }));

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  logs: {job.Folder}");
        Assert.Equal("WORKBENCH", result.Device);
        Assert.Equal([Path.Combine(job.Folder, "clip-draft.mp4")], result.OutputPaths);
        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths[0]);
        Assert.Equal([160, 90], movie.Size);
        Assert.Equal(4, movie.Frames);
    }

    // ---- the scene as a Blender file ----

    private static SceneSpec Parts(int frames) => Flythrough(frames) with
    {
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 64, Height = 36, BaseName = "part" },
    };

    [BlenderFact]
    public async Task ASavedScene_OpensAgainAsItWasBuilt_AndNothingWasRendered()
    {
        var (_, job) = PlayerRunner.WriteJob("blend-only", Medium.Value, Parts(frames: 6), plan =>
            ScenePlanBuilder.SaveBlend(plan with { Output = plan.Output with { Directory = "frames" } }, "project/cave.blend", render: false));
        string blend = Path.Combine(job.Folder, "project", "cave.blend");

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);

        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {job.Folder}");
        Assert.Equal([blend], PlayerRunner.Reported(log, "blend"));
        Assert.Equal(["1"], PlayerRunner.Reported(log, "done"));
        Assert.Empty(PlayerRunner.Reported(log, "frame"));
        Assert.False(Directory.Exists(Path.Combine(job.Folder, "frames")));

        var options = new ProbeOptions { Samples = 3 };
        var opened = await PlayerRunner.ProbeBlendAsync(blend, options);
        var built = await PlayerRunner.ProbeAsync(job.ScriptPath, options);

        // The output is named from where the file lies, so the folder can be moved.
        Assert.Equal("//../frames/part_####", opened.Scene.OutputPath);
        Assert.Equal(built.Scene with { OutputPath = "" }, opened.Scene with { OutputPath = "", Resolution = built.Scene.Resolution });
        Assert.Equal(built.Scene.Resolution, opened.Scene.Resolution);
        Assert.Equal(built.Objects.Select(o => (o.Name, o.Role, o.Type, o.Parent, o.Vertices, o.Polygons, o.Energy)),
            opened.Objects.Select(o => (o.Name, o.Role, o.Type, o.Parent, o.Vertices, o.Polygons, o.Energy)));
        Assert.All(built.Objects.Zip(opened.Objects), pair =>
        {
            Assert.Equal(pair.First.Materials, pair.Second.Materials);
            Assert.Equal(pair.First.Constraints, pair.Second.Constraints);
            AssertNear(pair.First.Location, pair.Second.Location, 1e-6, pair.First.Name);
        });
        Assert.All(built.Frames.Zip(opened.Frames), pair =>
        {
            AssertNear(pair.First.Camera!.Location, pair.Second.Camera!.Location, 1e-6, $"frame {pair.First.Frame}");
            AssertNear(pair.First.Camera.Forward, pair.Second.Camera.Forward, 1e-6, $"frame {pair.First.Frame}");
        });
    }

    [BlenderFact]
    public async Task ASavedScene_RendersInParts_WhereverItsFolderIsMovedTo()
    {
        var (_, job) = PlayerRunner.WriteJob("blend-parts", Small.Value, Parts(frames: 4), plan =>
            ScenePlanBuilder.SaveBlend(plan with { Output = plan.Output with { Directory = "frames" } }, render: false));
        var (exitCode, _) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {job.Folder}");

        // Somewhere else, with nothing of the job but the file: frames 2 and 3 of the four.
        string moved = PlayerRunner.NewCase("blend-parts-moved");
        string blend = Path.Combine(moved, "scene.blend");
        File.Copy(Path.Combine(job.Folder, "scene.blend"), blend);
        (exitCode, string log) = await BlenderTestProcess.RunAsync(SceneProbeRunner.Blender.Path, [blend, "-s", "2", "-e", "3", "-a"], moved, 600);
        await File.WriteAllTextAsync(Path.Combine(moved, "run.log"), log);

        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {moved}");
        Assert.Equal(
            ["part_0002.png", "part_0003.png"],
            Directory.GetFiles(Path.Combine(moved, "frames")).Select(Path.GetFileName).Order());
        Assert.False(Directory.Exists(Path.Combine(job.Folder, "frames")));
    }

    [BlenderFact]
    public async Task APlan_CanSaveTheSceneAndRenderIt_TheFileFirst()
    {
        var spec = Orbit with { Output = new OutputSpec { Kind = OutputKind.Still, Width = 64, Height = 64, BaseName = "one" } };
        var (_, job) = PlayerRunner.WriteJob("blend-and-render", Small.Value, spec, plan => ScenePlanBuilder.SaveBlend(plan));
        string blend = Path.Combine(job.Folder, "scene.blend");

        // A scene that is only built is not saved.
        var (exitCode, _) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(blend));

        (exitCode, string log) = await PlayerRunner.RunAsync(job.ScriptPath);

        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {job.Folder}");
        Assert.True(new FileInfo(blend).Length > 0);
        Assert.True(new FileInfo(Path.Combine(job.Folder, "one.png")).Length > 0);
        Assert.True(
            log.IndexOf("THIDE:blend=", StringComparison.Ordinal) < log.IndexOf("THIDE:phase=render", StringComparison.Ordinal),
            "the scene was saved after the render started");
    }

    // ---- modules from outside the player ----

    private static ScenePlan WithMarkers(ScenePlan plan) => plan with
    {
        Features = [.. plan.Features, "marker_cube"],
        Sections = new()
        {
            ["marker_cube"] = JsonSerializer.SerializeToElement(new { size = 2.0, points = new[] { new[] { 1.0, 2.0, 3.0 }, new[] { -4.0, 5.0, 6.0 } } }),
        },
    };

    private static void AssertTheMarkersAreThere(SceneReport report, string jobFolder)
    {
        var markers = report.WithRole("marker");
        Assert.Equal(["marker:0", "marker:1"], markers.Select(m => m.Name));
        AssertNear(new CaveVector3(1, 2, 3), markers[0].Location, 1e-5, "marker:0");
        AssertNear(new CaveVector3(-4, 5, 6), markers[1].Location, 1e-5, "marker:1");
        AssertNear(new CaveVector3(-5, 4, 5), markers[1].BoundsMin!, 1e-5, "marker:1");
        Assert.Equal(["2"], PlayerRunner.Reported(File.ReadAllText(Path.Combine(jobFolder, "run.log")), "markers"));
        // The rest of the scene is built as ever, by the player as it was shipped.
        Assert.Single(report.WithRole(ProbeRoles.Model));
        Assert.All(PlayerFiles.JobFiles, file =>
            Assert.Equal(file.Text, File.ReadAllText(Path.Combine(jobFolder, file.Path.Replace('/', Path.DirectorySeparatorChar)))));
    }

    [BlenderFact]
    public async Task AModuleInAFolderNamedOnTheCommandLine_IsLoadedAndRun()
    {
        var (_, job) = PlayerRunner.WriteJob("extra-module-option", Small.Value, Orbit, WithMarkers);
        Assert.False(File.Exists(Path.Combine(job.Folder, "thide_player", "modules", "marker_cube.py")));

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame, "--modules", PlayerRunner.ExtraModules);

        AssertTheMarkersAreThere(report, job.Folder);
    }

    [BlenderFact]
    public async Task AModuleInAFolderThePlanNames_IsLoadedAndRun()
    {
        var (_, job) = PlayerRunner.WriteJob(
            "extra-module-plan", Small.Value, Orbit, plan => WithMarkers(plan) with { ModuleFolders = ["my_modules"] });
        Directory.CreateDirectory(Path.Combine(job.Folder, "my_modules"));
        File.Copy(Path.Combine(PlayerRunner.ExtraModules, "marker_cube.py"), Path.Combine(job.Folder, "my_modules", "marker_cube.py"));

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame);

        AssertTheMarkersAreThere(report, job.Folder);
    }

    // ---- what the player refuses ----

    /// <summary>Runs a job that must be refused and returns the error it reported.</summary>
    private static async Task<(string Error, string Log)> Refused(PlayerJob job, params string[] arguments)
    {
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, ["--build-only", .. arguments]);
        Assert.True(exitCode == 64, $"exit code {exitCode}, expected 64; logs: {job.Folder}");
        Assert.Empty(PlayerRunner.Reported(log, "built"));
        return (Assert.Single(PlayerRunner.Reported(log, "error")), log);
    }

    private static string ModuleFolder(PlayerJob job, string fileName, string source)
    {
        string folder = Path.Combine(job.Folder, "other_modules");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), source, new UTF8Encoding(false));
        return folder;
    }

    [BlenderFact]
    public async Task APlanThatNeedsAModuleThePlayerLacks_IsRefusedBeforeAnythingIsBuilt()
    {
        var (_, job) = PlayerRunner.WriteJob("refuse-missing-module", Small.Value, Orbit, WithMarkers);

        var (error, log) = await Refused(job);

        Assert.Contains("needs the module \"marker_cube\"", error);
        Assert.DoesNotContain("import", PlayerRunner.Reported(log, "phase"));
    }

    [BlenderFact]
    public async Task APlanOfANewerVersion_IsRefused()
    {
        var (_, job) = PlayerRunner.WriteJob("refuse-newer-plan", Small.Value, Orbit);
        File.WriteAllText(job.PlanPath, File.ReadAllText(job.PlanPath).Replace("\"version\": 1,", "\"version\": 2,"));

        var (error, _) = await Refused(job);

        Assert.Equal("the plan is version 2 and this player reads up to version 1; use a newer player", error);
    }

    [BlenderFact]
    public async Task APlan_CannotNameAModuleFolderThatIsNotBesideIt()
    {
        var (_, job) = PlayerRunner.WriteJob("refuse-outside-folder", Small.Value, Orbit);
        File.WriteAllText(job.PlanPath, File.ReadAllText(job.PlanPath).Replace("\"moduleFolders\": []", "\"moduleFolders\": [\"../assets-player-small\"]"));

        var (error, _) = await Refused(job);

        Assert.Contains("is not a folder beside the plan", error);
    }

    [BlenderFact]
    public async Task AModuleFoundTwice_IsRefused_SoNothingReplacesAModuleSilently()
    {
        var (_, job) = PlayerRunner.WriteJob("refuse-module-twice", Small.Value, Orbit);
        string folder = ModuleFolder(job, "look.py", "def build(ctx, section):\n    pass\n");

        var (error, _) = await Refused(job, "--modules", folder);

        Assert.StartsWith("the module \"look\" is defined more than once", error);
    }

    [BlenderFact]
    public async Task AFileThatIsNotAModule_IsRefused_AndAModuleThatFailsIsNamed()
    {
        var (_, job) = PlayerRunner.WriteJob("refuse-bad-module", Small.Value, Orbit, WithMarkers);
        string folder = ModuleFolder(job, "marker_cube.py", "\"\"\"Nothing to build with.\"\"\"\n");

        var (error, _) = await Refused(job, "--modules", folder);
        Assert.Contains("has no build(ctx, section) function", error);

        ModuleFolder(job, "marker_cube.py", "def build(ctx, section):\n    raise ValueError(\"no cubes today\")\n");
        (error, string log) = await Refused(job, "--modules", folder);
        Assert.Equal("the module \"marker_cube\" failed: ValueError: no cubes today", error);
        Assert.Contains("Traceback", log);

        ModuleFolder(job, "marker_cube.py", "import no_such_library\n");
        (error, _) = await Refused(job, "--modules", folder);
        Assert.StartsWith("the module \"marker_cube\" (", error);
        Assert.EndsWith("cannot be loaded: ModuleNotFoundError: No module named 'no_such_library'", error);
    }

    [BlenderFact]
    public async Task AMissingMesh_IsRefusedWithItsPath_AndASectionNobodyPlaysIsWarnedOf()
    {
        var (_, job) = PlayerRunner.WriteJob("refuse-missing-mesh", Small.Value, Orbit, plan => plan with
        {
            Sections = new() { ["annotations"] = JsonSerializer.SerializeToElement(new[] { new { id = "a1", text = "Big Hall" } }) },
        });
        File.Delete(Path.Combine(job.Folder, "model.ply"));

        var (error, log) = await Refused(job);

        Assert.StartsWith("models[0].path: file not found:", error);
        Assert.Equal(
            ["the plan's \"annotations\" section is not played: no module in \"features\" reads it"],
            PlayerRunner.Reported(log, "warning"));
    }

    [BlenderFact]
    public async Task AnotherPlan_CanBeNamedOnTheCommandLine()
    {
        var (plan, job) = PlayerRunner.WriteJob("plan-option", Small.Value, Orbit);
        string other = Path.Combine(job.Folder, "second.json");
        ScenePlanSerializer.WriteFile(plan with { Timeline = plan.Timeline with { FrameCount = 7 } }, other);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, OneFrame, "--plan", other);

        Assert.Equal(7, report.Scene.FrameEnd);
    }

    // ---- one file ----

    [BlenderFact]
    public async Task ASingleFile_PlaysWithNothingBesideIt()
    {
        var assets = Small.Value;
        var plan = PlayerRunner.PlanFor(assets, Orbit) with { Name = PlayerWriterTests.HostileName };
        string dir = PlayerRunner.NewCase("single-file");
        string script = Path.Combine(dir, "render.py");
        File.WriteAllText(
            script,
            PlayerWriter.SingleFile(plan, new Dictionary<string, byte[]> { ["model.ply"] = File.ReadAllBytes(assets.Conversion.Manifest.ModelPath) }),
            new UTF8Encoding(false));

        var single = await PlayerRunner.ProbeAsync(script, new ProbeOptions { Samples = 3 });
        var (_, job) = PlayerRunner.WriteJob("single-file-as-folder", assets, Orbit);
        var folder = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Samples = 3 });

        Assert.Equal([script], Directory.GetFiles(dir, "*.py", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(dir, "model.ply")));
        // The name of the plan is text, whatever it says.
        Assert.DoesNotContain("owned", File.ReadAllLines(Path.Combine(dir, "run.log")));
        Assert.Equal(folder.Objects.Select(o => (o.Name, o.Role, o.Vertices)), single.Objects.Select(o => (o.Name, o.Role, o.Vertices)));
        Assert.Equal(assets.Conversion.Manifest.WallTriangleCount, single.Object("CaveModel").Polygons);
        Assert.All(folder.Frames.Zip(single.Frames), pair =>
            AssertNear(pair.First.Camera!.Location, pair.Second.Camera!.Location, 1e-6, $"frame {pair.First.Frame}"));
    }

    [BlenderFact]
    public async Task ASingleFile_Renders()
    {
        var assets = Small.Value;
        var spec = Orbit with { Output = new OutputSpec { Kind = OutputKind.Still, Width = 64, Height = 64, BaseName = "single" } };
        string dir = PlayerRunner.NewCase("single-file-render");
        string script = Path.Combine(dir, "anything.py");
        File.WriteAllText(
            script,
            PlayerWriter.SingleFile(
                PlayerRunner.PlanFor(assets, spec),
                new Dictionary<string, byte[]> { ["model.ply"] = File.ReadAllBytes(assets.Conversion.Manifest.ModelPath) }),
            new UTF8Encoding(false));

        var (exitCode, log) = await PlayerRunner.RunAsync(script);

        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {dir}");
        Assert.Equal([Path.Combine(dir, "single.png")], PlayerRunner.Reported(log, "output"));
        Assert.True(new FileInfo(Path.Combine(dir, "single.png")).Length > 0);
    }

    // ---- helpers ----

    private static double[] Array(CaveVector3 v) => [v.X, v.Y, v.Z];

    private static double[] Rgb(ColorRgb c) => [c.R, c.G, c.B];

    private static CaveVector3 Vector(double[] v) => new(v[0], v[1], v[2]);

    private static double Distance(CaveVector3 a, double[] b) => (a - Vector(b)).Length;

    private static void AssertNear(CaveVector3 expected, double[] actual, double tolerance, string what)
        => Assert.True(Distance(expected, actual) <= tolerance, $"{what}: expected {expected}, found {Vector(actual)}");

    private static void AssertNear(double[] expected, double[] actual, double tolerance, string what)
        => AssertNear(Vector(expected), actual, tolerance, what);

    /// <summary>The angle between two directions. From the cross product as well as the dot
    /// product: the arc cosine alone cannot tell a hundredth of a degree from nothing.</summary>
    private static double AngleDegrees(double[] a, double[] b)
    {
        var (u, v) = (Vector(a), Vector(b));
        return Math.Atan2(u.Cross(v).Length, u.Dot(v)) * 180.0 / Math.PI;
    }
}
