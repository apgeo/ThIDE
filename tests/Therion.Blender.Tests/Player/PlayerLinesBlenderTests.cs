// Lines, a ramp over a model's own heights and a camera's clip range per pose, in a real Blender
// on invented caves: a line is built as tubes through its points, placed and coloured as the plan
// says, changes as a model does and shows the colours of its points in the picture; a ramp in
// the model's own space stays with a model that is moved, in a render and in a draft; and the
// range a camera draws within moves from pose to pose. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlayerLinesBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("lines-small", InventedCave.Build(InventedCaveSize.Small)));

    /// <summary>A grey cave under three suns, seen from one place: one frame.</summary>
    private static SceneSpec OneFrame(RenderEngineKind engine = RenderEngineKind.Cycles) => new()
    {
        Engine = new EngineSpec { Kind = engine, Samples = 16, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = true },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.5, 0.5, 0.5) },
        Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
        Camera = new CameraSpec { Template = CameraTemplate.Static },
        Animation = new AnimationSpec { Fps = 5, DurationSeconds = 0.2 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 192, Height = 108, BaseName = "f" },
    };

    private static void Near(double expected, double actual, string what, double tolerance = 1e-4)
        => Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected}, found {actual}");

    private sealed record Picture(int Width, int Height, byte[] Rgba)
    {
        public static Picture Read(string path)
        {
            var (width, height, rgba) = Png.Decode(File.ReadAllBytes(path));
            return new Picture(width, height, rgba);
        }

        public byte this[int x, int y, int channel] => Rgba[(y * Width + x) * 4 + channel];

        /// <summary>The pixels that are there (not background) and mostly of one channel's colour.</summary>
        public int Count(int channel, int fromX, int toX)
        {
            int count = 0;
            for (int y = 0; y < Height; y++)
                for (int x = fromX; x < toX; x++)
                {
                    if (this[x, y, 3] < 250) continue;
                    int own = this[x, y, channel], others = Math.Max(this[x, y, (channel + 1) % 3], this[x, y, (channel + 2) % 3]);
                    if (own > 60 && own > 2 * others) count++;
                }
            return count;
        }

        public int Solid => Enumerable.Range(0, Width * Height).Count(p => Rgba[p * 4 + 3] >= 250);

        /// <summary>How far this picture is from another, as the mean difference of a channel
        /// (of 255) over the pixels that are there in either.</summary>
        public double DifferenceFrom(Picture other)
        {
            double sum = 0;
            int count = 0;
            for (int p = 0; p < Width * Height; p++)
            {
                if (Rgba[p * 4 + 3] < 250 && other.Rgba[p * 4 + 3] < 250) continue;
                for (int c = 0; c < 4; c++) sum += Math.Abs(Rgba[p * 4 + c] - other.Rgba[p * 4 + c]);
                count += 4;
            }
            return count == 0 ? 0 : sum / count;
        }
    }

    private static async Task<Picture> Render(string name, SceneSpec spec, Func<ScenePlan, ScenePlan> change)
    {
        var (plan, job) = PlayerRunner.WriteJob(name, Small.Value, spec, change);
        Assert.Equal(1, plan.Timeline.FrameCount);
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return Picture.Read(Path.Combine(job.Folder, $"{plan.Output.BaseName}_0001.png"));
    }

    // ---- lines ----

    private static readonly PlanMaterial Yellow = new() { Id = "route", Kind = PlanMaterialKind.Flat, Color = new ColorRgb(0.9, 0.8, 0.1) };

    private static readonly PlanMaterial ByPoint = new() { Id = "by-point", Kind = PlanMaterialKind.VertexColor, Color = new ColorRgb(0.5, 0.5, 0.5) };

    private static ScenePlan WithLines(ScenePlan plan, params PlanLine[] lines) => ScenePlanBuilder.WithNeededFeatures(plan with
    {
        Look = plan.Look with { Materials = [.. plan.Look.Materials, Yellow, ByPoint] },
        Lines = lines,
    });

    [BlenderFact]
    public async Task Lines_AreBuiltAsTubes_PlacedAsModelsAre_AndChangeAsModelsDo()
    {
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 1 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
            Labels = new LabelsSpec(),
        };
        var red = new ColorRgb(1, 0, 0);
        var (plan, job) = PlayerRunner.WriteJob("lines-built", Small.Value, spec, p =>
        {
            p = WithLines(p,
                new PlanLine
                {
                    Id = "Route", Width = 2, Material = "route",
                    Strips =
                    [
                        new PlanLineStrip([new CaveVector3(0, 0, 0), new CaveVector3(10, 0, 0), new CaveVector3(10, 0, 0), new CaveVector3(10, 10, 0)]),
                        new PlanLineStrip([new CaveVector3(0, 0, 5), new CaveVector3(0, 0, 15)]),
                    ],
                },
                new PlanLine
                {
                    Id = "Legs", Role = "survey-lines", Width = 1, Material = "by-point",
                    Translation = new CaveVector3(100, 0, 0), Scale = new CaveVector3(1, 1, 2),
                    Strips = [new PlanLineStrip([new CaveVector3(0, 0, 0), new CaveVector3(0, 0, 10)], [red, new ColorRgb(0, 0, 1)])],
                });
            return ScenePlanBuilder.WithNeededFeatures(p with
            {
                Changes =
                [
                    new PlanChange
                    {
                        Target = new PlanChangeTarget { Id = "Route" },
                        Offset = [new PlanVectorKey(0, new CaveVector3(0, 0, 0)), new PlanVectorKey(0.9, new CaveVector3(0, 0, 9))],
                        Visible = [new PlanVisibleKey(0.9, false)],
                    },
                    new PlanChange
                    {
                        Target = new PlanChangeTarget { Role = "survey-lines" },
                        Tint = [new PlanTintKey(0, red, 0), new PlanTintKey(0.9, red, 0.9)],
                        Opacity = [new PlanNumberKey(0, 1), new PlanNumberKey(0.9, 0.1)],
                        Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 2)), new PlanVectorKey(0.9, new CaveVector3(1, 1, 4))],
                    },
                ],
            });
        });
        Assert.Equal(["models", "lines", "camera_track", "look", "changes", "output"], plan.Features);
        Assert.Empty(ScenePlanValidator.Validate(plan));

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [1, 6, 10] });
        string log = await File.ReadAllTextAsync(Path.Combine(job.Folder, "run.log"));
        // Two pieces and one (a point that is given twice is one point), and one.
        Assert.Equal(["4"], PlayerRunner.Reported(log, "lines"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));

        // A ring of six about each of its five points; six sides to a piece and two ends to a strip.
        var route = report.Object("Route");
        Assert.Equal(("line", "MESH", 30, 22), (route.Role, route.Type, route.Vertices, route.Polygons));
        Assert.Equal(["route"], route.Materials);
        // One metre about its points, and closed where it begins and ends.
        Assert.True(route.BoundsMin!.All(c => c >= -1.0001), string.Join(", ", route.BoundsMin));
        Near(-1, route.BoundsMin[1], "the side of the first strip");
        Assert.True(route.BoundsMax![0] <= 11.0001 && route.BoundsMax[2] <= 15.0001, string.Join(", ", route.BoundsMax));
        Near(10, route.BoundsMax[1], "where the first strip ends");
        Near(15, route.BoundsMax[2], "where the second strip ends");
        Assert.True(route.BoundsMax[0] > 10.4, $"the bend is as wide as the line: {route.BoundsMax[0]}");

        // Moved and stretched as a model is, with the colours of its points on it.
        var legs = report.Object("Legs");
        Assert.Equal(("survey-lines", 12, 8), (legs.Role, legs.Vertices, legs.Polygons));
        Assert.Equal(["by-point"], legs.Materials);
        Assert.Contains("Col", legs.ColourAttributes!);
        // Six-sided, half a metre from its points to its edges.
        Near(-0.5, legs.BoundsMin![1], "the south side of the legs");
        Near(0.5, legs.BoundsMax![1], "the north side of the legs");
        Near(100 - 0.5 * Math.Cos(Math.PI / 6), legs.BoundsMin[0], "the west side of the legs");
        Near(100 + 0.5 * Math.Cos(Math.PI / 6), legs.BoundsMax[0], "the east side of the legs");
        Near(0, legs.BoundsMin![2], "the foot of the legs");
        Near(20, legs.BoundsMax![2], "the top of the legs");

        // What changes of a model changes of a line.
        Assert.Equal((true, true), (report.Material("by-point").SeeThrough, report.Material("by-point").Tinted));
        foreach (int f in new[] { 1, 6, 10 })
        {
            double share = (f - 1) / 9.0;
            var moved = report.Frame(f).Thing("Route");
            // (Blender does not work out where a thing is while it is hidden.)
            if (f < 10) Near(9 * share, moved.Location[2], $"frame {f}: the route's height");
            Assert.True(moved.Hidden == f >= 10, $"frame {f}: the route hidden {moved.Hidden}");
            var changed = report.Frame(f).Thing("Legs");
            Near(0.9 * share, changed.TintStrength!.Value, $"frame {f}: the strength of the legs' tint", 2e-5);
            Near(1 - 0.9 * share, changed.Opacity!.Value, $"frame {f}: the legs' opacity", 2e-5);
            Near(2 + 2 * share, changed.Scale[2], $"frame {f}: the legs' stretch", 2e-5);
        }
    }

    [BlenderFact]
    public async Task ALine_ShowsTheColoursOfItsPoints_InThePicture()
    {
        // A thick line across the picture, half-way between the camera and what it looks at: red
        // at its left end, blue at its right.
        var picture = await Render("lines-picture", OneFrame(), plan =>
        {
            var pose = plan.Camera.Track[0];
            var forward = pose.Target - pose.Eye;
            double distance = forward.Length;
            var right = forward.Cross(new CaveVector3(0, 0, 1));
            right = right / right.Length;
            var middle = pose.Eye + forward * 0.5;
            // The picture is as wide as the sensor is to the lens at that distance.
            double halfWidth = distance * 0.5 * plan.Camera.SensorWidth / plan.Camera.Lens / 2;
            return WithLines(plan, new PlanLine
            {
                Id = "Across", Width = halfWidth * 0.15, Material = "by-point",
                Strips = [new PlanLineStrip([middle - right * (halfWidth * 0.8), middle + right * (halfWidth * 0.8)], [new ColorRgb(1, 0, 0), new ColorRgb(0, 0, 1)])],
            });
        });

        int third = picture.Width / 3;
        int redLeft = picture.Count(0, 0, third), redRight = picture.Count(0, picture.Width - third, picture.Width);
        int blueLeft = picture.Count(2, 0, third), blueRight = picture.Count(2, picture.Width - third, picture.Width);
        string counts = $"red {redLeft} left and {redRight} right, blue {blueLeft} left and {blueRight} right";
        Assert.True(redLeft > 40 && blueRight > 40, $"the line does not show its colours: {counts}");
        Assert.True(redRight == 0 && blueLeft == 0, $"the colours are on the wrong ends: {counts}");
    }

    [BlenderFact]
    public async Task WhatALineCannotBe_IsRefused_ByName()
    {
        var (plan, job) = PlayerRunner.WriteJob("lines-refused", Small.Value, OneFrame(), p => WithLines(p, new PlanLine
        {
            Id = "Route", Width = 2, Material = "route", Strips = [new PlanLineStrip([new CaveVector3(0, 0, 0), new CaveVector3(10, 0, 0)])],
        }));
        var line = plan.Lines![0];

        async Task<string> Refused(PlanLine changed)
        {
            ScenePlanSerializer.WriteFile(plan with { Lines = [changed] }, job.PlanPath);
            var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
            Assert.True(exitCode == 64, $"exit code {exitCode}, expected 64; logs: {job.Folder}");
            return Assert.Single(PlayerRunner.Reported(log, "error"));
        }

        Assert.Equal("lines[0].width: a line is a positive number of metres thick", await Refused(line with { Width = 0 }));
        Assert.Equal("lines[0].strips: a line needs at least one strip", await Refused(line with { Strips = [] }));
        Assert.Equal(
            "lines[0].strips[0].points: a strip runs through at least two points",
            await Refused(line with { Strips = [new PlanLineStrip([new CaveVector3(0, 0, 0)])] }));
        Assert.Equal(
            "lines[0].strips[0].colors: a strip has a colour for every point or none",
            await Refused(line with { Strips = [new PlanLineStrip(line.Strips[0].Points, [new ColorRgb(1, 0, 0)])] }));
        Assert.Equal(
            "lines[0].strips: either every strip gives its points colours or none does",
            await Refused(line with { Strips = [line.Strips[0], new PlanLineStrip(line.Strips[0].Points, [new ColorRgb(1, 0, 0), new ColorRgb(1, 0, 0)])] }));
        Assert.Equal(
            "the model \"Route\" asks for the material \"no-such\", which look.materials does not have",
            await Refused(line with { Material = "no-such" }));

        // Built after the look, a line would be left without its material.
        ScenePlanSerializer.WriteFile(plan with { Features = ["models", "camera_track", "look", "lines", "output"] }, job.PlanPath);
        var (late, said) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.Equal(64, late);
        Assert.Equal(
            ["lines: list \"lines\" before \"look\" in \"features\": the look gives the lines their materials"],
            PlayerRunner.Reported(said, "error"));

        // A player from before there were lines has no such module, and says so.
        ScenePlanSerializer.WriteFile(plan, job.PlanPath);
        File.Delete(Path.Combine(job.Folder, PlayerFiles.PackageName, "modules", "lines.py"));
        var (code, text) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.Equal(64, code);
        Assert.Equal(["the plan needs the module \"lines\", which this player does not have"], PlayerRunner.Reported(text, "error"));
    }

    // ---- a ramp over the model's own heights ----

    [BlenderFact]
    public async Task ARampInTheModelsOwnSpace_StaysWithAModelThatIsMoved_InARenderAndInADraft()
    {
        var bounds = Small.Value.Framing.LocalBounds;
        var lift = new CaveVector3(0, 0, 1000);
        // Blue in the lower half of the cave, red in the upper.
        var (low, high) = (new ColorRgb(0, 0, 1), new ColorRgb(1, 0, 0));
        PlanMaterial Ramp(PlanRampSpace? space) => new()
        {
            Id = ScenePlanBuilder.RockMaterialId, Kind = PlanMaterialKind.HeightRamp, Low = bounds.Min.Z, High = bounds.Max.Z, Space = space,
            Stops = [new PlanColorStop(0, low), new PlanColorStop(0.45, low), new PlanColorStop(0.55, high), new PlanColorStop(1, high)],
        };
        // The cave and the camera a kilometre up, or where they are.
        ScenePlan Scene(ScenePlan plan, PlanRampSpace? space, bool lifted, bool draft)
        {
            plan = plan with { Look = plan.Look with { Materials = [Ramp(space)] } };
            if (lifted)
                plan = plan with
                {
                    Models = [plan.Models[0] with { Translation = lift }],
                    Camera = plan.Camera with { Track = [.. plan.Camera.Track.Select(k => k with { Eye = k.Eye + lift, Target = k.Target + lift })] },
                };
            return draft ? ScenePlanBuilder.Draft(plan, scale: 1) with { Output = plan.Output } : plan;
        }

        foreach (bool draft in new[] { false, true })
        {
            string kind = draft ? "draft" : "render";
            var home = await Render($"ramp-home-{kind}", OneFrame(), p => Scene(p, null, lifted: false, draft));
            var inTheScene = await Render($"ramp-scene-{kind}", OneFrame(), p => Scene(p, PlanRampSpace.Scene, lifted: true, draft));
            var inTheModel = await Render($"ramp-model-{kind}", OneFrame(), p => Scene(p, PlanRampSpace.Model, lifted: true, draft));

            int cave = home.Solid;
            int blueAtHome = home.Count(2, 0, home.Width), redAtHome = home.Count(0, 0, home.Width);
            string atHome = $"{kind}: of {cave} pixels of cave {blueAtHome} are blue and {redAtHome} red";
            Assert.True(cave > 300 && blueAtHome > cave / 20 && redAtHome > cave / 20, atHome);
            double fromScene = home.DifferenceFrom(inTheScene), fromModel = home.DifferenceFrom(inTheModel);
            Assert.True(fromModel < 2 && fromScene > 10 * Math.Max(fromModel, 0.5),
                $"{kind}: lifted, the picture is {fromScene:F2} of 255 from the one at home with a ramp in the scene, {fromModel:F2} with a ramp in the model");

            // A kilometre up, a ramp in the scene has the whole cave above its top: all red.
            Assert.Equal(0, inTheScene.Count(2, 0, home.Width));
            Assert.True(inTheScene.Count(0, 0, home.Width) > cave * 0.6, $"{kind}: lifted through a ramp in the scene, {inTheScene.Count(0, 0, home.Width)} of {cave} pixels are red");
            // A ramp in the model has gone up with it: the same picture as at home.
            int blue = inTheModel.Count(2, 0, home.Width), red = inTheModel.Count(0, 0, home.Width);
            Assert.True(Math.Abs(blue - blueAtHome) <= cave / 50 + 2 && Math.Abs(red - redAtHome) <= cave / 50 + 2,
                $"{atHome}; lifted with a ramp of its own, {blue} are blue and {red} red");
        }
    }

    // ---- the clip range of a pose ----

    [BlenderFact]
    public async Task ThePosesOfACamera_DrawWithinTheirOwnClipRanges()
    {
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 1.1 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
            Labels = new LabelsSpec(),
        };
        // From far away to close by in a second: the first pose draws from 5 m to 4 km, the last
        // from 5 cm to 200 m, and the pose between them as the camera says.
        var (plan, job) = PlayerRunner.WriteJob("camera-clip", Small.Value, spec, p =>
        {
            var first = p.Camera.Track[0];
            var last = p.Camera.Track[^1];
            return p with
            {
                Camera = p.Camera with
                {
                    ClipStart = 1, ClipEnd = 1000, Interpolation = PlanInterpolation.Linear,
                    Track =
                    [
                        new PlanCameraKey(0, first.Eye, first.Target) { ClipStart = 5, ClipEnd = 4000 },
                        new PlanCameraKey(0.5, last.Eye, last.Target),
                        new PlanCameraKey(1, first.Eye, first.Target) { ClipStart = 0.05, ClipEnd = 200 },
                    ],
                },
            };
        });
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Equal(11, plan.Timeline.FrameCount);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [1, 3, 6, 9, 11] });
        var expected = new Dictionary<int, (double Start, double End)>
        {
            [1] = (5, 4000), [3] = (3.4, 2800), [6] = (1, 1000), [9] = (0.43, 520), [11] = (0.05, 200),
        };
        foreach (var (f, (start, end)) in expected)
        {
            var camera = report.Frame(f).Camera!;
            Near(start, camera.ClipStart, $"frame {f}: the nearest the camera draws", 1e-4);
            Near(end, camera.ClipEnd, $"frame {f}: the farthest the camera draws", 0.05);
        }

        // A camera that stands still takes the range of its one pose.
        var still = plan with { Camera = plan.Camera with { Track = [plan.Camera.Track[0]] } };
        ScenePlanSerializer.WriteFile(still, job.PlanPath);
        report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [1, 11] });
        Assert.All(new[] { 1, 11 }, f => Assert.Equal((5, 4000), (report.Frame(f).Camera!.ClipStart, report.Frame(f).Camera!.ClipEnd)));
    }
}
