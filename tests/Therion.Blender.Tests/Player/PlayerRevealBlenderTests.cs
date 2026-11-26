// A reveal in a real Blender, on invented caves: the threshold has the plan's value at every
// frame, the cave grows with it face by face and pixel by pixel, the edge glows and the glow
// goes, a draft shows the cut, lines and GLB models are revealed as PLY models are, the counter
// of a reveal by date shows the year the threshold has got to, and what cannot be revealed is
// refused by name. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH
// picks the Blender).

using System.Text.Json;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public partial class PlayerRevealBlenderTests
{
    /// <summary>The cheapest render there is: one sample on the CPU.</summary>
    private static readonly EngineSpec Fast = new() { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly };

    private const int Fps = 10;

    /// <summary>The orbit preset over <paramref name="frames"/> frames at 10 fps, without
    /// labels, with this reveal.</summary>
    private static SceneSpec Clip(int frames, RevealSpec? reveal) => PlanFixtures.Preset("Orbit showcase") with
    {
        Engine = Fast,
        Animation = new AnimationSpec { Fps = Fps, DurationSeconds = frames / (double)Fps },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
        Labels = new LabelsSpec(),
        Reveal = reveal,
    };

    private static void Near(double expected, double? actual, string what, double tolerance = 2e-4)
        => Assert.True(actual is { } a && Math.Abs(expected - a) <= tolerance * Math.Max(1, Math.Abs(expected)),
            $"{what}: expected {expected}, found {actual?.ToString() ?? "nothing"}");

    /// <summary>What a track of two keys gives at a frame: evenly from the first to the second.</summary>
    private static double At(IReadOnlyList<PlanNumberKey> keys, int frame)
    {
        double first = 1 + Math.Round(keys[0].T * Fps), last = 1 + Math.Round(keys[1].T * Fps);
        return keys[0].Value + (keys[1].Value - keys[0].Value) * Math.Clamp((frame - first) / (last - first), 0, 1);
    }

    /// <summary>The files a plan of the converted cave names, each once.</summary>
    private static Dictionary<string, string> Files(ProbeAssets assets, ScenePlan plan)
        => PlayerWriter.FilesOf(plan).Select(f => f.Path).Distinct().ToDictionary(path => path, path => Path.Combine(assets.Directory, path));

    private static async Task<PlayerJob> Built(ScenePlan plan, string caseName, ProbeAssets assets, params string[] warnings)
    {
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase(caseName), Files(assets, plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        Assert.Equal(warnings, PlayerRunner.Reported(log, "warning"));
        return job;
    }

    // ---- the threshold, and how much of the cave is there ----

    [BlenderFact]
    public async Task ByDistance_TheThresholdHasThePlansValueAtEveryFrame_AndTheCaveGrowsWithIt()
    {
        const int frames = 16;
        var spec = Clip(frames, new RevealSpec { By = RevealBy.Distance, Start = 0.2, End = 1.2 });
        var assets = RevealFixtures.Prepare("reveal-distance", InventedCave.Build(InventedCaveSize.Medium), spec, asLox: true);
        var plan = PlayerRunner.PlanFor(assets, spec);
        // A twin wears the cave's material and is not revealed.
        plan = ScenePlanBuilder.AddModel(
            plan, new PlanModel { Id = "Twin", Path = plan.Models[0].Path, Translation = new CaveVector3(0, 0, 500) }, plan.Look.Materials[0]);
        var change = Assert.Single(plan.Changes!);
        var job = await Built(plan, "reveal-distance", assets);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

        var material = report.Material(ScenePlanBuilder.RockMaterialId);
        Assert.Equal((true, true, true, false), (material.SeeThrough, material.Revealed, material.Glowing, material.Tinted));
        var values = assets.Conversion.Meta.Reveal!;
        Assert.Equal(("distance", 0.0), (values.By, values.Min));

        double before = -1;
        foreach (int f in Enumerable.Range(1, frames))
        {
            var cave = report.Frame(f).Thing(ScenePlanBuilder.ModelId);
            string at = $"frame {f}";
            double threshold = At(change.Reveal!, f);
            Near(threshold, cave.Reveal, $"{at}: the threshold");
            Near(change.RevealFront!.Edge, cave.RevealEdge, $"{at}: the edge");
            Near(change.RevealFront.Glow!.Width, cave.RevealGlowWidth, $"{at}: the glow's width");
            Assert.False(cave.Hidden, at);

            // What is there is what lies this side of the threshold, and never less than before.
            Assert.True(cave.Area >= before, $"{at}: the cave has not shrunk ({cave.Area} after {before})");
            if (cave.Area > 0) Assert.True(cave.LastShown < threshold, $"{at}: a face beyond the threshold ({cave.LastShown} ≥ {threshold}) is left");
            before = cave.Area!.Value;
            if (f <= 3) Assert.Equal(0, cave.Area);
            if (f >= 13) Near(cave.AreaBuilt!.Value, cave.Area, $"{at}: all of the cave is there");
            if (f is > 4 and < 12) Assert.InRange(cave.Area.Value, 0.01 * cave.AreaBuilt!.Value, 0.99 * cave.AreaBuilt.Value);

            // The twin is all there throughout.
            var twin = report.Frame(f).Thing("Twin");
            Near(1.0e18, twin.Reveal, $"{at}: the twin's threshold", 1e-6);
            Assert.Null(twin.Area);
        }
    }

    [BlenderFact]
    public async Task ByDate_AnExpeditionAppearsWhenTheThresholdPassesItsDay_FromALoxWithThe3dBesideItToo()
    {
        const int frames = 20;
        var spec = Clip(frames, new RevealSpec { By = RevealBy.Date, Start = 0, End = 1.8, Edge = 0, Glow = false, Counter = false });
        var dated = RevealFixtures.MediumByExpedition();
        double upper = RevealField.DecimalYear(RevealFixtures.UpperDay), pitch = RevealField.DecimalYear(RevealFixtures.PitchDay),
               lower = RevealField.DecimalYear(RevealFixtures.LowerDay);

        // The dates of a .3d, and those of the .3d that lies beside a .lox: the .lox has the
        // walls' distances, so its tubes differ, and its dates are the same.
        foreach (var (name, asLox) in new[] { ("reveal-date-3d", false), ("reveal-date-lox", true) })
        {
            var assets = RevealFixtures.Prepare(name, dated, spec, asLox, withDatesBeside: asLox);
            var values = assets.Conversion.Meta.Reveal!;
            Assert.Equal(("date", upper, lower, 0), (values.By, values.Min, values.Max, values.LegsWithoutDate));
            var plan = PlayerRunner.PlanFor(assets, spec);
            var change = Assert.Single(plan.Changes!);
            Assert.Null(change.RevealFront);
            var job = await Built(plan, name, assets);

            var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

            var material = report.Material(ScenePlanBuilder.RockMaterialId);
            Assert.Equal((true, true), (material.SeeThrough, material.Revealed));
            // The area of each expedition, from the steps the cave takes: it is there in three.
            var areas = Enumerable.Range(1, frames).Select(f => report.Frame(f).Thing(ScenePlanBuilder.ModelId)).ToList();
            var steps = areas.Select(a => a.Area!.Value).Distinct().ToList();
            Assert.True(steps.Count == 4 && steps[0] == 0, $"{name}: the cave is there in three steps, not {string.Join(", ", steps)}");
            Near(areas[^1].AreaBuilt!.Value, steps[3], $"{name}: all of it at the end");
            foreach (int f in Enumerable.Range(1, frames))
            {
                double threshold = At(change.Reveal!, f);
                Near(threshold, areas[f - 1].Reveal, $"{name}, frame {f}: the threshold");
                int shown = threshold > lower ? 3 : threshold > pitch ? 2 : threshold > upper ? 1 : 0;
                Assert.True(areas[f - 1].Area == steps[shown], $"{name}, frame {f}: at {threshold:F2} the cave has {shown} expeditions, area {steps[shown]}, not {areas[f - 1].Area}");
            }
        }
    }

    [BlenderFact]
    public async Task ByOrder_TheCaveIsDrawnLegByLeg()
    {
        const int frames = 18;
        var spec = Clip(frames, new RevealSpec { By = RevealBy.Order, Start = 0, End = 1.6, Edge = 0, Glow = false });
        var assets = RevealFixtures.Prepare("reveal-order", InventedCave.Build(InventedCaveSize.Small), spec, asLox: true);
        Assert.Equal(("order", 0.0, 16.0), (assets.Conversion.Meta.Reveal!.By, assets.Conversion.Meta.Reveal.Min, assets.Conversion.Meta.Reveal.Max));
        var plan = PlayerRunner.PlanFor(assets, spec);
        var job = await Built(plan, "reveal-order", assets);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

        // One leg a frame: sixteen legs in sixteen steps of the threshold. A leg's faces are
        // there as soon as the threshold is past its start.
        var legs = assets.Conversion.Geometry.Reveal!.Legs;
        double before = 0;
        foreach (int f in Enumerable.Range(1, frames))
        {
            var cave = report.Frame(f).Thing(ScenePlanBuilder.ModelId);
            double threshold = At(plan.Changes![0].Reveal!, f);
            Near(threshold, cave.Reveal, $"frame {f}: the threshold");
            if (f == 1) Assert.Equal(0, cave.Area);
            else if (f <= 17) Assert.True(cave.Area > before, $"frame {f}: one more leg is there ({cave.Area} after {before})");
            if (cave.Area > 0) Assert.True(cave.LastShown < threshold && cave.LastShown >= Math.Ceiling(threshold) - 1.0001, $"frame {f}: the last leg begun is {cave.LastShown} at {threshold}");
            before = cave.Area!.Value;
        }
        Assert.Equal(16, legs.Count);
        Near(report.Frame(frames).Thing(ScenePlanBuilder.ModelId).AreaBuilt!.Value, before, "all of the cave at the end");

        // A saved scene plays the same reveal with no Python of the player's in it: the
        // threshold is a curve, and the modifier follows it by itself.
        var saved = PlayerWriter.WriteJob(ScenePlanBuilder.SaveBlend(plan, render: false), PlayerRunner.NewCase("reveal-order-saved"), Files(assets, plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(saved.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {saved.Folder}");
        int[] sampled = [1, 4, 9, 14, 18];
        var opened = await PlayerRunner.ProbeBlendAsync(Path.Combine(saved.Folder, "scene.blend"), new ProbeOptions { Frames = sampled });
        Assert.True(opened.Material(ScenePlanBuilder.RockMaterialId).Revealed);
        foreach (int f in sampled)
        {
            var (then, now) = (report.Frame(f).Thing(ScenePlanBuilder.ModelId), opened.Frame(f).Thing(ScenePlanBuilder.ModelId));
            Near(then.Reveal!.Value, now.Reveal, $"saved, frame {f}: the threshold");
            Assert.Equal(then.Faces, now.Faces);
        }
    }

    // ---- lines, and a model from a GLB file ----

    [BlenderFact]
    public async Task ALine_IsRevealedByTheValuesOfItsPoints_AndAGlbModelByItsOwnAttribute()
    {
        const int frames = 12;
        var spec = Clip(frames, new RevealSpec { By = RevealBy.Distance, Start = 0, End = 1.0, Edge = 0.1, Glow = true });
        var assets = RevealFixtures.Prepare("reveal-line", InventedCave.Build(InventedCaveSize.Small), spec, asLox: true);
        var field = assets.Conversion.Geometry.Reveal!;
        var plan = PlayerRunner.PlanFor(assets, spec);
        var keys = plan.Changes![0].Reveal!;
        var front = plan.Changes[0].RevealFront!;

        // The centreline as a line of its own, above the cave, in a flat colour, revealed by the
        // same keys; and the cave's own mesh once more as a GLB file, when this Blender reads one.
        bool gltf = await ReadsGltf.Value;
        Therion.CaveGeometry.Writers.GlbWriter.WriteFile(assets.Conversion.Geometry.Walls, Path.Combine(assets.Directory, "walls.glb"));
        var line = RevealPlanner.Centreline(field, "Centreline", 0.4, "LineBlue") with { Translation = new CaveVector3(0, 0, 30) };
        plan = plan with
        {
            Lines = [line],
            Look = plan.Look with { Materials = [.. plan.Look.Materials, new PlanMaterial { Id = "LineBlue", Color = new ColorRgb(0.1, 0.2, 0.9) }] },
            Changes =
            [
                .. plan.Changes,
                new PlanChange { Target = new PlanChangeTarget { Role = RevealPlanner.CentrelineRole }, Reveal = keys, RevealFront = front },
            ],
        };
        if (gltf)
            plan = ScenePlanBuilder.AddModel(plan, new PlanModel { Id = "FromGlb", Path = "walls.glb", Translation = new CaveVector3(0, 0, -60) },
                    plan.Look.Materials[0]) with
                {
                    Changes = [.. plan.Changes, new PlanChange { Target = new PlanChangeTarget { Id = "FromGlb" }, Reveal = keys }],
                };
        plan = ScenePlanBuilder.WithNeededFeatures(plan);
        Assert.Equal(16, line.Strips.Count);
        var job = await Built(plan, "reveal-line", assets);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

        Assert.Equal((true, true), (report.Material("LineBlue").Revealed, report.Material("LineBlue").Glowing));
        double lineBefore = -1;
        foreach (int f in Enumerable.Range(1, frames))
        {
            double threshold = At(keys, f);
            var (shown, cave) = (report.Frame(f).Thing("Centreline"), report.Frame(f).Thing(ScenePlanBuilder.ModelId));
            Near(threshold, shown.Reveal, $"frame {f}: the line's threshold");
            Near(front.Edge, shown.RevealEdge, $"frame {f}: the line's edge");
            Assert.True(shown.Area >= lineBefore, $"frame {f}: the line has not shrunk");
            if (shown.Area > 0) Assert.True(shown.LastShown < threshold, $"frame {f}: a piece of the line beyond the threshold is left");
            lineBefore = shown.Area!.Value;
            // A piece of the line is there when the leg it runs along has begun to be.
            Assert.Equal(cave.Area > 0, shown.Area > 0);
            if (f == 1) Assert.Equal(0, shown.Area);
            if (f == frames) Near(shown.AreaBuilt!.Value, shown.Area, "all of the line at the end");
            if (f is > 2 and < 10) Assert.InRange(shown.Area.Value, 0.02 * shown.AreaBuilt!.Value, 0.98 * shown.AreaBuilt.Value);

            if (!gltf) continue;
            // The GLB has the vertices of the PLY, so it has the faces the PLY has at every frame.
            var glb = report.Frame(f).Thing("FromGlb");
            Near(threshold, glb.Reveal, $"frame {f}: the GLB model's threshold");
            Assert.Equal(cave.Faces, glb.Faces);
            Near(0, glb.RevealEdge, $"frame {f}: the GLB model has a clean cut");
        }
    }

    /// <summary>Whether the Blender under test can read glTF: its glTF add-on needs numpy, which
    /// the Blender a Linux distribution packages may not have.</summary>
    private static readonly Lazy<Task<bool>> ReadsGltf = new(async () =>
    {
        var (exitCode, _) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path, ["--python-expr", "import numpy"], Path.GetTempPath(), 120);
        return exitCode == 0;
    });

    // ---- in the picture ----

    private sealed record Picture(int Width, int Height, byte[] Rgba)
    {
        public static Picture Read(string path)
        {
            var (width, height, rgba) = Png.Decode(File.ReadAllBytes(path));
            return new Picture(width, height, rgba);
        }

        public int Pixels => Width * Height;

        /// <summary>How much of the picture is covered, from 0 to 1.</summary>
        public double Cover => Enumerable.Range(0, Pixels).Average(p => Rgba[p * 4 + 3] / 255.0);

        /// <summary>How many pixels have anything in them.</summary>
        public int Touched => Enumerable.Range(0, Pixels).Count(p => Rgba[p * 4 + 3] > 8);

        /// <summary>The pixels that are solid and mostly of one channel: it is strong, and
        /// clearly stronger than either of the others. (A glow is bright, and the view transform
        /// takes a bright colour towards white: its channel leads, but not by much.)</summary>
        public int Mostly(int channel) => Enumerable.Range(0, Pixels).Count(p =>
            Rgba[p * 4 + 3] > 128 && Rgba[p * 4 + channel] > 60
            && Enumerable.Range(0, 3).Where(c => c != channel).All(c => Rgba[p * 4 + channel] > 1.15 * Rgba[p * 4 + c]));
    }

    private const int PictureFrames = 12;

    /// <summary>A blue cave under three suns, seen from one place for twelve frames at 6 fps, on
    /// nothing: what is not the cave is see-through.</summary>
    private static SceneSpec Twelve(RevealSpec reveal) => new()
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 8, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = true },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.02, 0.02, 0.8) },
        Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
        Camera = new CameraSpec { Template = CameraTemplate.Static },
        Animation = new AnimationSpec { Fps = 6, DurationSeconds = PictureFrames / 6.0 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 192, Height = 108, BaseName = "f" },
        Reveal = reveal,
    };

    private static async Task<(List<Picture> Pictures, string Log)> Render(string name, SceneSpec spec, Func<ScenePlan, ScenePlan>? change = null)
    {
        var assets = RevealFixtures.Prepare(name, InventedCave.Build(InventedCaveSize.Small), spec, asLox: true);
        var plan = PlayerRunner.PlanFor(assets, spec);
        if (change is not null) plan = change(plan);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Equal(PictureFrames, plan.Timeline.FrameCount);
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase(name), Files(assets, plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        string baseName = plan.Output.BaseName;
        return (Enumerable.Range(1, PictureFrames).Select(f => Picture.Read(Path.Combine(job.Folder, $"{baseName}_{f:0000}.png"))).ToList(), log);
    }

    [BlenderFact]
    public async Task InThePicture_TheCaveGrowsFrameByFrame_ItsEdgeGlowsGreen_AndTheGlowGoes()
    {
        // From nothing at the first frame to all of it at the eleventh; the twelfth is the same.
        var green = new ColorRgb(0.02, 0.9, 0.02);
        var (frames, log) = await Render("reveal-picture", Twelve(new RevealSpec
        {
            By = RevealBy.Distance, Start = 0, End = 10 / 6.0, Edge = 0.02, Glow = true, GlowColor = green,
        }));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));

        var cover = frames.Select(p => p.Cover).ToList();
        string covers = string.Join(", ", cover.Select(c => c.ToString("F4")));
        Assert.True(cover[0] == 0, $"something is there at the first frame: {covers}");
        Assert.True(cover[^1] > 0.02, $"the cave covers too little of the picture to tell: {covers}");
        for (int f = 1; f <= 10; f++)
            Assert.True(cover[f] > cover[f - 1], $"the cave does not grow at frame {f + 1}: {covers}");
        Assert.InRange(cover[11], cover[10] * 0.98, cover[10] * 1.02);

        // What has just appeared is green; what has been there a while is blue; at the end
        // nothing is green.
        var greens = frames.Select(p => p.Mostly(1)).ToList();
        var blues = frames.Select(p => p.Mostly(2)).ToList();
        string colours = $"green {string.Join(", ", greens)}; blue {string.Join(", ", blues)}";
        for (int f = 1; f <= 9; f++) Assert.True(greens[f] > 0, $"no glow at frame {f + 1}: {colours}");
        Assert.True(greens[10] == 0 && greens[11] == 0, $"the glow has not gone: {colours}");
        for (int f = 3; f <= 10; f++) Assert.True(blues[f] > blues[f - 1], $"the cave behind the glow does not grow at frame {f + 1}: {colours}");
        Assert.True(blues[11] > 20 * Math.Max(1, greens[5]) / 10, $"the cave is not mostly its own colour at the end: {colours}");
    }

    [BlenderFact]
    public async Task InThePicture_ASoftEdgeIsPartlyThere_WhereACleanCutIsNot()
    {
        var (soft, _) = await Render("reveal-picture-soft", Twelve(new RevealSpec { By = RevealBy.Distance, Start = 0, End = 10 / 6.0, Edge = 0.4, Glow = false }));
        var (cut, _) = await Render("reveal-picture-cut", Twelve(new RevealSpec { By = RevealBy.Distance, Start = 0, End = 10 / 6.0, Edge = 0, Glow = false }));

        // How solid what is there is, on average: a cut is as solid half-way as at the end; an
        // edge of two fifths of the cave leaves much of what is there half there.
        double Solid(Picture p) => p.Touched == 0 ? 0 : p.Cover * p.Pixels / p.Touched;
        string solids = $"soft {string.Join(", ", soft.Select(p => Solid(p).ToString("F2")))}; cut {string.Join(", ", cut.Select(p => Solid(p).ToString("F2")))}";
        foreach (int f in new[] { 2, 3, 4, 5 })
        {
            Assert.True(Solid(soft[f]) < 0.8 * Solid(soft[11]), $"the soft edge is solid at frame {f + 1}: {solids}");
            Assert.True(Solid(cut[f]) > 0.9 * Solid(cut[11]), $"the cut is not solid at frame {f + 1}: {solids}");
        }
        // Both end with the whole cave, and the soft one gets there too.
        Assert.InRange(soft[11].Cover, cut[11].Cover * 0.98, cut[11].Cover * 1.02);
        for (int f = 1; f <= 10; f++) Assert.True(soft[f].Cover > soft[f - 1].Cover, $"the soft cave does not grow at frame {f + 1}");
    }

    [BlenderFact]
    public async Task ADraft_ShowsTheRevealAsACut_AndSaysWhatItLeavesOut()
    {
        var spec = Twelve(new RevealSpec { By = RevealBy.Distance, Start = 0, End = 10 / 6.0, Edge = 0.05, Glow = true });
        var (frames, log) = await Render("reveal-draft", spec, plan => ScenePlanBuilder.Draft(plan, scale: 1.0));
        Assert.Equal(["WORKBENCH"], PlayerRunner.Reported(log, "device"));
        Assert.Equal(
            ["the draft shows a reveal as a cut along the faces of the mesh, without its soft edge or its glow"],
            PlayerRunner.Reported(log, "warning"));

        var cover = frames.Select(p => p.Cover).ToList();
        string covers = string.Join(", ", cover.Select(c => c.ToString("F4")));
        Assert.True(cover[0] == 0, $"something is there at the first frame: {covers}");
        Assert.True(cover[^1] > 0.02, $"the cave covers too little of the picture to tell: {covers}");
        // A face is there whole as soon as the threshold reaches its first corner: the last faces
        // are there a little before the reveal ends.
        for (int f = 1; f <= 9; f++)
            Assert.True(cover[f] > cover[f - 1], $"the cave does not grow at frame {f + 1}: {covers}");
        Assert.True(cover[10] >= cover[9], $"the cave shrinks at frame 11: {covers}");
        Assert.InRange(cover[11], cover[10] * 0.98, cover[10] * 1.02);

        // A clean cut says nothing.
        var (_, quiet) = await Render("reveal-draft-cut", spec with { Reveal = spec.Reveal! with { Edge = 0, Glow = false } }, plan => ScenePlanBuilder.Draft(plan, scale: 1.0));
        Assert.Empty(PlayerRunner.Reported(quiet, "warning"));
    }

    // ---- the counter ----

    private sealed record ShownText(string Name, string Text, double Opacity);

    private sealed record StripFrame(int Frame, List<ShownText> Texts);

    private sealed record Strip(string Name, string Text, int First, int Last, int Channel);

    private sealed record StripsReport(int FrameEnd, List<Strip> Strips, List<StripFrame> Frames);

    [BlenderFact]
    public async Task TheCounterOfARevealByDate_ShowsTheYearTheThresholdHasGotTo_AtEveryFrame()
    {
        const int frames = 20;
        var spec = Clip(frames, new RevealSpec { By = RevealBy.Date, Start = 0.2, End = 1.6, Edge = 0.05, Glow = true }) with
        {
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 320, Height = 180, BaseName = "clip" },
            // An end card holds the last frame for half a second: the counter stays as it was.
            Labels = new LabelsSpec { Overlays = new OverlaySpec { EndCard = new EndCardSpec { Text = "The end", Seconds = 0.5, Backdrop = 0 } } },
        };
        var assets = RevealFixtures.Prepare("reveal-counter", RevealFixtures.MediumByExpedition(), spec);
        var plan = ScenePlanBuilder.Draft(PlayerRunner.PlanFor(assets, spec), scale: 1.0) with { Output = PlayerRunner.PlanFor(assets, spec).Output };
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var counter = Assert.Single(plan.Texts!.Items, i => i.Counter is not null);
        Assert.Equal((RevealPlanner.CounterId, "{}", 1987.0, 2003.0), (counter.Id, counter.Text, counter.Counter!.Min, counter.Counter.Max));
        var keys = Assert.Single(plan.Changes!).Reveal!;
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase("reveal-counter"), Files(assets, plan));

        // The first stage: the frames, and the threshold the scene has at each.
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--stage", "frames");
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        var scene = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });

        // The second stage: the strips of the video editor.
        string path = Path.Combine(job.Folder, "strips.json");
        int last = frames + 5;
        (exitCode, log) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path,
            ["--python", job.ScriptPath, "--python", PlayerRunner.RevealStripsScript, "--", "--stage", "compose", "--keep-frames", "--strips", path,
             "--strip-frames", string.Join(",", Enumerable.Range(1, last))],
            job.Folder, 600);
        await File.WriteAllTextAsync(Path.Combine(job.Folder, "strips.log"), log);
        Assert.True(exitCode == 0 && File.Exists(path),
            $"No strips were reported (exit code {exitCode}): {PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log)}\n  logs: {job.Folder}");
        var strips = JsonSerializer.Deserialize<StripsReport>(await File.ReadAllTextAsync(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(last, strips.FrameEnd);

        var years = new List<string>();
        foreach (int f in Enumerable.Range(1, last))
        {
            // The year the plan's keys give, the year the scene's own threshold gives, and the
            // year that is written: the same.
            int sceneFrame = Math.Min(f, frames);
            double threshold = At(keys, sceneFrame);
            string expected = Math.Clamp(Math.Floor(threshold), 1987, 2003).ToString("0");
            double inScene = scene.Frame(sceneFrame).Thing(ScenePlanBuilder.ModelId).Reveal!.Value;
            Assert.Equal(expected, Math.Clamp(Math.Floor(inScene), 1987, 2003).ToString("0"));
            var shown = Assert.Single(strips.Frames.Single(s => s.Frame == f).Texts, t => t.Name.StartsWith(RevealPlanner.CounterId + ".", StringComparison.Ordinal));
            Assert.True(shown.Text == expected, $"frame {f}: the counter says {shown.Text} when the threshold is {threshold:F3}");
            Assert.Equal(1, shown.Opacity);
            years.Add(shown.Text);
        }
        // It counts: from the first survey's year to the last one's, never back, a year at a time
        // or more, and the last frames hold the last year.
        Assert.Equal("1987", years[0]);
        Assert.Equal("2003", years[^1]);
        Assert.Equal(years.Order(StringComparer.Ordinal), years);
        Assert.True(years.Distinct().Count() >= 10, $"the counter shows only {string.Join(", ", years.Distinct())}");
        Assert.All(years.Skip(frames - 1), year => Assert.Equal("2003", year));
        // One strip for every stretch of frames a year lasts, one after the other on one channel.
        var pieces = strips.Strips.Where(s => s.Name.StartsWith(RevealPlanner.CounterId + ".", StringComparison.Ordinal)).ToList();
        Assert.Equal(years.Distinct().Count(), pieces.Count);
        Assert.Single(pieces.Select(p => p.Channel).Distinct());
        Assert.Equal(1, pieces[0].First);
        Assert.Equal(last, pieces[^1].Last);
        for (int i = 1; i < pieces.Count; i++) Assert.Equal(pieces[i - 1].Last + 1, pieces[i].First);

        // And it is in the picture: where the counter stands, the frames of the movie differ from
        // the frames of the scene, and two frames with different years differ there from each other.
        Picture Corner(string file)
        {
            var whole = Picture.Read(file);
            int x1 = whole.Width * 30 / 100, y0 = whole.Height * 70 / 100, y1 = whole.Height * 95 / 100;
            var part = new byte[x1 * (y1 - y0) * 4];
            for (int y = y0; y < y1; y++) Array.Copy(whole.Rgba, y * whole.Width * 4, part, (y - y0) * x1 * 4, x1 * 4);
            return new Picture(x1, y1 - y0, part);
        }
        static int Light(Picture p) => Enumerable.Range(0, p.Pixels).Count(i => p.Rgba[i * 4] > 200 && p.Rgba[i * 4 + 1] > 200 && p.Rgba[i * 4 + 2] > 200);
        var early = Corner(Path.Combine(job.Folder, "clip_0001.png"));
        var late = Corner(Path.Combine(job.Folder, $"clip_{frames:0000}.png"));
        Assert.Equal(0, Light(Corner(Path.Combine(job.Folder, "frames", "frame_0001.png"))));
        Assert.True(Light(early) > 40 && Light(late) > 40, $"the counter is not written: {Light(early)} and {Light(late)} light pixels");
        Assert.False(early.Rgba.AsSpan().SequenceEqual(late.Rgba), "1987 and 2003 look the same");
    }

    // ---- what cannot be revealed ----

    /// <summary>Plays a plan the library itself would not write: over the plan of a job that is
    /// there, as a program that writes its own plans might.</summary>
    private static async Task<string> Refused(PlayerJob job, ScenePlan plan)
    {
        ScenePlanSerializer.WriteFile(plan, job.PlanPath);
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.True(exitCode != 0, $"the plan was played\n  logs: {job.Folder}");
        return Assert.Single(PlayerRunner.Reported(log, "error"));
    }

    [BlenderFact]
    public async Task WhatCannotBeRevealed_IsRefusedByName()
    {
        // A cave converted without reveal values, a label, an edge without a reveal, a counter
        // of a thing nothing reveals.
        var labels = new LabelsSpec { Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 } };
        var spec = Clip(8, null) with { Labels = labels };
        var assets = RevealFixtures.Prepare("reveal-refused", InventedCave.Build(InventedCaveSize.Small), spec, asLox: true);
        Assert.Null(assets.Conversion.Meta.Reveal);
        var plan = PlayerRunner.PlanFor(assets, spec);
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase("reveal-refused"), Files(assets, plan));
        PlanNumberKey[] keys = [new(0, 0), new(0.5, 100)];
        ScenePlan With(params PlanChange[] changes) => ScenePlanBuilder.WithNeededFeatures(plan with { Changes = changes });

        Assert.Equal(
            "changes[0].reveal: \"CaveModel\" has no reveal values: its mesh has no attribute \"reveal\" (a number for every vertex)",
            await Refused(job, With(new PlanChange { Target = new PlanChangeTarget { Id = ScenePlanBuilder.ModelId }, Reveal = keys })));

        Assert.Equal(
            "changes[0].reveal: \"CaveModel\" has no reveal values: its mesh has no attribute \"years\" (a number for every vertex)",
            await Refused(job, With(new PlanChange
            {
                Target = new PlanChangeTarget { Id = ScenePlanBuilder.ModelId }, Reveal = keys, RevealFront = new PlanRevealFront { Attribute = "years" },
            })));

        string label = plan.Labels!.Texts!.Items[0].Id;
        Assert.Equal(
            $"changes[0].reveal: \"{label}\" is not a model or a line; a text or a marker is shown and hidden as a whole (\"visible\")",
            await Refused(job, With(new PlanChange { Target = new PlanChangeTarget { Id = label }, Reveal = keys })));

        Assert.Equal(
            "changes[0].revealFront: the edge of a reveal goes with the keys of the reveal (\"reveal\")",
            await Refused(job, With(new PlanChange
            {
                Target = new PlanChangeTarget { Id = ScenePlanBuilder.ModelId }, Opacity = [new PlanNumberKey(0, 1)], RevealFront = new PlanRevealFront { Edge = 2 },
            })));

        // A key the player does not know is one a newer player may have.
        ScenePlanSerializer.WriteFile(With(new PlanChange
        {
            Target = new PlanChangeTarget { Id = ScenePlanBuilder.ModelId }, Reveal = keys, RevealFront = new PlanRevealFront { Edge = 2 },
        }), job.PlanPath);
        File.WriteAllText(job.PlanPath, File.ReadAllText(job.PlanPath).Replace("\"edge\": 2", "\"edge\": 2, \"blur\": 1"));
        var (code, unknown) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.NotEqual(0, code);
        Assert.Equal(
            [$"changes[0].revealFront.blur is not known to player {PlayerFiles.Version}; a newer player may be needed"],
            PlayerRunner.Reported(unknown, "error"));

        var counted = ScenePlanBuilder.WithTexts(plan, new PlanScreenTexts
        {
            Items =
            [
                new PlanScreenItem
                {
                    Id = "year", Kind = PlanScreenItemKind.Text, Text = "{}", Size = 0.05,
                    Counter = new PlanCounter { Target = new PlanChangeTarget { Id = ScenePlanBuilder.ModelId } },
                },
            ],
        });
        Assert.Equal(
            "texts.items[0].counter.target: no change of the plan reveals this target; a counter shows the threshold of a reveal",
            await Refused(job, counted));
    }
}
