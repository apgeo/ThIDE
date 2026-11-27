// The terrain's cut-away in a real Blender, on an invented cave under an invented terrain: from
// straight above, a pixel over the cave is the cave's colour with the cut and the terrain's
// without it, in Cycles, in EEVEE and in the draft; a pixel far from the cave is the terrain's
// either way; the mesh keeps its faces; a cut that opens is shut on the first frame and open on
// the last; a saved scene carries its distances; a change of a model without one is refused. Opt-in
// like the other Blender tests (THIDE_BLENDER_SMOKE=1).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlayerCutawayBlenderTests
{
    private static readonly Lazy<ProbeAssets> Assets = new(() => SceneProbeRunner.Prepare(
        "player-cutaway", InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small), withImage: false), includeSurface: true));

    private static readonly ColorRgb Red = new(0.8, 0.02, 0.02);

    private const int Width = 160, Height = 90;

    /// <summary>The cave flat red, from straight above, under a solid terrain (or none), with
    /// or without a cut-away; frames as numbered PNGs.</summary>
    private static SceneSpec Above(RenderEngineKind engine, SurfaceMode mode, CutawaySpec? cut, int frames = 1) => new()
    {
        Engine = new EngineSpec { Kind = engine, Samples = 4, Denoise = false, Gpu = GpuMode.CpuOnly },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = Red },
        Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
        // The same view from above on every frame, ten frames a second.
        Camera = new CameraSpec { Template = CameraTemplate.StillSet, Stills = new StillSetParams { Views = Enumerable.Repeat(StandardView.Top, frames).ToList() } },
        Animation = new AnimationSpec { Fps = 10 },
        Surface = new SurfaceSpec { Mode = mode, Opacity = 1, Cutaway = cut },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = Width, Height = Height, BaseName = "view" },
    };

    private static readonly CutawaySpec Footprint = new() { Margin = 6, Edge = 1.5, Rim = 0.6 };

    private static async Task<RenderedPicture[]> Render(string name, SceneSpec spec, Func<ScenePlan, ScenePlan>? change = null)
    {
        var (plan, job) = PlayerRunner.WriteJob(name, Assets.Value, spec, change);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return Directory.GetFiles(job.Folder, "view_*.png").Order(StringComparer.Ordinal).Select(RenderedPicture.Read).ToArray();
    }

    private static (byte R, byte G, byte B, byte A) At(RenderedPicture picture, int x, int y)
    {
        int at = (y * picture.Width + x) * 4;
        return (picture.Rgba[at], picture.Rgba[at + 1], picture.Rgba[at + 2], picture.Rgba[at + 3]);
    }

    private static bool IsRed((byte R, byte G, byte B, byte A) p) => p.R > 70 && p.R > 2 * Math.Max(p.G, p.B);

    /// <summary>The terrain's muted earth colour, lit: green at least as strong as red, nothing
    /// like the cave's red.</summary>
    private static bool IsTerrain((byte R, byte G, byte B, byte A) p) => p.A > 200 && p.G >= 0.8 * p.R && p.G > 10 && !IsRed(p);

    /// <summary>The red pixel of the bare cave nearest the middle of the picture: a pixel over the
    /// cave. And the pixel farthest from any red one: a pixel far from it.</summary>
    private static ((int X, int Y) Over, (int X, int Y) Far) Pixels(RenderedPicture bare)
    {
        var red = new List<(int X, int Y)>();
        for (int y = 0; y < bare.Height; y++)
            for (int x = 0; x < bare.Width; x++)
                if (IsRed(At(bare, x, y))) red.Add((x, y));
        Assert.True(red.Count > 100, $"the bare cave is {red.Count} red pixels");
        var over = red.MinBy(p => Math.Pow(p.X - bare.Width / 2.0, 2) + Math.Pow(p.Y - bare.Height / 2.0, 2));
        var far = (from y in Enumerable.Range(0, bare.Height)
                   from x in Enumerable.Range(0, bare.Width)
                   select (X: x, Y: y)).MaxBy(p => red.Min(r => Math.Pow(r.X - p.X, 2) + Math.Pow(r.Y - p.Y, 2)));
        return (over, far);
    }

    private static int RedCount(RenderedPicture picture)
        => Enumerable.Range(0, picture.Width * picture.Height).Count(i => IsRed(At(picture, i % picture.Width, i / picture.Width)));

    public static TheoryData<string> Engines => new() { "cycles", "eevee", "draft" };

    [BlenderTheory]
    [MemberData(nameof(Engines))]
    public async Task APixelOverTheCave_IsTheCavesColour_WithTheCut_AndTheTerrainsWithout(string engine)
    {
        var kind = engine == "eevee" ? RenderEngineKind.Eevee : RenderEngineKind.Cycles;
        Func<ScenePlan, ScenePlan>? look = engine == "draft" ? p => ScenePlanBuilder.Draft(p, scale: 1) with { Output = p.Output } : null;

        var bare = (await Render($"cutaway-{engine}-bare", Above(kind, SurfaceMode.Off, null), look))[0];
        var closed = (await Render($"cutaway-{engine}-closed", Above(kind, SurfaceMode.Solid, null), look))[0];
        var cut = (await Render($"cutaway-{engine}-cut", Above(kind, SurfaceMode.Solid, Footprint), look))[0];

        var (over, far) = Pixels(bare);
        Assert.True(IsRed(At(cut, over.X, over.Y)), $"{engine}: over the cave, with the cut: {At(cut, over.X, over.Y)}");
        Assert.True(IsTerrain(At(closed, over.X, over.Y)), $"{engine}: over the cave, without the cut: {At(closed, over.X, over.Y)}");
        Assert.True(IsTerrain(At(cut, far.X, far.Y)), $"{engine}: far from the cave, with the cut: {At(cut, far.X, far.Y)}");
        Assert.True(IsTerrain(At(closed, far.X, far.Y)), $"{engine}: far from the cave, without the cut: {At(closed, far.X, far.Y)}");
        // Most of the cave shows through the hole; none of it through the terrain.
        int shown = RedCount(cut), all = RedCount(bare);
        Assert.True(shown > all * 0.6, $"{engine}: {shown} of the cave's {all} red pixels show through the cut");
        Assert.True(RedCount(closed) < all * 0.02, $"{engine}: {RedCount(closed)} red pixels show through the closed terrain");
    }

    [BlenderFact]
    public async Task TheMeshKeepsItsFaces_TheDistancesTravelInASavedScene_AndTheDraftSaysWhatItLeavesOut()
    {
        var spec = Above(RenderEngineKind.Cycles, SurfaceMode.Solid, Footprint);
        var (_, job) = PlayerRunner.WriteJob("cutaway-saved", Assets.Value, spec, p => ScenePlanBuilder.SaveBlend(p, "scene.blend", render: false));
        var (_, plainJob) = PlayerRunner.WriteJob("cutaway-plain", Assets.Value, Above(RenderEngineKind.Cycles, SurfaceMode.Solid, null));
        var options = new ProbeOptions { Samples = 1 };
        var cut = await PlayerRunner.ProbeAsync(job.ScriptPath, options);
        var plain = await PlayerRunner.ProbeAsync(plainJob.ScriptPath, options);

        var (terrain, uncut) = (cut.Object("Terrain"), plain.Object("Terrain"));
        Assert.Equal((uncut.Vertices, uncut.Polygons), (terrain.Vertices, terrain.Polygons));
        Assert.Equal(uncut.BoundsMin, terrain.BoundsMin);
        Assert.Equal(uncut.BoundsMax, terrain.BoundsMax);
        // The distances are an image inside the scene.
        var field = Assert.Single(cut.Images, i => i.Name.StartsWith("ThideCutawayField", StringComparison.Ordinal));
        Assert.True(field.Loaded, "the distances are not there");

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        string elsewhere = PlayerRunner.NewCase("cutaway-saved-moved");
        string moved = Path.Combine(elsewhere, "scene.blend");
        File.Move(Path.Combine(job.Folder, "scene.blend"), moved);
        var reopened = await PlayerRunner.ProbeBlendAsync(moved, options);
        var kept = Assert.Single(reopened.Images, i => i.Name == field.Name);
        Assert.True(kept.Loaded, "the distances did not travel with the file");
        Assert.Equal(field.Size, kept.Size);

        var (_, draft) = PlayerRunner.WriteJob("cutaway-draft-probe", Assets.Value, spec, p => ScenePlanBuilder.Draft(p));
        var drafted = await PlayerRunner.ProbeAsync(draft.ScriptPath, options);
        Assert.False(drafted.Object("Terrain").HideRender);
        string draftLog = await File.ReadAllTextAsync(Path.Combine(draft.Folder, "run.log"));
        Assert.Contains("the draft shows the cut-away of \"Terrain\" as a cut along its faces",
            Assert.Single(PlayerRunner.Reported(draftLog, "warning")));
    }

    [BlenderFact]
    public async Task ACutThatOpens_IsShutOnTheFirstFrame_AndOpenOnTheLast()
    {
        var spec = Above(RenderEngineKind.Cycles, SurfaceMode.Solid, Footprint with { OpenAt = 0.2, OpenSeconds = 0.5 }, frames: 10);
        var frames = await Render("cutaway-opening", spec);
        var bare = (await Render("cutaway-opening-bare", Above(RenderEngineKind.Cycles, SurfaceMode.Off, null)))[0];
        var (over, _) = Pixels(bare);

        Assert.Equal(10, frames.Length);
        Assert.True(IsTerrain(At(frames[0], over.X, over.Y)), $"first frame: {At(frames[0], over.X, over.Y)}");
        Assert.True(IsRed(At(frames[^1], over.X, over.Y)), $"last frame: {At(frames[^1], over.X, over.Y)}");
        // It grows: never less of the cave than the frame before.
        var counts = frames.Select(RedCount).ToList();
        for (int i = 1; i < counts.Count; i++) Assert.True(counts[i] >= counts[i - 1] - 3, $"red pixels by frame: {string.Join(", ", counts)}");
        Assert.True(counts[0] < counts[^1] / 10, $"red pixels by frame: {string.Join(", ", counts)}");
    }

    [BlenderFact]
    public async Task AChangeOfTheCutawayOfAModelWithoutOne_IsRefusedByThePlayer_ByName()
    {
        var (_, job) = PlayerRunner.WriteJob("cutaway-uncut-change", Assets.Value,
            Above(RenderEngineKind.Cycles, SurfaceMode.Solid, Footprint with { OpenSeconds = 0.2 }, frames: 2));
        // Past the validator, by hand: the material loses its cut-away, the change stays.
        string planPath = Path.Combine(job.Folder, "scene-plan.json");
        var plan = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(planPath))!;
        Assert.True(plan["texturedMaterials"]![0]!.AsObject().Remove("cutaway"));
        await File.WriteAllTextAsync(planPath, plan.ToJsonString());

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("cutaway: \"Terrain\" has no cut-away", PlayerRunner.Reported(log, "error").Single());
    }
}
