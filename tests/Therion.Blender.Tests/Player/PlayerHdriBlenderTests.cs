// A world of an image (an HDRI) in a real Blender, on an invented cave under invented skies: the
// image is the background and turns with the world's rotation the way the plan says, it lights
// the scene from the side its bright part stands on and by its strength, its strength changes
// with the clip, and it travels with the plan: in a job folder, inside a single script and inside
// a saved Blender file. The images are made by the tests, as Radiance HDR and as OpenEXR.
// Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using System.Text;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlayerHdriBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("hdri-small", InventedCave.Build(InventedCaveSize.Small)));

    private static readonly ColorRgb Red = new(1, 0, 0), Green = new(0, 1, 0), Blue = new(0, 0, 1), Yellow = new(1, 1, 0);

    /// <summary>An invented sky, written beside the converted cave.</summary>
    private static string Sky(string name, byte[] image) => TestImages.Write(Path.Combine(Small.Value.Directory, "skies"), name, image);

    private static string Compass => Sky("compass.hdr", TestImages.RadianceHdr(64, 32, TestImages.Compass));

    private static string LitFromTheSouth => Sky("lit from the south.hdr", TestImages.RadianceHdr(64, 32, TestImages.LitFromTheSouth));

    /// <summary>A pale cave under an image and nothing else, seen from one place: one frame.</summary>
    private static SceneSpec OneFrame(string image, double strength = 1, double rotation = 0, bool transparent = false) => new()
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 16, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = transparent },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.8, 0.8, 0.8) },
        Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = image, Strength = strength, HdriRotation = rotation },
        Camera = new CameraSpec { Template = CameraTemplate.Static },
        Animation = new AnimationSpec { Fps = 5, DurationSeconds = 0.2 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 192, Height = 108, BaseName = "f" },
    };

    /// <summary>The plan with its camera level with the middle of the cave, a cave length and a
    /// half away, looking at it along <paramref name="along"/>. The cave is long and low: the
    /// upper corners of the picture are background.</summary>
    private static ScenePlan LookingAlong(ScenePlan plan, CaveVector3 along)
    {
        var bounds = Small.Value.Framing.LocalBounds;
        double away = 1.6 * Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));
        var eye = bounds.Center - along * (away / along.Length);
        return plan with
        {
            Camera = plan.Camera with
            {
                ClipStart = 0.1, ClipEnd = 10 * away,
                Track = [.. Enumerable.Range(0, plan.Camera.Track.Count).Select(i => plan.Camera.Track[i] with { Eye = eye, Target = bounds.Center })],
            },
        };
    }

    private static readonly CaveVector3 NorthEast = new(1, 1, 0), North = new(0, 1, 0);

    private static async Task<string> Run(string name, SceneSpec spec, CaveVector3 along, Func<ScenePlan, ScenePlan>? change = null, int frame = 1)
    {
        var (plan, job) = PlayerRunner.WriteJob(name, Small.Value, spec, p => (change ?? (q => q))(LookingAlong(p, along)));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return Path.Combine(job.Folder, $"{plan.Output.BaseName}_{frame:0000}.png");
    }

    private static async Task<RenderedPicture> Render(string name, SceneSpec spec, CaveVector3 along, Func<ScenePlan, ScenePlan>? change = null)
        => RenderedPicture.Read(await Run(name, spec, along, change));

    /// <summary>The two upper corners of a picture: background, with the cave in the middle.</summary>
    private static RenderedPicture[] Corners(RenderedPicture picture) => [picture.Part(0, 0.15, 0, 0.2), picture.Part(0.85, 1, 0, 0.2)];

    /// <summary>The share of the corners that is mostly of a colour.</summary>
    private static double ShareOf(RenderedPicture picture, ColorRgb colour)
        => Corners(picture).Sum(c => c.Of(colour).Count) / (double)Corners(picture).Sum(c => c.Width * c.Height);

    /// <summary>How bright the pixels that are there are, from 0 to 255, and how many there are.</summary>
    private static (double Mean, int Count) Brightness(RenderedPicture picture)
    {
        double sum = 0;
        int count = 0;
        for (int at = 0; at < picture.Rgba.Length; at += 4)
        {
            if (picture.Rgba[at + 3] < 250) continue;
            sum += (picture.Rgba[at] + picture.Rgba[at + 1] + picture.Rgba[at + 2]) / 3.0;
            count++;
        }
        return (count == 0 ? 0 : sum / count, count);
    }

    /// <summary>How much light the pixels that are there show, from 0 to 1: their mean, with
    /// the picture's sRGB curve taken off, so twice the light is twice the number.</summary>
    private static double Light(RenderedPicture picture)
    {
        static double Linear(byte value)
        {
            double c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        double sum = 0;
        int count = 0;
        for (int at = 0; at < picture.Rgba.Length; at += 4)
        {
            if (picture.Rgba[at + 3] < 250) continue;
            sum += (Linear(picture.Rgba[at]) + Linear(picture.Rgba[at + 1]) + Linear(picture.Rgba[at + 2])) / 3.0;
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }

    // ---- the background ----

    [BlenderFact]
    public async Task TheImage_IsTheBackground_AndTurnsAboutTheVertical_AnticlockwiseSeenFromAbove()
    {
        // The camera looks north-east. Not turned, the image has its green quarter there (north
        // to east). Turned a quarter anticlockwise, what stood from east to south (blue) stands
        // there; turned the other way, what stood from west to north (red); turned half round,
        // what stood from south to west (yellow).
        foreach (var (rotation, colour, name) in new[] { (0.0, Green, "green"), (90.0, Blue, "blue"), (-90.0, Red, "red"), (180.0, Yellow, "yellow") })
        {
            var picture = await Render($"hdri-turned-{name}", OneFrame(Compass, rotation: rotation), NorthEast);

            Assert.True(ShareOf(picture, colour) > 0.95, $"turned {rotation}: {ShareOf(picture, colour):0.00} of the corners is {name}");
        }
    }

    [BlenderFact]
    public async Task TheImage_IsAsBrightAsTheWorldIsStrong_BehindTheSceneAndOnIt()
    {
        var dim = await Render("hdri-dim", OneFrame(Compass, strength: 0.2), NorthEast);
        var bright = await Render("hdri-bright", OneFrame(Compass, strength: 1), NorthEast);

        // The background: the same colour, several times as bright.
        double dimSky = Corners(dim).Average(c => Brightness(c).Mean), brightSky = Corners(bright).Average(c => Brightness(c).Mean);
        Assert.True(brightSky > 1.5 * dimSky, $"the background: {dimSky:0.0} at 0.2 and {brightSky:0.0} at 1");
        Assert.True(ShareOf(bright, Green) > 0.95);

        // The cave itself, without the background: lit by the image alone, so as much brighter.
        var dimCave = Brightness(await Render("hdri-dim-cave", OneFrame(Compass, strength: 0.2, transparent: true), NorthEast));
        var brightCave = Brightness(await Render("hdri-bright-cave", OneFrame(Compass, strength: 1, transparent: true), NorthEast));
        Assert.True(dimCave.Count > 200 && brightCave.Count > 200, $"{dimCave.Count} and {brightCave.Count} pixels of the cave");
        Assert.True(brightCave.Mean > 1.5 * dimCave.Mean, $"the cave: {dimCave.Mean:0.0} at 0.2 and {brightCave.Mean:0.0} at 1");
        Assert.True(dimCave.Mean > 5, $"the cave is lit at 0.2: {dimCave.Mean:0.0}");
    }

    // ---- the light ----

    [BlenderFact]
    public async Task TheImage_LightsTheScene_FromTheSideItsBrightHalfStandsOn()
    {
        // The camera looks north, at the side of the cave that faces south. The image is white
        // round the south and black round the north: that side is lit. Turned half round, the
        // light comes from behind the cave, and the side the camera sees is in its own shadow.
        var front = await Render("hdri-lit", OneFrame(LitFromTheSouth, transparent: true), North);
        var behind = await Render("hdri-shaded", OneFrame(LitFromTheSouth, rotation: 180, transparent: true), North);

        var (lit, shaded) = (Brightness(front), Brightness(behind));
        Assert.True(lit.Count > 200 && Math.Abs(lit.Count - shaded.Count) < 0.05 * lit.Count, $"{lit.Count} and {shaded.Count} pixels of the cave");
        // The floors and roofs of the passages see half of the sky either way; the walls that
        // face the camera see all of the bright half or none of it. Twice the light, and more.
        Assert.True(Light(front) > 2 * Light(behind), $"light from the front {Light(front):0.000}, from behind {Light(behind):0.000}");
        Assert.True(lit.Mean > 100 && lit.Mean > shaded.Mean + 40, $"lit from the front {lit.Mean:0.0}, from behind {shaded.Mean:0.0}");
    }

    [BlenderFact]
    public async Task TheWorldsStrength_ChangesWithTheClip_UnderAnImageAsUnderAnyWorld()
    {
        // Three frames: the world lights nothing on the first and is at full strength on the last.
        var spec = OneFrame(LitFromTheSouth, transparent: true) with { Animation = new AnimationSpec { Fps = 5, DurationSeconds = 0.6 } };
        ScenePlan Dawn(ScenePlan plan) => ScenePlanBuilder.WithNeededFeatures(plan with
        {
            Changes = [new PlanChange { WorldStrength = [new PlanNumberKey(0, 0), new PlanNumberKey(0.4, 1.5)] }],
        });

        var (plan, job) = PlayerRunner.WriteJob("hdri-dawn", Small.Value, spec, p => Dawn(LookingAlong(p, North)));
        Assert.Equal(3, plan.Timeline.FrameCount);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");

        var frames = Enumerable.Range(1, 3).Select(n => Brightness(RenderedPicture.Read(Path.Combine(job.Folder, $"f_{n:0000}.png"))).Mean).ToList();
        Assert.True(frames[0] < 3, $"the first frame is dark: {frames[0]:0.0}");
        Assert.True(frames[1] > frames[0] + 20 && frames[2] > frames[1] + 10, $"the frames grow brighter: {string.Join(", ", frames.Select(f => f.ToString("0.0")))}");
    }

    // ---- the other format, and the draft ----

    [BlenderFact]
    public async Task AnOpenExrImage_IsTheBackgroundToo_AndADraftShowsAPlainGreyForIt()
    {
        string exr = Sky("compass.exr", TestImages.OpenExr(64, 32, TestImages.Compass));

        var picture = await Render("hdri-exr", OneFrame(exr, rotation: 90), NorthEast);
        Assert.True(ShareOf(picture, Blue) > 0.95, $"{ShareOf(picture, Blue):0.00} of the corners is blue");

        // The solid view draws no image: the background is a grey as strong as the world.
        var draft = await Render("hdri-exr-draft", OneFrame(exr, rotation: 90), NorthEast, plan => ScenePlanBuilder.Draft(plan, 1.0) with { Output = plan.Output });
        foreach (var corner in Corners(draft))
            for (int at = 0; at < corner.Rgba.Length; at += 4)
                Assert.True(
                    Math.Abs(corner.Rgba[at] - corner.Rgba[at + 1]) <= 2 && Math.Abs(corner.Rgba[at + 1] - corner.Rgba[at + 2]) <= 2 && corner.Rgba[at] is > 100 and < 240,
                    $"a pixel of the draft's background is ({corner.Rgba[at]}, {corner.Rgba[at + 1]}, {corner.Rgba[at + 2]})");
    }

    // ---- it travels with the plan ----

    [BlenderFact]
    public async Task TheImage_TravelsInsideASingleScript()
    {
        var assets = Small.Value;
        var spec = OneFrame(Compass, rotation: 180);
        var plan = LookingAlong(PlayerRunner.PlanFor(assets, spec), NorthEast);
        string dir = PlayerRunner.NewCase("hdri-single-file");
        string script = Path.Combine(dir, "anything.py");
        File.WriteAllText(
            script,
            PlayerWriter.SingleFile(plan, new Dictionary<string, byte[]>
            {
                ["model.ply"] = File.ReadAllBytes(assets.Conversion.Manifest.ModelPath),
                ["hdri/compass.hdr"] = File.ReadAllBytes(Compass),
            }),
            new UTF8Encoding(false));

        var (exitCode, log) = await PlayerRunner.RunAsync(script);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {dir}");
        // Nothing lies beside the script but what it rendered.
        Assert.Equal(["anything.py", "f_0001.png", "run.log"], Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(ShareOf(RenderedPicture.Read(Path.Combine(dir, "f_0001.png")), Yellow) > 0.95);
    }

    [BlenderFact]
    public async Task TheImage_TravelsInsideASavedBlenderFile_WhichRendersWithNothingBesideIt()
    {
        var (_, job) = PlayerRunner.WriteJob("hdri-blend", Small.Value, OneFrame(Compass, rotation: -90), plan =>
            ScenePlanBuilder.SaveBlend(LookingAlong(plan, NorthEast) with { Output = plan.Output with { Directory = "frames" } }, render: false));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        Assert.DoesNotContain(PlayerRunner.Reported(log, "warning"), w => w.Contains("could not be put into the Blender file", StringComparison.Ordinal));

        // Somewhere else, with nothing of the job but the file: no mesh, and no image.
        string moved = PlayerRunner.NewCase("hdri-blend-moved");
        string blend = Path.Combine(moved, "scene.blend");
        File.Copy(Path.Combine(job.Folder, "scene.blend"), blend);
        Directory.Delete(job.Folder, recursive: true);
        (exitCode, log) = await BlenderTestProcess.RunAsync(SceneProbeRunner.Blender.Path, [blend, "-f", "1"], moved, 600);
        await File.WriteAllTextAsync(Path.Combine(moved, "run.log"), log);

        Assert.True(exitCode == 0, $"exit code {exitCode}; logs: {moved}");
        var picture = RenderedPicture.Read(Path.Combine(moved, "frames", "f_0001.png"));
        // A world whose image is missing is drawn in pink; this one is the image's red.
        Assert.True(ShareOf(picture, Red) > 0.95, $"{ShareOf(picture, Red):0.00} of the corners is red");
    }

    // ---- what is no image ----

    [BlenderFact]
    public async Task AFileThatIsNoImage_IsRefusedByThePlayerItself_ByWhatItHolds()
    {
        // The writer of a job folder checks the image; a folder that comes from elsewhere, or
        // whose file was changed afterwards, is checked again by the player.
        var (plan, job) = PlayerRunner.WriteJob("hdri-not-an-image", Small.Value, OneFrame(Compass), p => LookingAlong(p, NorthEast));
        File.WriteAllBytes(Path.Combine(job.Folder, "hdri", "compass.hdr"), TestImages.FlagPng());

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(
            "look.world.image: \"hdri/compass.hdr\" is neither a Radiance HDR nor an OpenEXR image",
            PlayerRunner.Reported(log, "error").FirstOrDefault());
        Assert.False(File.Exists(Path.Combine(job.Folder, $"{plan.Output.BaseName}_0001.png")));

        // And one that is not there at all is named.
        File.Delete(Path.Combine(job.Folder, "hdri", "compass.hdr"));
        (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.NotEqual(0, exitCode);
        Assert.StartsWith("look.world.image: file not found:", PlayerRunner.Reported(log, "error").FirstOrDefault());
    }
}
