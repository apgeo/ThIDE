// Keys before the clip's first frame and three keys on one frame, a world whose backdrop is set
// apart from its light, a world's image that turns, a ramp in bands, a photo cut out of its
// background and an image laid over the picture, in a real Blender on an invented cave: by the
// numbers a curve has at every frame, and by the pixels of the picture. Opt-in like the other
// Blender tests (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public partial class PlayerChangesBlenderTests
{
    [BlenderFact]
    public async Task AClipThatBeginsInsideAMove_AndAFrameWithThreeKeys_HaveThePlansValue_AtEveryFrame()
    {
        var plan = PlayerRunner.PlanFor(Small.Value, Orbit(20));
        plan = Changed(plan with
        {
            Look = plan.Look with
            {
                Lights =
                [
                    new PlanLight { Id = "Sun", Kind = PlanLightKind.Sun, Energy = 3, Direction = new CaveVector3(0.3, 0.2, -1) },
                    new PlanLight { Id = "Headlamp", Kind = PlanLightKind.Headlamp, Energy = 80, Size = 0.3 },
                ],
            },
        },
            // The sun comes up smoothly over two seconds, and the clip begins half-way through.
            Of("Sun") with { Energy = [new PlanNumberKey(-1, 0), new PlanNumberKey(1, 4, PlanEasing.Smooth)] },
            // The lamp is turned up to 20, is 50 for one frame, and goes on from 100.
            Of("Headlamp") with
            {
                Energy =
                [
                    new PlanNumberKey(0, 10), new PlanNumberKey(0.5, 20), new PlanNumberKey(0.5, 50, PlanEasing.Constant),
                    new PlanNumberKey(0.5, 100, PlanEasing.Constant), new PlanNumberKey(1.5, 200),
                ],
            },
            Of(ScenePlanBuilder.ModelId) with
            {
                // The first frame shows the cave 5 m up; the way on starts 10 m up, smoothly.
                Offset =
                [
                    new PlanVectorKey(0, new CaveVector3(0, 0, 0)), new PlanVectorKey(0, new CaveVector3(0, 0, 5), PlanEasing.Constant),
                    new PlanVectorKey(0, new CaveVector3(0, 0, 10), PlanEasing.Constant), new PlanVectorKey(1, new CaveVector3(0, 0, 20), PlanEasing.Smooth),
                ],
                // It began to appear half a second before the clip, and takes a second.
                Visible = [new PlanVisibleKey(-0.5, true, 1)],
                Opacity = [new PlanNumberKey(-1, 0), new PlanNumberKey(1, 1)],
            });
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase("changes-keys"), PlayerRunner.MeshOf(Small.Value));

        var every = new ProbeOptions { Frames = [.. Enumerable.Range(1, 20)] };
        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, every);
        var home = plan.Models[0].Translation;
        foreach (int f in every.Frames)
        {
            var frame = report.Frame(f);
            double t = (f - 1) / 10.0;
            NearBy(4 * Smooth((t + 1) / 2), frame.Thing("Sun").Energy, $"frame {f}: the sun's energy");
            double lamp = f < 6 ? 10 + 10 * t / 0.5 : f == 6 ? 50 : Math.Min(200, 100 + 100 * (t - 0.5));
            NearBy(lamp, frame.Thing("Headlamp").Energy, $"frame {f}: the lamp's energy");
            var cave = frame.Thing(ScenePlanBuilder.ModelId);
            double up = f == 1 ? 5 : 10 + 10 * Smooth(t);
            Near([home.X, home.Y, home.Z + up], cave.Location, $"frame {f}: where the cave is", 2e-4);
            Assert.False(cave.Hidden, $"frame {f}: the cave is hidden");
            NearBy(Math.Min(1, t + 0.5), cave.Fade, $"frame {f}: how far the cave has appeared");
            NearBy(Math.Min(1, (t + 1) / 2), cave.Opacity, $"frame {f}: the cave's opacity");
        }
    }

    private static SceneSpec Flat(int frames, bool transparent = false) => new()
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 16, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = transparent },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.8, 0.8, 0.8) },
        Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
        Camera = new CameraSpec { Template = CameraTemplate.Static },
        Animation = new AnimationSpec { Fps = 5, DurationSeconds = frames / 5.0 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 320, Height = 180, BaseName = "f" },
    };

    private static async Task<List<RenderedPicture>> Pictures(string name, SceneSpec spec, Func<ScenePlan, ScenePlan> change, Dictionary<string, byte[]>? images = null)
    {
        var plan = change(PlayerRunner.PlanFor(Small.Value, spec));
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var files = PlayerRunner.MeshOf(Small.Value);
        string folder = PlayerRunner.NewCase(name + "-files");
        foreach (var (path, bytes) in images ?? [])
            files[path] = TestImages.Write(folder, Path.GetFileName(path), bytes);
        var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase(name), files);
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return Enumerable.Range(1, plan.Timeline.FrameCount)
            .Select(f => RenderedPicture.Read(Path.Combine(job.Folder, $"{plan.Output.BaseName}_{f:0000}.png"))).ToList();
    }

    /// <summary>The mean of a picture's channels, of 255, over all of it.</summary>
    private static double Brightness(RenderedPicture picture)
        => Enumerable.Range(0, picture.Width * picture.Height).Average(p => (picture.Rgba[p * 4] + picture.Rgba[p * 4 + 1] + picture.Rgba[p * 4 + 2]) / 3.0);

    private static double Brightest(RenderedPicture picture)
        => Enumerable.Range(0, picture.Width * picture.Height).Max(p => (picture.Rgba[p * 4] + picture.Rgba[p * 4 + 1] + picture.Rgba[p * 4 + 2]) / 3.0);

    [BlenderFact]
    public async Task TheWorldsBackdrop_IsSetApartFromItsLight_AndEachIsKeyedByItself()
    {
        // Nothing lights the cave but the world. First the world lights and is not seen; then it
        // is seen and lights nothing.
        var frames = await Pictures("world-backdrop", Flat(2), p => Changed(p with
        {
            Look = p.Look with { Lights = [], World = new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0.8, 0.8, 0.8), Strength = 1, Backdrop = 0 } },
        }, new PlanChange
        {
            WorldStrength = [new PlanNumberKey(0, 1), new PlanNumberKey(0.2, 0, PlanEasing.Constant)],
            WorldBackdrop = [new PlanNumberKey(0, 0), new PlanNumberKey(0.2, 1, PlanEasing.Constant)],
        }));

        // The corners are the world; the cave is in the middle.
        var corner = frames.Select(f => Brightness(f.Part(0, 0.1, 0, 0.15))).ToList();
        var cave = frames.Select(f => Brightest(f.Part(0.3, 0.7, 0.3, 0.7))).ToList();
        string numbers = $"corner {corner[0]:F0}/{corner[1]:F0}, cave at its brightest {cave[0]:F0}/{cave[1]:F0}";
        Assert.True(corner[0] < 2 && cave[0] > 80, numbers);
        Assert.True(corner[1] > 150, numbers);
        // In the second frame the cave is black before a bright world: its darkest pixel is dark.
        var darkest = Enumerable.Range(0, frames[1].Width * frames[1].Height).Min(p => (int)frames[1].Rgba[p * 4]);
        Assert.True(darkest < 3, $"{numbers}; the darkest pixel of the second frame is {darkest}");

        // A world that says nothing of a backdrop is seen as strongly as it lights, as before.
        var plain = await Pictures("world-plain", Flat(1), p => p with
        {
            Look = p.Look with { Lights = [], World = new PlanWorld { Kind = PlanWorldKind.Color, Color = new ColorRgb(0.8, 0.8, 0.8), Strength = 1 } },
        });
        Assert.InRange(Brightness(plain[0].Part(0, 0.1, 0, 0.15)), corner[1] - 2, corner[1] + 2);
        // (The cave's brightest pixel is one at its edge, where a bright world behind it shows through a little.)
        Assert.InRange(Brightest(plain[0].Part(0.3, 0.7, 0.3, 0.7)), cave[0] - 14, cave[0] + 14);
    }

    [BlenderFact]
    public async Task TheWorldsImage_TurnsByItsKeys_AsItsRotationTurnsIt()
    {
        var sky = new Dictionary<string, byte[]> { ["hdri/compass.hdr"] = TestImages.RadianceHdr(64, 32, TestImages.Compass) };
        PlanWorld World(double? rotation) => new() { Kind = PlanWorldKind.Image, Image = "hdri/compass.hdr", Strength = 1, Rotation = rotation };
        var keyed = await Pictures("world-turns", Flat(2), p => Changed(p with { Look = p.Look with { Lights = [], World = World(null) } },
            new PlanChange { WorldRotation = [new PlanNumberKey(0, 0), new PlanNumberKey(0.2, 90, PlanEasing.Constant)] }), sky);
        var still = await Pictures("world-turned", Flat(1), p => p with { Look = p.Look with { Lights = [], World = World(90) } }, sky);
        var unturned = await Pictures("world-unturned", Flat(1), p => p with { Look = p.Look with { Lights = [], World = World(null) } }, sky);

        double Difference(RenderedPicture a, RenderedPicture b) => Enumerable.Range(0, a.Rgba.Length).Average(i => Math.Abs(a.Rgba[i] - b.Rgba[i]));
        Assert.True(Difference(keyed[0], unturned[0]) < 0.5, $"the first frame is {Difference(keyed[0], unturned[0]):F2} from the image as it lies");
        Assert.True(Difference(keyed[1], still[0]) < 0.5, $"the second frame is {Difference(keyed[1], still[0]):F2} from the image turned by 90°");
        Assert.True(Difference(keyed[0], keyed[1]) > 10, $"the two frames are {Difference(keyed[0], keyed[1]):F2} apart");
    }

    [BlenderFact]
    public async Task ARampInBands_ChangesItsColourAtOnce_InARenderAndInADraft()
    {
        ColorRgb red = new(1, 0, 0), blue = new(0, 0, 1);
        var bounds = Small.Value.Framing.DrawnBounds;
        PlanMaterial Ramp(bool banded) => new()
        {
            Id = ScenePlanBuilder.RockMaterialId, Kind = PlanMaterialKind.HeightRamp, Low = bounds.Min.Z, High = bounds.Max.Z, Banded = banded, Unlit = true,
            Stops = [new PlanColorStop(0, blue), new PlanColorStop(0.5, red), new PlanColorStop(1, red)],
        };
        // Pixels that are neither the one colour nor the other: where a ramp blends.
        static int Between(RenderedPicture picture)
            => Enumerable.Range(0, picture.Width * picture.Height).Count(p => picture.Rgba[p * 4 + 3] == 255 && picture.Rgba[p * 4] > 50 && picture.Rgba[p * 4 + 2] > 50);

        var blended = (await Pictures("ramp-blended", Flat(1, transparent: true), p => p with { Look = p.Look with { Materials = [Ramp(false)] } }))[0];
        var banded = (await Pictures("ramp-banded", Flat(1, transparent: true), p => p with { Look = p.Look with { Materials = [Ramp(true)] } }))[0];
        var draft = (await Pictures("ramp-banded-draft", Flat(1, transparent: true),
            p => ScenePlanBuilder.Draft(p with { Look = p.Look with { Materials = [Ramp(true)] } }, scale: 1) with { Output = p.Output }))[0];

        string numbers = $"between the colours: {Between(blended)} blended, {Between(banded)} banded, {Between(draft)} in a draft; " +
                         $"banded red {banded.Of(red).Count}, blue {banded.Of(blue).Count}";
        Assert.True(banded.Of(red).Count > 100 && banded.Of(blue).Count > 100, numbers);
        Assert.True(Between(blended) > 150, numbers);
        // What is left between them is the one row of pixels along each edge of a band. (A draft
        // colours the vertices, so there a band ends along the faces of the mesh.)
        Assert.True(Between(banded) < Between(blended) / 3, numbers);
        Assert.True(draft.Of(red).Count > 100 && draft.Of(blue).Count > 100, numbers);
    }

    /// <summary>Green on its left half and clear on its right.</summary>
    private static byte[] HalfClearPng()
    {
        var rgba = new byte[64 * 32 * 4];
        for (int p = 0; p < 64 * 32; p++)
        {
            bool green = p % 64 < 32;
            rgba[p * 4] = (byte)(green ? 0 : 255);
            rgba[p * 4 + 1] = (byte)(green ? 255 : 0);
            rgba[p * 4 + 3] = (byte)(green ? 255 : 0);
        }
        return Png.EncodeRgba(64, 32, rgba);
    }

    [BlenderFact]
    public async Task APhotoCutOutOfItsBackground_IsNotThereWhereItsImageIsClear_AndTakesATintStill()
    {
        // A photo that fills the middle of the picture, before nothing: where it is clear the
        // picture is clear. On the second frame it is tinted blue.
        ColorRgb green = new(0, 1, 0), blue = new(0, 0, 1);
        var frames = await Pictures("photo-cut-out", Flat(2, transparent: true), p =>
        {
            var pose = p.Camera.Track[0];
            double distance = (pose.Target - pose.Eye).Length;
            var middle = pose.Eye + (pose.Target - pose.Eye) * 0.5;
            p = ScenePlanBuilder.WithNeededFeatures(p with
            {
                Models = [p.Models[0] with { Translation = new CaveVector3(0, 0, -100_000) }],   // out of the picture
                Camera = p.Camera with { ClipEnd = 500_000 },
                Annotations =
                [
                    new PlanAnnotation { Id = "cut", Kind = PlanAnnotationKind.Photo, At = middle, Image = "photos/half.png", Width = distance * 0.3 },
                ],
            });
            return Changed(p, Of("cut") with { Tint = [new PlanTintKey(0, blue, 0), new PlanTintKey(0.2, blue, 1, PlanEasing.Constant)] });
        }, new Dictionary<string, byte[]> { ["photos/half.png"] = HalfClearPng() });

        var (leftEdge, rightEdge) = frames[0].BoxOf(green);
        int solid = Enumerable.Range(0, frames[0].Width * frames[0].Height).Count(p => frames[0].Rgba[p * 4 + 3] > 128);
        string numbers = $"green from column {leftEdge} to {rightEdge}, {frames[0].Of(green).Count} green of {solid} solid pixels";
        Assert.True(frames[0].Of(green).Count > 500, numbers);
        // Nothing of the red its clear half is made of, and nothing solid beside the green half.
        Assert.Equal(0, frames[0].Of(new ColorRgb(1, 0, 0)).Count);
        Assert.InRange(solid, frames[0].Of(green).Count, frames[0].Of(green).Count * 1.1);
        Assert.True(rightEdge < frames[0].Width * 0.52, numbers);
        // The tint lies over the photo, and its clear half stays clear.
        Assert.True(frames[1].Of(blue).Count > 500 && frames[1].Of(green).Count == 0, $"tinted: {frames[1].Of(blue).Count} blue, {frames[1].Of(green).Count} green");
        Assert.True(frames[1].BoxOf(blue).Right < frames[1].Width * 0.52);
    }

    [BlenderFact]
    public async Task AnImageOverThePicture_LiesWhereThePlanPutsIt_ForItsTime_AndLetsThePictureThroughWhereItIsClear()
    {
        ColorRgb green = new(0, 1, 0);
        var info = new PlanImageInfo(PlanImageFormat.Png, 64, 32, 1);
        var spec = Flat(3) with { Output = Flat(3).Output with { Kind = OutputKind.FrameSequence } };
        var frames = await Pictures("image-inset", spec, p =>
        {
            // In the corner on the first two frames, over the whole picture on the third.
            var corner = ScreenTextPlanner.PhotoInset("corner", "photos/half.png", info, p.Look.Width, p.Look.Height, end: 0.4, fade: 0);
            var whole = ScreenTextPlanner.PhotoInset("whole", "photos/half.png", info, p.Look.Width, p.Look.Height, wholePicture: true, start: 0.4, fade: 0);
            return ScenePlanBuilder.WithTexts(ScenePlanBuilder.Draft(p, scale: 1) with { Output = p.Output }, new PlanScreenTexts { Items = [corner, whole] });
        }, new Dictionary<string, byte[]> { ["photos/half.png"] = HalfClearPng() });

        // The corner: 0.3 of the width, its right edge 5 % from the picture's and its foot 11 % up;
        // the green half is its left half.
        int w = frames[0].Width, h = frames[0].Height;
        var (left, right) = frames[0].BoxOf(green);
        int top = frames[0].TopOf(green, 0, w);
        string numbers = $"green columns {left}–{right}, top row {top} of {w}×{h}";
        Assert.InRange(left, 0.65 * w - 3, 0.65 * w + 3);
        Assert.InRange(right, 0.80 * w - 3, 0.80 * w + 3);
        Assert.InRange(top, h * (1 - 0.11) - 0.15 * w - 3, h * (1 - 0.11) - 0.15 * w + 3);
        Assert.True(frames[0].Of(new ColorRgb(1, 0, 0)).Count == 0, numbers);
        Assert.Equal(frames[0].Of(green).Count, frames[1].Of(green).Count);

        // The whole picture: as wide as the margin lets it be (its shape is 2:1 in a 16:9 picture).
        var (wholeLeft, wholeRight) = frames[2].BoxOf(green);
        Assert.InRange(wholeLeft, 0.05 * w - 3, 0.05 * w + 3);
        Assert.InRange(wholeRight, 0.5 * w - 3, 0.5 * w + 3);
        Assert.True(frames[2].Of(green).Count > 5 * frames[0].Of(green).Count);
    }
}
