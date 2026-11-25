// What reads in a cave no light reaches, in a real Blender, by the numbers of the picture: a
// survey line whose material is unlit shows the same colour under three suns, by a head lamp 25 m
// away and by one 140 m away, where a lit line is a fraction as bright; and a head lamp that
// reaches lights what the camera looks at from 140 m as it does from 25 m, where a lamp that does
// not leaves it black. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1;
// THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlayerDimUndergroundBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("dim-small", InventedCave.Build(InventedCaveSize.Small)));

    private const double Near = 25, Far = 140;

    /// <summary>High above the cave, where nothing else is in the picture.</summary>
    private static readonly CaveVector3 Place = new(0, 0, 2000);

    private static readonly ColorRgb Orange = new(0.9, 0.35, 0.05);

    private static SceneSpec Lit(LightingRig rig, RenderEngineKind engine = RenderEngineKind.Cycles) => new()
    {
        Engine = new EngineSpec { Kind = engine, Samples = 16, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = true },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.5, 0.5, 0.5) },
        Lighting = new LightingSpec { Rig = rig },
        Camera = new CameraSpec { Template = CameraTemplate.Static },
        Animation = new AnimationSpec { Fps = 5, DurationSeconds = 0.6 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 320, Height = 180, BaseName = "f" },
    };

    /// <summary>A survey line 3 m thick, seen square on from 25 m, then from 140 m, then from
    /// 25 m again: three frames of a camera that jumps.</summary>
    private static ScenePlan WithALine(ScenePlan plan, bool unlit, bool reach)
    {
        PlanCameraKey From(double t, double distance) => new(t, Place + new CaveVector3(0, -distance, 0), Place);
        plan = ScenePlanBuilder.WithNeededFeatures(plan with
        {
            Timeline = plan.Timeline with { FrameCount = 3 },
            Camera = plan.Camera with
            {
                Interpolation = PlanInterpolation.Constant, ClipStart = 0.1, ClipEnd = 5000,
                Track = [From(0, Near), From(0.2, Far), From(0.4, Near)],
            },
            Look = plan.Look with
            {
                Materials = [.. plan.Look.Materials, new PlanMaterial { Id = "legs", Kind = PlanMaterialKind.Flat, Color = Orange, Unlit = unlit }],
            },
            Lines =
            [
                new PlanLine
                {
                    Id = "Legs", Role = "survey-lines", Width = 3, Material = "legs", Translation = Place,
                    Strips = [new PlanLineStrip([new CaveVector3(-200, 0, 0), new CaveVector3(200, 0, 0)])],
                },
            ],
        });
        return reach ? LampReach.Apply(plan) : plan;
    }

    private sealed record Line(double Red, double Green, double Blue, int Pixels);

    /// <summary>The line in a frame: the mean colour of the pixels it wholly covers, of 255.</summary>
    private static Line LineIn(string path)
    {
        var (width, height, rgba) = Png.Decode(File.ReadAllBytes(path));
        double red = 0, green = 0, blue = 0;
        int count = 0;
        for (int p = 0; p < width * height; p++)
        {
            if (rgba[p * 4 + 3] < 250) continue;   // a draft's solid pixels are a hair short of solid in some Blenders
            red += rgba[p * 4];
            green += rgba[p * 4 + 1];
            blue += rgba[p * 4 + 2];
            count++;
        }
        return count == 0 ? new Line(0, 0, 0, 0) : new Line(red / count, green / count, blue / count, count);
    }

    private static async Task<Line[]> Frames(string name, SceneSpec spec, Func<ScenePlan, ScenePlan> change)
    {
        var (plan, job) = PlayerRunner.WriteJob(name, Small.Value, spec, change);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return Enumerable.Range(1, plan.Timeline.FrameCount)
            .Select(frame => LineIn(Path.Combine(job.Folder, $"{plan.Output.BaseName}_{frame:0000}.png"))).ToArray();
    }

    [BlenderFact]
    public async Task AnUnlitLine_ReadsTheSame_UnderTheSun_ByALampNear_AndByALampFar_WhereALitLineGoesDark()
    {
        var sunlit = await Frames("dim-lit-suns", Lit(LightingRig.ThreePoint), p => WithALine(p, unlit: false, reach: false));
        var lamplit = await Frames("dim-lit-lamp", Lit(LightingRig.Headlamp), p => WithALine(p, unlit: false, reach: false));
        var unlitBySuns = await Frames("dim-unlit-suns", Lit(LightingRig.ThreePoint), p => WithALine(p, unlit: true, reach: false));
        var unlitByLamp = await Frames("dim-unlit-lamp", Lit(LightingRig.Headlamp), p => WithALine(p, unlit: true, reach: false));
        string numbers = $"lit: suns {sunlit[0].Red:F0}/{sunlit[1].Red:F0}, lamp {lamplit[0].Red:F0}/{lamplit[1].Red:F0}; " +
                         $"unlit: suns {unlitBySuns[0].Red:F0}/{unlitBySuns[1].Red:F0}, lamp {unlitByLamp[0].Red:F0}/{unlitByLamp[1].Red:F0} (red at 25 m/140 m)";

        // The line is in every picture: 3 m of it are 26 rows at 25 m and 4 at 140 m.
        Assert.All(new[] { sunlit, lamplit, unlitBySuns, unlitByLamp }, frames =>
        {
            Assert.True(frames[0].Pixels > 320 * 15, $"{frames[0].Pixels} pixels of line at 25 m");
            Assert.True(frames[1].Pixels > 320 * 2, $"{frames[1].Pixels} pixels of line at 140 m");
        });

        // The defect: under the suns a lit line reads, by the 80 W head lamp it is a sixth as
        // bright from 25 m, or less, and gone from 140 m.
        Assert.True(sunlit[0].Red > 120 && sunlit[1].Red > 120, numbers);
        Assert.True(lamplit[0].Red < sunlit[0].Red / 5, numbers);
        Assert.True(lamplit[1].Red < 3, numbers);

        // Unlit: one colour, whatever the light and however far the camera. It is the line's own
        // orange, strong in red and weak in blue.
        var all = unlitBySuns.Concat(unlitByLamp).ToList();
        Assert.All(all, line => Assert.InRange(line.Red, all[0].Red - 2.5, all[0].Red + 2.5));
        Assert.All(all, line => Assert.InRange(line.Green, all[0].Green - 2.5, all[0].Green + 2.5));
        Assert.True(all[0].Red > 170 && all[0].Blue < all[0].Red / 2, numbers + $"; blue {all[0].Blue:F0}");
        Assert.True(unlitByLamp[1].Red > 50 * Math.Max(lamplit[1].Red, 1), numbers);
    }

    [BlenderFact]
    public async Task AnUnlitLine_LightsNothingRoundIt_AndChangesAsALitOneDoes()
    {
        // A grey wall stands 2 m behind the line, in the dark: a line that glowed would light it.
        var wall = new PlanLine
        {
            Id = "Wall", Width = 60, Material = "wall", Translation = Place + new CaveVector3(0, 32, 0),
            Strips = [new PlanLineStrip([new CaveVector3(-200, 0, 0), new CaveVector3(200, 0, 0)])],
        };
        ScenePlan Changed(ScenePlan p)
        {
            p = WithALine(p, unlit: true, reach: false);
            return ScenePlanBuilder.WithNeededFeatures(p with
            {
                Look = p.Look with
                {
                    Lights = [],
                    Materials = [.. p.Look.Materials, new PlanMaterial { Id = "wall", Kind = PlanMaterialKind.Flat, Color = new ColorRgb(0.8, 0.8, 0.8) }],
                },
                Lines = [.. p.Lines!, wall],
                Changes =
                [
                    new PlanChange
                    {
                        Target = new PlanChangeTarget { Id = "Legs" },
                        // On the third frame the line is blue.
                        Tint = [new PlanTintKey(0, new ColorRgb(0, 0, 1), 0), new PlanTintKey(0.4, new ColorRgb(0, 0, 1), 1, PlanEasing.Constant)],
                    },
                ],
            });
        }
        var (plan, job) = PlayerRunner.WriteJob("dim-unlit-wall", Small.Value, Lit(LightingRig.Headlamp), Changed);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");

        var (width, height, first) = Png.Decode(File.ReadAllBytes(Path.Combine(job.Folder, "f_0001.png")));
        var (_, _, third) = Png.Decode(File.ReadAllBytes(Path.Combine(job.Folder, "f_0003.png")));
        // The line crosses the middle of the picture; the wall fills the rest and stays black.
        int At(int x, int y, int channel, byte[] rgba) => rgba[(y * width + x) * 4 + channel];
        Assert.True(At(width / 2, height / 2, 0, first) > 170, $"the line is {At(width / 2, height / 2, 0, first)} red");
        for (int y = 0; y < height; y++)
        {
            if (Math.Abs(y - height / 2) < 24) continue;   // the line itself, 37 rows, and the rows its edge is smoothed into
            Assert.True(At(width / 2, y, 0, first) <= 2 && At(width / 2, y, 3, first) == 255,
                $"row {y} of the wall is {At(width / 2, y, 0, first)} red, alpha {At(width / 2, y, 3, first)}");
        }
        // A tint lies over an unlit colour as over a lit one.
        Assert.True(At(width / 2, height / 2, 2, third) > 150 && At(width / 2, height / 2, 0, third) < 40,
            $"tinted: red {At(width / 2, height / 2, 0, third)}, blue {At(width / 2, height / 2, 2, third)}");
    }

    [BlenderFact]
    public async Task AnUnlitLine_ReadsInEeveeToo_AndInADraft()
    {
        var eevee = await Frames("dim-unlit-eevee", Lit(LightingRig.Headlamp, RenderEngineKind.Eevee), p => WithALine(p, unlit: true, reach: false));
        Assert.True(eevee[0].Red > 170 && eevee[1].Red > 170 && eevee[0].Blue < eevee[0].Red / 2,
            $"red {eevee[0].Red:F0} at 25 m, {eevee[1].Red:F0} at 140 m; blue {eevee[0].Blue:F0}");
        Assert.InRange(eevee[1].Red, eevee[0].Red - 4, eevee[0].Red + 4);

        var draft = await Frames("dim-unlit-draft", Lit(LightingRig.Headlamp),
            p => ScenePlanBuilder.Draft(WithALine(p, unlit: true, reach: false), scale: 1));
        Assert.True(draft[0].Pixels > 320 * 15 && draft[0].Red > 2 * draft[0].Blue, $"{draft[0]}");
    }

    [BlenderFact]
    public async Task ALampThatReaches_LightsFrom140m_AsFrom25m_WhereALampThatDoesNotLeavesTheLineBlack()
    {
        var plain = await Frames("dim-lamp-plain", Lit(LightingRig.Headlamp), p => WithALine(p, unlit: false, reach: false));
        var reaching = await Frames("dim-lamp-reach", Lit(LightingRig.Headlamp), p => WithALine(p, unlit: false, reach: true));
        string numbers = $"red at 25 m/140 m/25 m: plain {plain[0].Red:F0}/{plain[1].Red:F0}/{plain[2].Red:F0}, " +
                         $"reaching {reaching[0].Red:F0}/{reaching[1].Red:F0}/{reaching[2].Red:F0}";

        // The energy is keyed: 80 W by the square of the distance over 3 m, at every pose.
        var (plan, _) = PlayerRunner.WriteJob("dim-lamp-reach-plan", Small.Value, Lit(LightingRig.Headlamp), p => WithALine(p, unlit: false, reach: true));
        var energy = plan.Changes!.Single(c => c.Target?.Id == "Headlamp").Energy!;
        Assert.Equal(
            [Math.Round(80 * Near * Near / 9, 6), Math.Round(80 * Far * Far / 9, 6), Math.Round(80 * Near * Near / 9, 6)],
            energy.Select(k => k.Value));

        Assert.True(plain[0].Red < 40 && plain[1].Red < 3, numbers);
        // As bright from far as from near, and bright enough to read.
        Assert.True(reaching[0].Red > 110, numbers);
        Assert.InRange(reaching[1].Red, reaching[0].Red * 0.93, reaching[0].Red * 1.07);
        Assert.InRange(reaching[2].Red, reaching[0].Red - 3, reaching[0].Red + 3);
        Assert.True(reaching[0].Red > 4 * Math.Max(plain[0].Red, 1) && reaching[1].Red > 50 * Math.Max(plain[1].Red, 1), numbers);
    }

    [BlenderFact]
    public async Task ACaveSeenFromFar_IsDarkByItsHeadLamp_AndLitWhenTheLampReaches()
    {
        // The whole cave in one still, from where the static camera stands: a few hundred metres.
        SceneSpec Spec(bool reach) => Lit(LightingRig.Headlamp) with
        {
            Lighting = new LightingSpec { Rig = LightingRig.Headlamp, LampReach = reach },
            Animation = new AnimationSpec { Fps = 5, DurationSeconds = 0.2 },
            Output = new OutputSpec { Kind = OutputKind.Still, Width = 320, Height = 180, BaseName = "cave" },
        };
        async Task<(Line Cave, ScenePlan Plan)> Still(string name, bool reach)
        {
            var (plan, job) = PlayerRunner.WriteJob(name, Small.Value, Spec(reach));
            var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
            Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
            return (LineIn(Path.Combine(job.Folder, "cave.png")), plan);
        }

        var (dark, plain) = await Still("dim-cave-plain", reach: false);
        var (lit, reaching) = await Still("dim-cave-reach", reach: true);
        double distance = (plain.Camera.Track[0].Target - plain.Camera.Track[0].Eye).Length;

        Assert.True(distance > 30, $"the camera stands {distance:F0} m away");
        Assert.Equal(80, plain.Look.Lights[0].Energy);
        Assert.Equal(80 * distance * distance / 9, reaching.Look.Lights[0].Energy, 1e-3);
        Assert.Null(reaching.Changes);
        Assert.True(dark.Pixels > 200 && lit.Pixels > 200, $"{dark.Pixels} and {lit.Pixels} pixels of cave");
        Assert.True(dark.Red < 6, $"by the lamp as it is the cave is {dark.Red:F1} of 255 from {distance:F0} m");
        Assert.True(lit.Red > 60, $"by a lamp that reaches the cave is {lit.Red:F1} of 255 from {distance:F0} m");
    }
}
