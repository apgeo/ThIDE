// The plan's changes in a real Blender, on invented caves: every property has the value the plan
// gives it at every frame, a marker goes on pulsing and keeps the size its distance gives it while
// it fades, a fade, a tint and the world's colour show in the picture, a draft deals with them,
// and what cannot be changed is refused by name. More tracks are in PlayerChangesTracksBlenderTests. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public partial class PlayerChangesBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("changes-small", InventedCave.Build(InventedCaveSize.Small)));

    private static readonly Lazy<ProbeAssets> Medium = new(() =>
        SceneProbeRunner.Prepare("changes-medium", InventedCave.Build(InventedCaveSize.Medium)));

    /// <summary>The cheapest render there is: one sample on the CPU.</summary>
    private static readonly EngineSpec Fast = new() { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly };

    private const int Fps = 10;

    /// <summary>The orbit preset over <paramref name="frames"/> frames at 10 fps, with these labels.</summary>
    private static SceneSpec Orbit(int frames, LabelsSpec? labels = null) => PlanFixtures.Preset("Orbit showcase") with
    {
        Engine = Fast,
        Animation = new AnimationSpec { Fps = Fps, DurationSeconds = frames / (double)Fps },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
        Labels = labels ?? new LabelsSpec(),
    };

    private static readonly LabelsSpec EveryGroup = new()
    {
        Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 5 },
        Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 },
        Leads = new LeadMarkerSpec { Show = true, ShowText = true, Sizing = LeadMarkerSizing.CameraDistance },
    };

    private static PlanChange Of(string? id = null, string? role = null) => new() { Target = new PlanChangeTarget { Id = id, Role = role } };

    /// <summary>The plan with these changes, and their module after the ones that make what
    /// they change.</summary>
    private static ScenePlan Changed(ScenePlan plan, params PlanChange[] changes) => plan with
    {
        Features = [.. plan.Features.Where(f => f != "output" && f != ScenePlan.BlendFileFeature && f != ScenePlan.ChangesFeature),
                    ScenePlan.ChangesFeature, .. plan.Features.Where(f => f == ScenePlan.BlendFileFeature), "output"],
        Changes = changes,
    };

    private static double Smooth(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);

    private static double Between(double from, double to, double share) => from + (to - from) * Math.Clamp(share, 0, 1);

    private static void Near(double expected, double? actual, string what, double tolerance = 2e-5)
        => Assert.True(actual is { } a && Math.Abs(expected - a) <= tolerance, $"{what}: expected {expected}, found {actual?.ToString() ?? "nothing"}");

    private static void Near(double[] expected, double[]? actual, string what, double tolerance = 2e-5)
    {
        Assert.True(actual is not null && actual.Length == expected.Length, $"{what}: found {(actual is null ? "nothing" : string.Join(", ", actual))}");
        for (int i = 0; i < expected.Length; i++) Near(expected[i], actual![i], $"{what}[{i}]", tolerance);
    }

    // ---- every property, at every frame ----

    private static readonly int[] Sampled = [1, 3, 6, 8, 11, 13, 16, 20];

    /// <summary>
    /// A plan written by hand over the medium cave's labels: lead markers that appear in a
    /// second, their captions cut, station labels that go and come back, the cave fading,
    /// tinted and stretched, and the world turning blue. A second model wears the cave's
    /// material and changes in nothing.
    /// </summary>
    private static ScenePlan EveryProperty(ScenePlan plan)
    {
        var blue = new ColorRgb(0, 0, 1);
        plan = ScenePlanBuilder.AddModel(
            plan, new PlanModel { Id = "Twin", Path = plan.Models[0].Path, Translation = new CaveVector3(0, 0, 500) }, plan.Look.Materials[0]);
        plan = plan with { Look = plan.Look with { World = new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0, 0, 0), Strength = 0.5 } } };
        return Changed(plan,
            Of(role: ScenePlanBuilder.LeadMarkerRole) with { Visible = [new PlanVisibleKey(0.5, true, 1.0)] },
            Of(role: ScenePlanBuilder.LeadTextRole) with { Visible = [new PlanVisibleKey(1.0, false)] },
            Of(role: ScenePlanBuilder.StationLabelRole) with { Visible = [new PlanVisibleKey(0.2, false, 0.5), new PlanVisibleKey(1.5, true)] },
            Of(ScenePlanBuilder.ModelId) with
            {
                Opacity = [new PlanNumberKey(0, 1), new PlanNumberKey(1.0, 0.2, PlanEasing.Smooth)],
                Tint =
                [
                    new PlanTintKey(0, blue, 0),
                    new PlanTintKey(0.5, blue, 0.8, PlanEasing.Constant),
                    new PlanTintKey(1.5, new ColorRgb(0, 1, 0), 0.4),
                ],
                Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 1)), new PlanVectorKey(1.9, new CaveVector3(1, 1, 3), PlanEasing.Smooth)],
            },
            new PlanChange { WorldColor = [new PlanColorKey(0, new ColorRgb(0, 0, 0)), new PlanColorKey(1.0, new ColorRgb(0.2, 0.4, 0.8))] });
    }

    [BlenderFact]
    public async Task EveryProperty_HasThePlansValue_AtEveryFrame_AndInASavedSceneToo()
    {
        // The cave and its twin are one file: the job is written with that one.
        var plan = ScenePlanBuilder.SaveBlend(EveryProperty(PlayerRunner.PlanFor(Medium.Value, Orbit(20, EveryGroup))), render: false);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase("changes-values"), PlayerRunner.MeshOf(Medium.Value));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        Assert.Equal(["7"], PlayerRunner.Reported(log, "changes"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));

        var options = new ProbeOptions { Frames = Sampled };
        var built = await PlayerRunner.ProbeAsync(job.ScriptPath, options);
        // The saved scene plays the same changes with no Python of the player's in it.
        var opened = await PlayerRunner.ProbeBlendAsync(Path.Combine(job.Folder, "scene.blend"), options);

        var markers = plan.Labels!.Markers!.Items.Select(m => m.Id).ToList();
        var captions = plan.Labels.Texts!.Items.Where(t => t.Role == ScenePlanBuilder.LeadTextRole).Select(t => t.Id).ToList();
        var stations = plan.Labels.Texts.Items.Where(t => t.Role == ScenePlanBuilder.StationLabelRole).Select(t => t.Id).ToList();
        var components = plan.Labels.Texts.Items.Where(t => t.Role == ScenePlanBuilder.ComponentLabelRole).Select(t => t.Id).ToList();
        Assert.True(markers.Count > 0 && captions.Count > 0 && stations.Count > 0 && components.Count > 0, "the cave has a label of every kind");

        foreach (var (who, report) in new[] { ("built", built), ("saved", opened) })
        {
            // The materials read the numbers of the object they are on. The captions are only
            // cut, but wear the material of the station labels, which fade.
            Assert.Equal((true, true), (report.Material(ScenePlanBuilder.RockMaterialId).SeeThrough, report.Material(ScenePlanBuilder.RockMaterialId).Tinted));
            Assert.Equal((true, false), (report.Material("LeadMarker").SeeThrough, report.Material("LeadMarker").Tinted));
            Assert.Equal((true, false), (report.Material("LabelText").SeeThrough, report.Material("LabelText").Tinted));

            foreach (int f in Sampled)
            {
                var frame = report.Frame(f);
                string at = $"{who}, frame {f}";

                // Lead markers: hidden for half a second, then a second to appear, from frame 6 to 16.
                foreach (string id in markers)
                {
                    var marker = frame.Thing(id);
                    Assert.True(marker.Hidden == f < 6 && marker.HiddenInViewport == f < 6, $"{at}: {id} hidden {marker.Hidden}");
                    if (f >= 6) Near(Between(0, 1, (f - 6) / 10.0), marker.Fade, $"{at}: fade of {id}");
                    Near(1, marker.Opacity, $"{at}: opacity of {id}");
                    Assert.Null(marker.TintStrength);
                }
                // Their captions: cut at one second.
                foreach (string id in captions)
                    Assert.True(frame.Thing(id).Hidden == f >= 11, $"{at}: {id} hidden {frame.Thing(id).Hidden}");
                // Station labels: from frame 3 half a second to go, hidden from frame 8, back at 16.
                foreach (string id in stations)
                {
                    var label = frame.Thing(id);
                    Assert.True(label.Hidden == (f >= 8 && f < 16), $"{at}: {id} hidden {label.Hidden}");
                    if (f < 8) Near(Between(1, 0, (f - 3) / 5.0), label.Fade, $"{at}: fade of {id}");
                    if (f >= 16) Near(1, label.Fade, $"{at}: fade of {id}");
                }
                // Component labels wear the same material and change in nothing.
                foreach (string id in components)
                {
                    var label = frame.Thing(id);
                    Assert.False(label.Hidden, $"{at}: {id}");
                    Near(1, label.Fade, $"{at}: fade of {id}");
                    Near(1, label.Opacity, $"{at}: opacity of {id}");
                }

                // The cave: fading smoothly over the first second, tinted at a stroke at half a
                // second and then towards green, stretched smoothly to three times its height.
                var cave = frame.Thing(ScenePlanBuilder.ModelId);
                Assert.False(cave.Hidden, at);
                Near(1 - 0.8 * Smooth((f - 1) / 10.0), cave.Opacity, $"{at}: opacity of the cave");
                Near(1, cave.Fade, $"{at}: fade of the cave");
                double turn = Math.Clamp((f - 6) / 10.0, 0, 1);
                Near(f < 6 ? 0 : Between(0.8, 0.4, turn), cave.TintStrength, $"{at}: strength of the cave's tint");
                Near([0, turn, 1 - turn], cave.Tint, $"{at}: the cave's tint");
                Near([1, 1, 1 + 2 * Smooth((f - 1) / 19.0)], cave.Scale, $"{at}: the cave's scale");

                // Its twin wears the same material and is as it was made.
                var twin = frame.Thing("Twin");
                Near(1, twin.Opacity, $"{at}: opacity of the twin");
                Near(1, twin.Fade, $"{at}: fade of the twin");
                Near(0, twin.TintStrength, $"{at}: strength of the twin's tint");
                Near([1, 1, 1], twin.Scale, $"{at}: the twin's scale");

                // The world, and what a draft shows of it (its colour by its strength).
                double blue = Math.Clamp((f - 1) / 10.0, 0, 1);
                Near([0.2 * blue, 0.4 * blue, 0.8 * blue], frame.World!.Color, $"{at}: the world's colour");
                Near([0.1 * blue, 0.2 * blue, 0.4 * blue], frame.World.ViewportColor, $"{at}: the world's colour in a draft");
                Near(0.5, frame.World.Strength, $"{at}: the world's strength");
            }
        }
    }

    // ---- beside the pulse and the size by distance ----

    [BlenderFact]
    public async Task AMarkerThatFades_GoesOnPulsing_AndKeepsTheSizeItsDistanceGivesIt()
    {
        const int frames = 12;
        var all = new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] };
        var sizings = new[] { ("by-distance", LeadMarkerSizing.CameraDistance), ("by-the-cave", LeadMarkerSizing.Cave) };
        foreach (var (name, sizing) in sizings)
        {
            var spec = Orbit(frames, new LabelsSpec { Leads = new LeadMarkerSpec { Show = true, ShowText = true, Sizing = sizing, Pulse = true } });
            var (plan, plainJob) = PlayerRunner.WriteJob("changes-pulse-plain-" + name, Medium.Value, spec);
            var (_, fadingJob) = PlayerRunner.WriteJob("changes-pulse-fading-" + name, Medium.Value, spec, p => Changed(p,
                Of(role: ScenePlanBuilder.LeadMarkerRole) with
                {
                    Visible = [new PlanVisibleKey(0, true, 0.8)],
                    Opacity = [new PlanNumberKey(0, 1), new PlanNumberKey(1.1, 0.3)],
                },
                Of(role: ScenePlanBuilder.LeadTextRole) with { Visible = [new PlanVisibleKey(0, true, 0.5)] }));
            Assert.Null(plan.Changes);
            Assert.NotNull(plan.Labels!.Markers!.Pulse);
            Assert.Equal(sizing == LeadMarkerSizing.CameraDistance, plan.Labels.Markers.ByDistance is not null);

            var plain = await PlayerRunner.ProbeAsync(plainJob.ScriptPath, all);
            var fading = await PlayerRunner.ProbeAsync(fadingJob.ScriptPath, all);

            var markers = plan.Labels.Markers.Items.Select(m => m.Id).ToList();
            var captions = plan.Labels.Texts!.Items.Select(t => t.Id).ToList();
            Assert.True(markers.Count > 0 && captions.Count > 0, name);
            foreach (int f in all.Frames)
            {
                foreach (string id in markers.Concat(captions))
                {
                    // The same size and the same place, to the last digit the probe writes.
                    var (was, now) = (plain.Frame(f).Object(id), fading.Frame(f).Object(id));
                    string what = $"{name}, frame {f}: {id}";
                    Assert.True(Math.Abs(was.Scale - now.Scale) <= 1e-6, $"{what}: scale {now.Scale}, without the fade {was.Scale}");
                    Near(was.Origin, now.Origin, what, 1e-6);
                    Near(plain.Frame(f).Thing(id).Scale, fading.Frame(f).Thing(id).Scale, what, 1e-6);
                    Assert.False(fading.Frame(f).Thing(id).Hidden, what);
                }
                // And the fade does run meanwhile: eight frames to appear, the opacity going down.
                foreach (string id in markers)
                {
                    Near(Math.Min(1, (f - 1) / 8.0), fading.Frame(f).Thing(id).Fade, $"{name}, frame {f}: fade of {id}");
                    Near(1 - 0.7 * (f - 1) / 11.0, fading.Frame(f).Thing(id).Opacity, $"{name}, frame {f}: opacity of {id}");
                    Assert.Null(plain.Frame(f).Thing(id).Fade);
                }
            }
            // The markers do pulse (and, sized by distance, follow the camera): there is something to keep.
            Assert.All(markers, id => Assert.True(
                plain.Frames.Select(frame => Math.Round(frame.Object(id).Scale, 3)).Distinct().Count() > 3, $"{name}: {id} does not change its size"));
        }
    }

    // ---- in the picture ----

    private sealed record Picture(int Width, int Height, byte[] Rgba)
    {
        public static Picture Read(string path)
        {
            var (width, height, rgba) = Png.Decode(File.ReadAllBytes(path));
            return new Picture(width, height, rgba);
        }

        public int Pixels => Width * Height;

        public byte Alpha(int pixel) => Rgba[pixel * 4 + 3];

        /// <summary>How much of the picture is covered, from 0 to 1.</summary>
        public double Cover => Enumerable.Range(0, Pixels).Average(p => Alpha(p) / 255.0);

        /// <summary>The mean of one channel over these pixels, 0 to 255.</summary>
        public double Mean(IEnumerable<int> pixels, int channel) => pixels.Average(p => (double)Rgba[p * 4 + channel]);

        public double Brightness(int pixel) => Math.Max(Rgba[pixel * 4], Math.Max(Rgba[pixel * 4 + 1], Rgba[pixel * 4 + 2]));
    }

    /// <summary>A red cave under three suns, seen from one place for five frames at 5 fps.</summary>
    private static SceneSpec FiveFrames(bool transparent) => new()
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 16, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = transparent },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.8, 0.02, 0.02) },
        Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
        Camera = new CameraSpec { Template = CameraTemplate.Static },
        Animation = new AnimationSpec { Fps = 5, DurationSeconds = 1 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 192, Height = 108, BaseName = "f" },
    };

    private static async Task<List<Picture>> Render(string name, SceneSpec spec, Func<ScenePlan, ScenePlan> change)
    {
        var (plan, job) = PlayerRunner.WriteJob(name, Small.Value, spec, change);
        Assert.Equal(5, plan.Timeline.FrameCount);
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return Enumerable.Range(1, 5).Select(f => Picture.Read(Path.Combine(job.Folder, $"f_{f:0000}.png"))).ToList();
    }

    [BlenderFact]
    public async Task AFade_ATint_AndTheWorldsColour_ShowInThePicture()
    {
        // The cave is solid for three frames, half there on the fourth and gone on the fifth; from
        // the third frame on it is tinted blue, all the way.
        var blue = new ColorRgb(0.02, 0.02, 0.9);
        var cave = await Render("changes-picture-cave", FiveFrames(transparent: true), plan => Changed(plan,
            Of(ScenePlanBuilder.ModelId) with
            {
                Opacity = [new PlanNumberKey(0, 1), new PlanNumberKey(0.4, 1), new PlanNumberKey(0.8, 0)],
                Tint = [new PlanTintKey(0, blue, 0), new PlanTintKey(0.4, blue, 1, PlanEasing.Constant)],
            }));

        var cover = cave.Select(p => p.Cover).ToList();
        string covers = string.Join(", ", cover.Select(c => c.ToString("F4")));
        Assert.True(cover[0] > 0.02, $"the cave covers too little of the picture to tell: {covers}");
        Assert.InRange(cover[1], cover[0] * 0.98, cover[0] * 1.02);
        Assert.InRange(cover[2], cover[0] * 0.98, cover[0] * 1.02);
        // Half there: more than half of it shows, for its far side shows through its near side.
        Assert.True(cover[3] > cover[0] * 0.4 && cover[3] < cover[0] * 0.99, $"half-way through the fade the cover is {covers}");
        Assert.True(cover[4] < cover[0] * 0.01, $"the cave has not gone: {covers}");

        var solid = Enumerable.Range(0, cave[0].Pixels).Where(p => cave[0].Alpha(p) > 250 && cave[2].Alpha(p) > 250).ToList();
        Assert.True(solid.Count > 30, $"only {solid.Count} pixels are wholly the cave's");
        var (red1, blue1) = (cave[1].Mean(solid, 0), cave[1].Mean(solid, 2));
        var (red2, blue2) = (cave[2].Mean(solid, 0), cave[2].Mean(solid, 2));
        Assert.True(red1 > 2 * blue1, $"before the tint the cave is red {red1:F1}, blue {blue1:F1}");
        Assert.True(blue2 > 2 * red2, $"with the tint the cave is red {red2:F1}, blue {blue2:F1}");
        // Light and shadow fall on the tinted cave as on the red one: where one is bright, so is
        // the other.
        var before = solid.Select(p => cave[1].Brightness(p)).ToList();
        var after = solid.Select(p => cave[2].Brightness(p)).ToList();
        double spread = after.Max() - after.Min();
        Assert.True(spread > 20, $"the tinted cave is flat: its brightness spans {spread:F0} of 255");
        double correlation = Correlation(before, after);
        Assert.True(correlation > 0.9, $"the tinted cave is not shaded as the red one: correlation {correlation:F3}");

        // The world goes from black to green behind a cave that changes in nothing.
        var world = await Render("changes-picture-world", FiveFrames(transparent: false), plan => Changed(
            plan with { Look = plan.Look with { World = new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0, 0, 0), Strength = 1 } } },
            new PlanChange { WorldColor = [new PlanColorKey(0, new ColorRgb(0, 0, 0)), new PlanColorKey(0.8, new ColorRgb(0.1, 0.6, 0.1))] }));
        var sky = Enumerable.Range(0, cave[0].Pixels).Where(p => cave[0].Alpha(p) == 0).ToList();
        Assert.True(sky.Count > 100, $"only {sky.Count} pixels are background");
        var green = world.Select(p => p.Mean(sky, 1)).ToList();
        string greens = string.Join(", ", green.Select(g => g.ToString("F1")));
        Assert.True(green[0] < 3, $"the world does not start black: {greens}");
        for (int f = 1; f < 5; f++) Assert.True(green[f] > green[f - 1] + 5, $"the world does not turn green evenly: {greens}");
        Assert.True(green[4] > world[4].Mean(sky, 0) + 40, $"the world ends green {greens}, red {world[4].Mean(sky, 0):F1}");
    }

    private static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        double meanA = a.Average(), meanB = b.Average();
        double both = 0, squaresA = 0, squaresB = 0;
        for (int i = 0; i < a.Count; i++)
        {
            both += (a[i] - meanA) * (b[i] - meanB);
            squaresA += (a[i] - meanA) * (a[i] - meanA);
            squaresB += (b[i] - meanB) * (b[i] - meanB);
        }
        return both / Math.Sqrt(squaresA * squaresB);
    }

    // ---- the draft ----

    [BlenderFact]
    public async Task ADraft_ShowsAFadeAsACut_SaysSo_AndRenders()
    {
        // Station labels that appear on the third frame, taking four frames to.
        var spec = Orbit(8, EveryGroup) with { Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 64, Height = 36, BaseName = "d" } };
        var (plan, job) = PlayerRunner.WriteJob("changes-draft", Medium.Value, spec, p => ScenePlanBuilder.Draft(
            Changed(p, Of(role: ScenePlanBuilder.StationLabelRole) with { Visible = [new PlanVisibleKey(0.2, true, 0.4)] })));
        Assert.Equal(PlanEngine.Workbench, plan.Look.Engine);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [1, 2, 3, 8] });
        string log = await File.ReadAllTextAsync(Path.Combine(job.Folder, "run.log"));

        Assert.Equal("BLENDER_WORKBENCH", report.Scene.Engine);
        Assert.Single(PlayerRunner.Reported(log, "warning"), w => w == "the draft shows a fade of a text, a marker or a photo as a cut, and no change of its opacity or tint");
        var stations = report.WithRole(ProbeRoles.StationLabel);
        Assert.NotEmpty(stations);
        Assert.All(stations, o => Assert.Equal(
            [true, true, false, false], new[] { 1, 2, 3, 8 }.Select(f => report.Frame(f).Thing(o.Name).Hidden)));

        var (exitCode, rendered) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(rendered, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        Assert.Equal(8, Directory.GetFiles(job.Folder, "d-draft_*.png").Length);
    }

    // ---- refused, by name ----

    private static async Task<(string Error, string Log)> Refused(PlayerJob job, ScenePlan plan)
    {
        ScenePlanSerializer.WriteFile(plan, job.PlanPath);
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.True(exitCode == 64, $"exit code {exitCode}, expected 64; logs: {job.Folder}");
        Assert.Empty(PlayerRunner.Reported(log, "built"));
        return (Assert.Single(PlayerRunner.Reported(log, "error")), log);
    }

    [BlenderFact]
    public async Task WhatCannotBeChanged_IsRefused_ByName()
    {
        var hard = new[] { new PlanVisibleKey(0.5, true) };
        var half = new[] { new PlanNumberKey(0, 0.5) };
        var (plan, job) = PlayerRunner.WriteJob("changes-refused", Medium.Value,
            Orbit(8, new LabelsSpec { Leads = new LeadMarkerSpec { Show = true } }),
            p => Changed(p, Of(role: ScenePlanBuilder.LeadMarkerRole) with { Visible = hard }));
        string marker = plan.Labels!.Markers!.Items.Select(m => m.Id).Order(StringComparer.Ordinal).First();

        // A key this player does not know, before anything is built: what a newer plan may hold,
        // a change of a material's image for one.
        string text = File.ReadAllText(job.PlanPath);
        const string target = "\"target\": { \"role\": \"lead-marker\" },";
        Assert.Contains(target, text);
        File.WriteAllText(job.PlanPath, text.Replace(target, target + "\n      \"image\": [{ \"t\": 1, \"value\": \"map.png\" }],"));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.Equal(64, exitCode);
        Assert.Equal([$"changes[0].image is not known to player {PlayerFiles.Version}; a newer player may be needed"], PlayerRunner.Reported(log, "error"));
        Assert.Empty(PlayerRunner.Reported(log, "phase"));

        // A target that names nothing.
        var (error, _) = await Refused(job, Changed(plan, Of(role: "no-such-role") with { Visible = hard }));
        Assert.Equal("changes[0].target.role: nothing in the scene has the role \"no-such-role\"", error);
        (error, _) = await Refused(job, Changed(plan, Of("NoSuchThing") with { Visible = hard }));
        Assert.Equal("changes[0].target.id: nothing in the scene has the id \"NoSuchThing\"", error);

        // The size of a marker is its own section's.
        (error, _) = await Refused(job, Changed(plan, Of(role: ScenePlanBuilder.LeadMarkerRole) with { Scale = [new PlanVectorKey(0, new CaveVector3(2, 2, 2))] }));
        Assert.Equal($"changes[0].scale: \"{marker}\" is not a model; the size of a text or a marker is its own section's", error);

        // One property of one thing, changed twice.
        (error, _) = await Refused(job, Changed(plan, Of(role: ScenePlanBuilder.LeadMarkerRole) with { Visible = hard }, Of(marker) with { Visible = hard }));
        Assert.Equal($"changes[1].visible: changes[0] changes \"visible\" of \"{marker}\" already", error);

        // Listed before the module that gives the cave its material.
        var early = Changed(plan, Of(ScenePlanBuilder.ModelId) with { Opacity = half }) with
        {
            Features = ["models", "camera_track", ScenePlan.ChangesFeature, "look", ScenePlan.LabelsFeature, "output"],
        };
        (error, _) = await Refused(job, early);
        Assert.Equal(
            "changes[0].opacity: \"CaveModel\" has no material to change; give it one, and list \"changes\" after the modules that make materials", error);

        // A sky has no colour of its own.
        var sky = plan with { Look = plan.Look with { World = new PlanWorld { Kind = PlanWorldKind.Sky, Strength = 0.5 } } };
        (error, _) = await Refused(job, Changed(sky, new PlanChange { WorldColor = [new PlanColorKey(0, new ColorRgb(0, 0, 1))] }));
        Assert.Equal(
            "changes[0].worldColor: the world has no colour of its own to change; give look.world the kind \"color\", " +
            "and list \"changes\" after \"look\" in \"features\"", error);

        // A player from before there were changes has no such module, and says so.
        File.Delete(Path.Combine(job.Folder, PlayerFiles.PackageName, "modules", "changes.py"));
        (error, log) = await Refused(job, plan);
        Assert.Equal("the plan needs the module \"changes\", which this player does not have", error);
        Assert.Empty(PlayerRunner.Reported(log, "phase"));
    }
}
