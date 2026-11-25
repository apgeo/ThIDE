// Annotations in a real Blender, on invented caves and invented images: each kind stands where
// the plan puts it and shows in the picture when a change says so; a picture that faces the
// camera faces it all the way round an orbit; a line reaches its anchor; what is to be there
// while the camera is near is there on exactly those frames; an annotation of a model goes where
// the model goes; a draft shows a photo as a mosaic; a saved scene keeps its photos; and what an
// annotation cannot be is refused by name. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using System.Text.Json;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Picture = Therion.Blender.Tests.Player.RenderedPicture;

namespace Therion.Blender.Tests.Player;

public class PlayerAnnotationsBlenderTests
{
    /// <summary>The invented small cave, converted, with two invented pictures beside it where a
    /// plan looks for its photos.</summary>
    private static readonly Lazy<ProbeAssets> Small = new(() =>
    {
        var assets = SceneProbeRunner.Prepare("annotations-small", InventedCave.Build(InventedCaveSize.Small));
        TestImages.Write(Path.Combine(assets.Directory, "photos"), "flag.png", TestImages.FlagPng());
        TestImages.Write(Path.Combine(assets.Directory, "photos"), "flag.jpg", TestImages.FlagJpeg());
        return assets;
    });

    private const string FlagPng = "photos/flag.png", FlagJpeg = "photos/flag.jpg";

    /// <summary>A grey cave under three suns, seen from one place, for <paramref name="frames"/>
    /// frames at five a second.</summary>
    private static SceneSpec Still(int frames = 1, int samples = 16) => new()
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = samples, Denoise = false, Gpu = GpuMode.CpuOnly, TransparentBackground = true },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.5, 0.5, 0.5) },
        Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
        Camera = new CameraSpec { Template = CameraTemplate.Static },
        Animation = new AnimationSpec { Fps = 5, DurationSeconds = frames / 5.0 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 192, Height = 108, BaseName = "f" },
    };

    private static ScenePlan With(ScenePlan plan, IReadOnlyList<PlanAnnotation> annotations, params PlanChange[] changes)
        => ScenePlanBuilder.WithNeededFeatures(plan with
        {
            Annotations = annotations,
            Changes = changes.Length == 0 ? plan.Changes : [.. plan.Changes ?? [], .. changes],
        });

    // ---- what the probes say ----

    private sealed record PartsReport(IReadOnlyList<PartsFrame> Frames)
    {
        public PartsFrame Frame(int frame) => Frames.Single(f => f.Frame == frame);
    }

    private sealed record PartsFrame(int Frame, PartsCamera Camera, IReadOnlyList<Part> Parts, IReadOnlyList<Anchor> Anchors)
    {
        public Part Part(string name) => Parts.FirstOrDefault(p => p.Name == name)
            ?? throw new InvalidOperationException($"Frame {Frame} has no part '{name}'. It has: {string.Join(", ", Parts.Select(p => p.Name))}");
    }

    /// <summary>Where the camera stands, the top of its picture, and the way out of its
    /// picture, towards whoever looks at it.</summary>
    private sealed record PartsCamera(double[] Location, double[] Up, double[] Back);

    private sealed record Anchor(string Name, string Of, string? Parent, double[] Location);

    private sealed record PartImage(string Name, int[] Size, bool Packed, bool Emitted);

    private sealed record Part
    {
        public string Name { get; init; } = "";
        public string? Id { get; init; }
        public string? PartName { get; init; }
        public string? Role { get; init; }
        public string Type { get; init; } = "";
        public double[] Location { get; init; } = [];
        public double[] X { get; init; } = [];
        public double[] Y { get; init; } = [];
        public double[] Z { get; init; } = [];
        public double[] Scale { get; init; } = [];
        public bool Hidden { get; init; }
        public double? Fade { get; init; }
        public double? Opacity { get; init; }
        public bool Shadow { get; init; }
        public bool Lights { get; init; }
        public IReadOnlyList<string> Materials { get; init; } = [];
        public IReadOnlyList<string> Constraints { get; init; } = [];
        public string? Text { get; init; }
        public double? Size { get; init; }
        public string? AlignY { get; init; }
        public int? Vertices { get; init; }
        public int? Faces { get; init; }
        public double[][]? Ends { get; init; }
        public double? Width { get; init; }
        public Dictionary<string, double[]>? Corners { get; init; }
        public int? Tiles { get; init; }
        public int? FrameFaces { get; init; }
        public PartImage? Image { get; init; }
        public string? Shown { get; init; }
        public Dictionary<string, double[]>? TileColours { get; init; }
    }

    private static async Task<PartsReport> Parts(PlayerJob job, params int[] frames)
    {
        string path = Path.Combine(job.Folder, "parts.json");
        var (exitCode, log) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path,
            ["--python", job.ScriptPath, "--python", PlayerRunner.AnnotationPartsScript, "--", "--build-only", "--parts", path,
             "--part-frames", string.Join(",", frames)],
            job.Folder, 600);
        await File.WriteAllTextAsync(Path.Combine(job.Folder, "parts.log"), log);
        Assert.True(exitCode == 0 && File.Exists(path),
            $"No parts were reported (exit code {exitCode}): {PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log)}\n  logs: {job.Folder}");
        // "part" is the object's part of its annotation; the record cannot have a member of its own name.
        string json = (await File.ReadAllTextAsync(path)).Replace("\"part\":", "\"partName\":");
        return JsonSerializer.Deserialize<PartsReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static CaveVector3 V(double[] v) => new(v[0], v[1], v[2]);

    private static void Near(CaveVector3 expected, double[] actual, string what, double tolerance = 1e-4)
        => Assert.True((expected - V(actual)).Length <= tolerance, $"{what}: expected {expected}, found {V(actual)}");

    private static void Near(double expected, double actual, string what, double tolerance = 1e-4)
        => Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: expected {expected}, found {actual}");

    private static double Degrees(CaveVector3 a, CaveVector3 b)
        => Math.Acos(Math.Clamp(a.Normalized().Dot(b.Normalized()), -1, 1)) * 180 / Math.PI;

    // ---- a place in the picture ----

    /// <summary>The plane half-way between a camera that stands still and what it looks at:
    /// a place in it by its share of the picture's half width to the right of the middle and of
    /// its half height above it.</summary>
    private sealed record Stage(CaveVector3 Middle, CaveVector3 Right, CaveVector3 Up, double HalfWidth, double HalfHeight)
    {
        public static Stage Of(ScenePlan plan)
        {
            var pose = plan.Camera.Track[0];
            var forward = pose.Target - pose.Eye;
            var right = forward.Cross(new CaveVector3(0, 0, 1)).Normalized();
            var up = right.Cross(forward).Normalized();
            // The picture is as wide as the sensor is to the lens at that distance.
            double halfWidth = forward.Length * 0.5 * plan.Camera.SensorWidth / plan.Camera.Lens / 2;
            return new Stage(pose.Eye + forward * 0.5, right, up, halfWidth, halfWidth * plan.Look.Height / plan.Look.Width);
        }

        public CaveVector3 At(double u, double v) => Middle + Right * (u * HalfWidth) + Up * (v * HalfHeight);
    }

    // ---- where each kind stands ----

    [BlenderFact]
    public async Task EachKind_StandsWhereThePlanPutsIt_AndALineReachesItsAnchor()
    {
        var a = new CaveVector3(-20, 5, 3);
        var b = new CaveVector3(-10, -5, -2);
        var c = new CaveVector3(0, 0, 8);
        var d = new CaveVector3(12, 4, 6);
        var e = new CaveVector3(25, -8, -4);
        var (plan, job) = PlayerRunner.WriteJob("annotations-where", Small.Value, Still(), p => With(p,
        [
            new PlanAnnotation { Id = "name", Kind = PlanAnnotationKind.Label, At = a, Text = "Sala Mare", Size = 1.5 },
            new PlanAnnotation
            {
                Id = "note", Role = "place-note", Kind = PlanAnnotationKind.Callout, At = b, Text = "Sifon", Size = 1.2,
                Offset = new CaveVector3(3, -2, 6), LineWidth = 0.2, Color = new ColorRgb(0, 1, 0),
            },
            new PlanAnnotation { Id = "low", Kind = PlanAnnotationKind.Callout, At = b, Text = "jos", Size = 1.2, Offset = new CaveVector3(0, 4, -3) },
            new PlanAnnotation { Id = "dot", Kind = PlanAnnotationKind.Marker, At = c, Radius = 1.5, Color = new ColorRgb(1, 0.8, 0) },
            new PlanAnnotation
            {
                Id = "view", Kind = PlanAnnotationKind.Photo, At = d, Image = FlagPng, Width = 8, Offset = new CaveVector3(0, 0, 6), LineWidth = 0.15,
                Frame = new PlanPhotoFrame { Width = 0.25, Color = new ColorRgb(1, 1, 0) },
            },
            new PlanAnnotation
            {
                Id = "board", Kind = PlanAnnotationKind.Photo, At = e, Image = FlagJpeg, Width = 4, Height = 3, Facing = PlanFacing.Fixed,
                Normal = new CaveVector3(0, -2, 0), Offset = new CaveVector3(0, 0, 5), LineWidth = 0.1,
            },
        ]));
        Assert.Equal(["models", "camera_track", "look", "annotations", "output"], plan.Features);
        Assert.Empty(ScenePlanValidator.Validate(plan));

        var parts = (await Parts(job, 1)).Frame(1);
        string log = await File.ReadAllTextAsync(Path.Combine(job.Folder, "parts.log"));
        Assert.Equal(["6"], PlayerRunner.Reported(log, "annotations"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        var camera = V(parts.Camera.Location);
        Assert.Equal(
            ["board", "board.line", "dot", "low", "low.line", "name", "note", "note.line", "view", "view.line"],
            parts.Parts.Select(p => p.Name));
        // Every object of an annotation has its id and its role; none casts a shadow.
        Assert.All(parts.Parts, p => Assert.Equal(p.Name.Split('.')[0], p.Id));
        Assert.Equal(
            ["photo", "photo", "marker", "callout", "callout", "label", "place-note", "place-note", "photo", "photo"], parts.Parts.Select(p => p.Role));
        Assert.All(parts.Parts, p => Assert.False(p.Shadow, p.Name + " casts a shadow"));
        Assert.Empty(parts.Anchors);

        // A label: its text, on its anchor, turned to the camera.
        var name = parts.Part("name");
        Assert.Equal(("text", "FONT", "Sala Mare", "CENTER"), (name.PartName, name.Type, name.Text, name.AlignY));
        Near(1.5, name.Size!.Value, "the label's letters");
        Near(a, name.Location, "the label");
        Assert.True(Degrees(V(name.Z), camera - a) < 0.05, "the label does not face the camera");

        // A callout: its text stands the offset away, on the end of a line that starts at the anchor.
        var note = parts.Part("note");
        var line = parts.Part("note.line");
        Near(b + new CaveVector3(3, -2, 6), note.Location, "the callout's text");
        Assert.Equal(("text", "Sifon", "BOTTOM"), (note.PartName, note.Text, note.AlignY));
        Assert.Equal(("line", "MESH", 12, 8), (line.PartName, line.Type, line.Vertices, line.Faces));
        Near(b, line.Ends![0], "where the callout's line starts");
        Near(b + new CaveVector3(3, -2, 6), line.Ends[1], "where the callout's line ends");
        Near(0.2, line.Width!.Value, "the callout's line");
        Assert.Equal(note.Materials, line.Materials);
        // One that hangs below its anchor has its text under the end of its line, and a line a
        // twelfth of its letters thick.
        Assert.Equal("TOP", parts.Part("low").AlignY);
        Near(b, parts.Part("low.line").Ends![0], "where the lower callout's line starts");
        Near(b + new CaveVector3(0, 4, -3), parts.Part("low.line").Ends![1], "where the lower callout's line ends");
        Near(0.1, parts.Part("low.line").Width!.Value, "the lower callout's line");

        // A marker: a ball on its anchor.
        var dot = parts.Part("dot");
        Assert.Equal(("ball", "MESH"), (dot.PartName, dot.Type));
        Near(c, dot.Location, "the marker");

        // A photo that faces the camera: as wide as the plan says and as high as its image makes
        // it, its middle the offset above the anchor, parallel to the picture and upright in
        // it, with a border and its image.
        var view = parts.Part("view");
        var middle = d + new CaveVector3(0, 0, 6);
        Near(middle, view.Location, "the photo");
        Near(V(parts.Camera.Back), view.Z, "the way the photo faces");
        Near(V(parts.Camera.Up), view.Y, "the top of the photo");
        Assert.True(V(view.Z).Dot(camera - middle) > 0, "the photo shows its back");
        Assert.Contains("COPY_ROTATION", view.Constraints);
        Near(8, (V(view.Corners!["bottomRight"]) - V(view.Corners["bottomLeft"])).Length, "the photo's width");
        Near(4, (V(view.Corners["topLeft"]) - V(view.Corners["bottomLeft"])).Length, "the photo's height");
        Near(middle, [.. Enumerable.Range(0, 3).Select(i => view.Corners.Values.Average(corner => corner[i]))], "the middle of the photo's corners");
        Assert.Equal((1, 4, null), (view.Tiles, view.FrameFaces, view.Shown));
        Assert.Equal(("flag.png", true, false), (view.Image!.Name, view.Image.Emitted, view.Lights));
        Assert.Equal([64, 32], view.Image.Size);
        Assert.Equal(2, view.Materials.Count);
        // Its line starts at the anchor and ends under the picture, about where its frame's edge is.
        var viewLine = parts.Part("view.line");
        Near(d, viewLine.Ends![0], "where the photo's line starts");
        Near(d + new CaveVector3(0, 0, 6 - 0.98 * 2.25), viewLine.Ends[1], "where the photo's line ends");

        // A photo that is fixed: it faces the way the plan says, top up, with the size it is given.
        var board = parts.Part("board");
        Near(e + new CaveVector3(0, 0, 5), board.Location, "the board");
        Near(new CaveVector3(0, -1, 0), board.Z, "the way the board faces");
        Near(new CaveVector3(0, 0, 1), board.Y, "the top of the board");
        Near(new CaveVector3(1, 0, 0), board.X, "the right of the board");   // seen from in front of it
        Near(4, (V(board.Corners!["bottomRight"]) - V(board.Corners["bottomLeft"])).Length, "the board's width");
        Near(3, (V(board.Corners["topLeft"]) - V(board.Corners["bottomLeft"])).Length, "the board's height");
        Assert.Equal(("flag.jpg", 0), (board.Image!.Name, board.FrameFaces));
        Assert.Equal([32, 16], board.Image.Size);
        Assert.DoesNotContain("TRACK_TO", board.Constraints);
        // Its line comes up to its lower edge exactly: the picture does not turn.
        Near(e, parts.Part("board.line").Ends![0], "where the board's line starts");
        Near(e + new CaveVector3(0, 0, 5 - 0.98 * 1.5), parts.Part("board.line").Ends![1], "where the board's line ends");
    }

    // ---- what shows in the picture, and when ----

    private static readonly ColorRgb Red = new(1, 0, 0), Green = new(0, 1, 0), Blue = new(0, 0, 1), Magenta = new(1, 0, 1), Yellow = new(1, 1, 0);

    /// <summary>A marker at the lower left, a callout at the upper left, a label at the bottom
    /// and a photo of the flag at the right, each in a colour of its own, all in the plane
    /// half-way to what the camera looks at.</summary>
    private static ScenePlan Shown(ScenePlan plan, params PlanChange[] changes)
    {
        var stage = Stage.Of(plan);
        double h = stage.HalfHeight, w = stage.HalfWidth;
        return With(plan,
        [
            new PlanAnnotation { Id = "dot", Kind = PlanAnnotationKind.Marker, At = stage.At(-0.6, -0.5), Radius = 0.2 * h, Color = Blue, Strength = 1 },
            new PlanAnnotation
            {
                Id = "note", Kind = PlanAnnotationKind.Callout, At = stage.At(-0.6, 0.1), Text = "MWM", Size = 0.3 * h, Color = Green, Strength = 1,
                Offset = stage.Up * (0.35 * h), LineWidth = 0.05 * h,
            },
            new PlanAnnotation { Id = "name", Kind = PlanAnnotationKind.Label, At = stage.At(0.1, -0.65), Text = "WMW", Size = 0.3 * h, Color = Magenta, Strength = 1 },
            new PlanAnnotation
            {
                Id = "view", Kind = PlanAnnotationKind.Photo, At = stage.At(0.55, 0.3), Image = FlagPng, Width = 0.7 * w,
                Frame = new PlanPhotoFrame { Width = 0.04 * w, Color = Yellow },
            },
        ], changes);
    }

    private static async Task<List<Picture>> Render(string name, SceneSpec spec, Func<ScenePlan, ScenePlan> change, List<string>? warnings = null)
    {
        var (plan, job) = PlayerRunner.WriteJob(name, Small.Value, spec, change);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        warnings?.AddRange(PlayerRunner.Reported(log, "warning"));
        return [.. Enumerable.Range(1, plan.Timeline.FrameCount).Select(f => Picture.Read(Path.Combine(job.Folder, $"{plan.Output.BaseName}_{f:0000}.png")))];
    }

    [BlenderFact]
    public async Task EachKind_ShowsInThePicture_WhereItStands_AndWhenAChangeSaysSo()
    {
        var warnings = new List<string>();
        // Five frames, a fifth of a second each. The photo is cut in on the third; the callout is
        // cut out on the fourth; the label fades in from the second to the fourth; the marker stays.
        var frames = await Render("annotations-picture", Still(frames: 5), plan => Shown(plan,
            new PlanChange { Target = new PlanChangeTarget { Id = "view" }, Visible = [new PlanVisibleKey(0.4, true)] },
            new PlanChange { Target = new PlanChangeTarget { Id = "note" }, Visible = [new PlanVisibleKey(0.6, false)] },
            new PlanChange { Target = new PlanChangeTarget { Id = "name" }, Visible = [new PlanVisibleKey(0.2, true, 0.4)] }), warnings);
        Assert.Empty(warnings);
        Assert.Equal(5, frames.Count);

        for (int f = 1; f <= 5; f++)
        {
            var picture = frames[f - 1];
            Picture left = picture.Part(0, 0.5, 0, 1), right = picture.Part(0.5, 1, 0, 1);

            // The marker: a blue disc at the lower left, on every frame.
            var dot = left.Of(Blue);
            Assert.True(dot.Count > 150 && dot.Y > 0.6 && dot.X < 0.6, $"frame {f}: the marker is {dot}");

            // The callout: green at the upper left until the fourth frame, its text above its line.
            var note = left.Of(Green);
            if (f < 4)
            {
                Assert.True(note.Count > 60 && note.Y < 0.5, $"frame {f}: the callout is {note}");
                int text = picture.Part(0, 0.5, 0.1, 0.3).Of(Green).Count, lineOnly = picture.Part(0, 0.5, 0.33, 0.44).Of(Green).Count;
                Assert.True(text > 40 && lineOnly > 3 && lineOnly < text, $"frame {f}: the callout's text has {text} pixels and its line {lineOnly}");
            }
            else
            {
                Assert.True(note.Count == 0, $"frame {f}: the callout is still there: {note}");
            }

            // The photo: nothing of it before the third frame; then the flag, the right way round
            // and the right way up, in its yellow border.
            var (red, blue, green, border) = (right.Of(Red), right.Of(Blue), right.Of(Green), right.Of(Yellow));
            if (f < 3)
            {
                Assert.True(red.Count + blue.Count + green.Count + border.Count == 0, $"frame {f}: the photo is there already: {red}, {blue}, {green}, {border}");
            }
            else
            {
                Assert.True(red.Count > 250 && blue.Count > 250 && green.Count > 150, $"frame {f}: the photo's colours are {red}, {blue}, {green}");
                Assert.True(red.X < blue.X - 0.2, $"frame {f}: red is not left of blue: {red.X:F2} and {blue.X:F2}");
                Assert.True(green.Y < red.Y - 0.1 && green.Y < blue.Y - 0.1, $"frame {f}: green is not above: {green.Y:F2}, {red.Y:F2}, {blue.Y:F2}");
                Assert.InRange(red.Count / (double)blue.Count, 0.8, 1.25);
                Assert.True(border.Count > 80, $"frame {f}: the border has {border.Count} pixels");
                // Seen as the rectangle it is: its green band is as high at its left end as at its right.
                var band = right.BoxOf(Green);
                int width = band.Right - band.Left;
                int topLeft = right.TopOf(Green, band.Left, band.Left + width / 4);
                int topRight = right.TopOf(Green, band.Right - width / 4, band.Right);
                Assert.True(width > 50 && Math.Abs(topLeft - topRight) <= 1, $"frame {f}: the flag is {width} pixels wide and its top is at {topLeft} on the left and {topRight} on the right");
            }
        }

        // The label: not there, then more and more of it, then all of it.
        double[] label = [.. frames.Select(picture => picture.Part(0.3, 0.8, 0.6, 1).AmountOf(Magenta))];
        string amounts = string.Join(", ", label.Select(amount => amount.ToString("F0")));
        Assert.True(label[0] == 0 && label[1] < 0.05 * label[4], $"the label is there before it appears: {amounts}");
        Assert.True(label[2] > 0.25 * label[4] && label[2] < 0.75 * label[4], $"the label is not half there on the third frame: {amounts}");
        Assert.True(label[4] > 5000 && Math.Abs(label[3] - label[4]) < 0.05 * label[4], $"the label is not all there from the fourth frame: {amounts}");
    }

    [BlenderFact]
    public async Task ADraft_ShowsAPhotoAsAMosaicOfItsColours_AndSaysWhatItCannotShow()
    {
        static ScenePlan Draft(ScenePlan plan) => ScenePlanBuilder.Draft(plan, scale: 1) with { Output = plan.Output };
        var warnings = new List<string>();
        var picture = Assert.Single(await Render("annotations-draft", Still(), plan => Draft(Shown(plan,
            new PlanChange { Target = new PlanChangeTarget { Id = "name" }, Visible = [new PlanVisibleKey(0, false, 0.2)] })), warnings));

        Assert.Equal(
            [
                "the draft shows a photo as a mosaic of its colours, 48 tiles along its longer side",
                "the draft shows a fade of a text, a marker or a photo as a cut, and no change of its opacity or tint",
            ], warnings);
        Picture left = picture.Part(0, 0.5, 0, 1), right = picture.Part(0.5, 1, 0, 1);
        var (red, blue, green) = (right.Of(Red), right.Of(Blue), right.Of(Green));
        Assert.True(red.Count > 250 && blue.Count > 250 && green.Count > 150, $"the photo's colours are {red}, {blue}, {green}");
        Assert.True(red.X < blue.X - 0.2 && green.Y < red.Y - 0.1, $"the flag is not the right way round: {red}, {blue}, {green}");
        Assert.True(right.Of(Yellow).Count > 80, "the border is not there");
        // The texts and the marker are there in their own colours.
        Assert.True(left.Of(Blue).Count > 150 && left.Of(Green).Count > 60, $"the marker and the callout are {left.Of(Blue)} and {left.Of(Green)}");

        // The mesh itself: 48 tiles by 24, each in the colour of its part of the image.
        var (_, job) = PlayerRunner.WriteJob("annotations-draft-parts", Small.Value, Still(), plan => Draft(Shown(plan)));
        var view = (await Parts(job, 1)).Frame(1).Part("view");
        Assert.Equal((48 * 24, 4, "ThideDraft"), (view.Tiles, view.FrameFaces, view.Shown));
        Near(new CaveVector3(1, 0, 0), view.TileColours!["left"], "the left of the mosaic", 2e-3);
        Near(new CaveVector3(0, 0, 1), view.TileColours["right"], "the right of the mosaic", 2e-3);
        Near(new CaveVector3(0, 1, 0), view.TileColours["top"], "the top of the mosaic", 2e-3);
        Near(new CaveVector3(0, 1, 0), view.TileColours["topLeft"], "the top left of the mosaic", 2e-3);
        Near(new CaveVector3(0, 0, 1), view.TileColours["bottomRight"], "the bottom right of the mosaic", 2e-3);
    }

    // ---- facing the camera ----

    [BlenderFact]
    public async Task APictureThatFacesTheCamera_FacesItAllTheWayRound_AndAFixedOneStaysAsItWasPut()
    {
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 1.2 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
            Labels = new LabelsSpec(),
        };
        var above = new CaveVector3(6, -4, 12);
        var slanted = new CaveVector3(1, -2, 0.5);
        var (plan, job) = PlayerRunner.WriteJob("annotations-facing", Small.Value, spec, p => With(p,
        [
            new PlanAnnotation { Id = "view", Kind = PlanAnnotationKind.Photo, At = above, Image = FlagPng, Width = 6 },
            new PlanAnnotation { Id = "name", Kind = PlanAnnotationKind.Label, At = new CaveVector3(-15, 10, -3), Text = "Sala Mare", Size = 2 },
            new PlanAnnotation
            {
                Id = "board", Kind = PlanAnnotationKind.Photo, At = new CaveVector3(0, 0, -10), Image = FlagJpeg, Width = 4, Facing = PlanFacing.Fixed,
                Normal = slanted, Up = new CaveVector3(0, 0, 3),
            },
            new PlanAnnotation
            {
                Id = "floor", Kind = PlanAnnotationKind.Photo, At = new CaveVector3(0, 0, -20), Image = FlagJpeg, Width = 4, Facing = PlanFacing.Fixed,
                Normal = new CaveVector3(0, 0, 1),
            },
        ]));
        int[] frames = [1, 3, 5, 7, 9, 12];

        var report = await Parts(job, frames);

        var directions = new List<CaveVector3>();
        foreach (int f in frames)
        {
            var at = report.Frame(f);
            var camera = V(at.Camera.Location);
            var view = at.Part("view");
            // It is parallel to the picture, with the picture's top as its own: seen as the
            // rectangle it is. And it shows the camera its front.
            double off = Degrees(V(view.Z), V(at.Camera.Back)), roll = Degrees(V(view.Y), V(at.Camera.Up));
            Assert.True(off < 0.05 && roll < 0.05, $"frame {f}: the photo is {off:F3} degrees off the picture's plane and rolled {roll:F3} degrees in it");
            Assert.True(Degrees(V(view.Z), camera - above) < 45, $"frame {f}: the photo looks {Degrees(V(view.Z), camera - above):F1} degrees past the camera");
            Assert.True(V(view.Y).Z > 0.5, $"frame {f}: the photo is not upright: its top points to {V(view.Y)}");
            Assert.True(Degrees(V(view.X), V(view.Y)) is > 89.99 and < 90.01, $"frame {f}: the photo is sheared");
            Near(above, view.Location, $"frame {f}: where the photo stands");
            directions.Add(V(view.Z));
            double text = Degrees(V(at.Part("name").Z), camera - new CaveVector3(-15, 10, -3));
            Assert.True(text < 0.05, $"frame {f}: the label looks {text:F3} degrees past the camera");

            // The fixed ones do not turn: the way they face, and their tops.
            var board = at.Part("board");
            Assert.True(Degrees(V(board.Z), slanted) < 0.01, $"frame {f}: the board faces {V(board.Z)}");
            Assert.True(V(board.Y).Z > 0.9 && Math.Abs(V(board.Y).Dot(slanted)) < 1e-5, $"frame {f}: the board's top points to {V(board.Y)}");
            Near(new CaveVector3(0, 0, 1), at.Part("floor").Z, $"frame {f}: the way the picture on the floor faces");
            Near(new CaveVector3(0, 1, 0), at.Part("floor").Y, $"frame {f}: the top of the picture on the floor");
        }
        // The camera did go round: the photo turned through most of a circle to follow it.
        double turned = Enumerable.Range(1, directions.Count - 1).Sum(i => Degrees(directions[i - 1], directions[i]));
        Assert.True(turned > 200, $"the photo turned only {turned:F0} degrees");
        Assert.Equal(12, plan.Timeline.FrameCount);
    }

    // ---- while the camera is near ----

    /// <summary>A camera that flies a straight, level line through the cave's middle, a hundred
    /// metres in four seconds, looking ahead; forty-one frames.</summary>
    private static ScenePlan FlyingPast(ScenePlan plan)
    {
        var from = new CaveVector3(-50, 0, 2);
        var to = new CaveVector3(50, 0, 2);
        return plan with
        {
            Timeline = new PlanTimeline { Fps = 10, FrameCount = 41 },
            Camera = plan.Camera with
            {
                ClipStart = 0.1, ClipEnd = 1000, Interpolation = PlanInterpolation.Linear,
                Track = [new PlanCameraKey(0, from, from + new CaveVector3(100, 0, 0)), new PlanCameraKey(4, to, to + new CaveVector3(100, 0, 0))],
            },
        };
    }

    [BlenderFact]
    public async Task WhatIsThereWhileTheCameraIsNear_IsThereOnExactlyThoseFrames_AndFadesInAndOut()
    {
        var anchor = new CaveVector3(3, 30, 2);
        var (plan, job) = PlayerRunner.WriteJob("annotations-near", Small.Value, Still() with { Output = Still().Output with { Kind = OutputKind.Video } }, p =>
        {
            p = FlyingPast(p);
            // The same annotations as a spec has them, lowered for this camera.
            var lowered = AnnotationPlanner.Plan(
            [
                new AnnotationSpec { Id = "near", Kind = AnnotationKind.Marker, At = anchor, Size = 1, Within = 45, Fade = 0.5 },
                new AnnotationSpec { Id = "nearer", Kind = AnnotationKind.Photo, At = anchor, Image = Path.Combine(Small.Value.Directory, "photos", "flag.png"), Size = 4, Within = 35, Fade = 0 },
                new AnnotationSpec { Id = "never", Kind = AnnotationKind.Label, At = anchor, Text = "far", Within = 5 },
                new AnnotationSpec { Id = "always", Kind = AnnotationKind.Label, At = anchor, Text = "here", Within = 500 },
            ], meta: null, Small.Value.Framing.LocalBounds, p.Camera, p.Timeline);
            Assert.Equal(["the camera never comes within 5 m of the annotation 'never'; it is not shown"], lowered.Notices);
            return With(p, lowered.Annotations, [.. lowered.Changes]);
        });
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Equal(["near", "nearer", "never"], plan.Changes!.Select(c => c.Target!.Id));

        int[] frames = [.. Enumerable.Range(1, 41)];
        var report = await Parts(job, frames);

        var shown = new Dictionary<string, List<int>> { ["near"] = [], ["nearer"] = [] };
        foreach (int f in frames)
        {
            var at = report.Frame(f);
            // The distance is the one Blender has for its own camera on this frame.
            double distance = (V(at.Camera.Location) - anchor).Length;
            foreach (var (id, within) in new[] { ("near", 45.0), ("nearer", 35.0) })
            {
                var part = at.Part(id);
                Assert.True(part.Hidden == distance > within, $"frame {f}: '{id}' is {(part.Hidden ? "hidden" : "shown")} with the camera {distance:F2} m away");
                if (!part.Hidden) shown[id].Add(f);
            }
            Assert.True(at.Part("never").Hidden, $"frame {f}: 'never' is shown");
            Assert.False(at.Part("always").Hidden, $"frame {f}: 'always' is hidden");
        }
        // The camera moves 2.5 m a frame, and is within 45 m of the anchor where it is within
        // √(45² − 30²) = 33.5 m of the anchor's foot on its path: from x = −30 to x = 35.
        Assert.Equal((9, 35), (shown["near"][0], shown["near"][^1]));
        Assert.Equal((15, 29), (shown["nearer"][0], shown["nearer"][^1]));
        Assert.Equal(27, shown["near"].Count);

        // Half a second to appear from the frame it comes near, and half a second to have gone by
        // the frame it is far again: five frames each.
        double Fade(int f) => report.Frame(f).Part("near").Fade!.Value;
        Assert.Equal([0, 0.2, 0.4, 0.6, 0.8, 1], new[] { 9, 10, 11, 12, 13, 14 }.Select(f => Math.Round(Fade(f), 4)));
        Assert.All(Enumerable.Range(14, 18), f => Near(1, Fade(f), $"frame {f}: how much of 'near' is there"));
        Assert.Equal([1, 0.8, 0.6, 0.4, 0.2], new[] { 31, 32, 33, 34, 35 }.Select(f => Math.Round(Fade(f), 4)));
        // One that does not fade has no such number: it is cut in and out.
        Assert.Null(report.Frame(20).Part("nearer").Fade);
    }

    [BlenderFact]
    public async Task ASpecsAnnotationOfAStation_ShownWhileAnOrbitingCameraIsNear_IsThereWhenBlendersCameraIs()
    {
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 3 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
            Labels = new LabelsSpec(),
        };
        // The entrance lies off the cave's middle: an orbiting camera is nearer to it on one side.
        var station = Small.Value.Conversion.Meta.Stations.Single(s => s.Name == "main0");
        var at = new CaveVector3(station.Position.X, station.Position.Y, station.Position.Z);
        var orbit = PlayerRunner.PlanFor(Small.Value, spec);
        var distances = Enumerable.Range(1, 30).Select(f => (AnnotationPlanner.EyeAt(orbit.Camera, f, 10) - at).Length).ToList();
        double within = Math.Round((distances.Min() + distances.Max()) / 2, 1);
        Assert.True(distances.Max() - distances.Min() > 20, $"the camera keeps {distances.Min():F0} to {distances.Max():F0} m from the entrance");

        var annotated = spec with
        {
            Annotations = [new AnnotationSpec { Id = "way-in", Kind = AnnotationKind.Callout, Station = "main0", Text = "Intrarea", Within = within, Fade = 0.3 }],
        };
        var (plan, job) = PlayerRunner.WriteJob("annotations-station", Small.Value, annotated);
        Assert.Equal(["models", "camera_track", "look", "annotations", "changes", "output"], plan.Features);
        Near(at, [plan.Annotations![0].At.X, plan.Annotations[0].At.Y, plan.Annotations[0].At.Z], "the station's place in the plan", 1e-5);

        int[] frames = [.. Enumerable.Range(1, 30)];
        var report = await Parts(job, frames);

        int seen = 0, unseen = 0;
        foreach (int f in frames)
        {
            var frame = report.Frame(f);
            double distance = (V(frame.Camera.Location) - at).Length;
            var text = frame.Part("way-in");
            Assert.True(text.Hidden == distance > within, $"frame {f}: the callout is {(text.Hidden ? "hidden" : "shown")} with the camera {distance:F2} m away, of {within}");
            Assert.Equal(text.Hidden, frame.Part("way-in.line").Hidden);
            Near(at, frame.Part("way-in.line").Ends![0], $"frame {f}: where the line starts");
            if (text.Hidden) unseen++; else seen++;
        }
        Assert.True(seen >= 5 && unseen >= 5, $"the callout is shown on {seen} frames and hidden on {unseen}");
    }

    // ---- an annotation of a model ----

    [BlenderFact]
    public async Task AnAnnotationOfAModel_GoesWhereTheModelsPointGoes_AndIsNotStretchedOrTurnedWithIt()
    {
        var spec = Still() with { Animation = new AnimationSpec { Fps = 10, DurationSeconds = 1.1 }, Output = Still().Output with { Kind = OutputKind.Video } };
        var point = new CaveVector3(4, -3, 2);
        var translation = new CaveVector3(100, 50, -20);
        var lift = new CaveVector3(0, 0, 30);
        var (plan, job) = PlayerRunner.WriteJob("annotations-model", Small.Value, spec, p => With(
            p with
            {
                // The cave is moved, and turned a quarter to the left about the vertical.
                Models = [p.Models[0] with { Translation = translation, Rotation = new PlanRotation { Axis = new CaveVector3(0, 0, 1), Degrees = 90 } }],
            },
            [
                new PlanAnnotation { Id = "rider", Kind = PlanAnnotationKind.Label, At = point, Model = ScenePlanBuilder.ModelId, Text = "st. 12", Size = 1 },
                new PlanAnnotation
                {
                    Id = "note", Kind = PlanAnnotationKind.Callout, At = point, Model = ScenePlanBuilder.ModelId, Text = "st. 12", Size = 1,
                    Offset = new CaveVector3(0, 2, 5),
                },
                new PlanAnnotation { Id = "view", Kind = PlanAnnotationKind.Photo, At = point, Model = ScenePlanBuilder.ModelId, Image = FlagPng, Width = 3 },
                new PlanAnnotation { Id = "stays", Kind = PlanAnnotationKind.Label, At = point, Text = "a point of the scene", Size = 1 },
            ],
            // While the clip runs it is lifted thirty metres and made twice as high.
            new PlanChange
            {
                Target = new PlanChangeTarget { Id = ScenePlanBuilder.ModelId },
                Offset = [new PlanVectorKey(0, CaveVector3.Zero), new PlanVectorKey(1, lift)],
                Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 1)), new PlanVectorKey(1, new CaveVector3(1, 1, 2))],
            }));
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Equal(11, plan.Timeline.FrameCount);

        var report = await Parts(job, 1, 6, 11);

        foreach (int f in new[] { 1, 6, 11 })
        {
            double share = (f - 1) / 10.0;
            // Stretched along its own axes, then turned (x, y) → (−y, x), then moved.
            var stretched = new CaveVector3(point.X, point.Y, point.Z * (1 + share));
            var expected = translation + lift * share + new CaveVector3(-stretched.Y, stretched.X, stretched.Z);
            var frame = report.Frame(f);
            Near(expected, frame.Part("rider").Location, $"frame {f}: the label of the model's point");
            Near(expected + new CaveVector3(0, 2, 5), frame.Part("note").Location, $"frame {f}: the callout's text");
            Near(expected, frame.Part("note.line").Ends![0], $"frame {f}: where the callout's line starts");
            Near(expected + new CaveVector3(0, 2, 5), frame.Part("note.line").Ends![1], $"frame {f}: where the callout's line ends");
            Near(expected, frame.Part("view").Location, $"frame {f}: the photo");
            // It rides the point; it is not stretched, and it turns only to the camera.
            Assert.All(new[] { "rider", "note", "note.line", "view" }, name => Near(new CaveVector3(1, 1, 1), frame.Part(name).Scale, $"frame {f}: the size of '{name}'"));
            Near(3, (V(frame.Part("view").Corners!["bottomRight"]) - V(frame.Part("view").Corners!["bottomLeft"])).Length, $"frame {f}: the photo's width");
            Assert.True(Degrees(V(frame.Part("rider").Z), V(frame.Camera.Location) - expected) < 0.05, $"frame {f}: the label does not face the camera");
            Near(V(frame.Camera.Back), frame.Part("view").Z, $"frame {f}: the way the photo faces");
            Near(point, frame.Part("stays").Location, $"frame {f}: the label of a point of the scene");
            Assert.Equal(
                [("note.anchor", "note", "CaveModel"), ("rider.anchor", "rider", "CaveModel"), ("view.anchor", "view", "CaveModel")],
                frame.Anchors.Select(a => (a.Name, a.Of, a.Parent!)));
            Assert.All(frame.Anchors, a => Near(expected, a.Location, $"frame {f}: the anchor {a.Name}"));
        }
    }

    // ---- a saved scene ----

    [BlenderFact]
    public async Task ASavedScene_KeepsItsPhotos_InsideTheFile()
    {
        var (plan, job) = PlayerRunner.WriteJob("annotations-blend", Small.Value, Still(), p => ScenePlanBuilder.SaveBlend(Shown(p), "scene.blend", render: false));
        Assert.Contains(ScenePlan.BlendFileFeature, plan.Features);

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        // The file is all that is needed: the picture beside the plan is taken away.
        Directory.Delete(Path.Combine(job.Folder, "photos"), recursive: true);

        var report = await PlayerRunner.ProbeBlendAsync(Path.Combine(job.Folder, "scene.blend"), new ProbeOptions { Frames = [1] });

        var image = Assert.Single(report.Images);
        Assert.Equal(("flag.png", true), (image.Name, image.Loaded));
        Assert.Equal([64, 32], image.Size);
        Assert.Equal("MESH", report.Object("view").Type);
        Assert.Contains("Photo flag.png", report.Object("view").Materials);
        Assert.Equal("MWM", report.Object("note").Text);
    }

    // ---- in a bundle ----

    [BlenderFact]
    public async Task ABundlesPhoto_TravelsWithIt_AndIsInThePictureItRenders()
    {
        string root = PlayerRunner.NewCase("annotations-bundle");
        string work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        string lox = Path.Combine(work, "cave.lox");
        Therion.Cave3D.Parsing.LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        string picture = TestImages.Write(Path.Combine(work, "trip"), "flag.jpg", TestImages.FlagJpeg());
        var spec = Still() with
        {
            Name = "Invented Small Cave",
            Annotations =
            [
                new AnnotationSpec { Id = "flag", Kind = AnnotationKind.Photo, Station = "main6", Image = picture, Size = 40, Offset = new CaveVector3(0, 0, 25), Framed = true },
            ],
        };
        var content = RenderBundleWriter.FromModel(
            new Therion.Blender.Sources.ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = Therion.Blender.Sources.ModelSourceKind.ExternalFile },
            spec, work);
        string folder = Path.Combine(root, "bundle");
        var written = RenderBundleWriter.WriteFolder(content, folder);
        // Where it came from is gone before the bundle is used.
        Directory.Delete(work, recursive: true);
        Assert.Contains("photos/flag.jpg", written);

        var (exitCode, log) = await PlayerRunner.RunAsync(Path.Combine(folder, PlayerFiles.EntryFileName));

        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {folder}");
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        var frame = Picture.Read(Path.Combine(folder, "out", "f_0001.png"));
        var (red, blue, green) = (frame.Of(Red), frame.Of(Blue), frame.Of(Green));
        Assert.True(red.Count > 100 && blue.Count > 100 && green.Count > 50, $"the flag's colours are {red}, {blue}, {green}");
        Assert.True(red.X < blue.X && green.Y < red.Y, $"the flag is not the right way round: {red}, {blue}, {green}");
    }

    [BlenderFact]
    public async Task ASingleFile_CarriesItsPhoto_AndShowsItWithNothingBesideIt()
    {
        var plan = Shown(PlayerRunner.PlanFor(Small.Value, Still()));
        string dir = PlayerRunner.NewCase("annotations-single-file");
        string script = Path.Combine(dir, "everything.py");
        var files = PlayerRunner.FilesOf(Small.Value, plan).ToDictionary(f => f.Key, f => File.ReadAllBytes(f.Value));
        Assert.Equal(["model.ply", FlagPng], files.Keys);
        await File.WriteAllTextAsync(script, PlayerWriter.SingleFile(plan, files));

        var (exitCode, log) = await PlayerRunner.RunAsync(script);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {dir}");
        Assert.Equal(["everything.py", "f_0001.png", "run.log"], Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetDirectories(dir));
        var right = Picture.Read(Path.Combine(dir, "f_0001.png")).Part(0.5, 1, 0, 1);
        var (red, blue, green) = (right.Of(Red), right.Of(Blue), right.Of(Green));
        Assert.True(red.Count > 250 && blue.Count > 250 && green.Count > 150 && red.X < blue.X, $"the flag's colours are {red}, {blue}, {green}");
    }

    // ---- what is refused ----

    [BlenderFact]
    public async Task WhatAnAnnotationCannotBe_IsRefused_ByName()
    {
        var (plan, job) = PlayerRunner.WriteJob("annotations-refused", Small.Value, Still(), p => Shown(p));
        string written = await File.ReadAllTextAsync(job.PlanPath);
        // An image that is no image, and one that is not there.
        TestImages.Write(Path.Combine(job.Folder, "photos"), "notes.png", "These are notes, not a picture."u8.ToArray());

        async Task<string> RefusedPlan()
        {
            var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
            Assert.True(exitCode == 64, $"exit code {exitCode}, expected 64; logs: {job.Folder}");
            return Assert.Single(PlayerRunner.Reported(log, "error"));
        }
        // What the plan's own records can say: one annotation changed, written without a check.
        async Task<string> Changed(string id, Func<PlanAnnotation, PlanAnnotation> change)
        {
            ScenePlanSerializer.WriteFile(plan with { Annotations = [.. plan.Annotations!.Select(a => a.Id == id ? change(a) : a)] }, job.PlanPath);
            return await RefusedPlan();
        }
        // What they cannot: the text of the plan, changed.
        async Task<string> Refused(string original, string changed)
        {
            Assert.Contains(original, written);
            await File.WriteAllTextAsync(job.PlanPath, written.Replace(original, changed));
            return await RefusedPlan();
        }

        Assert.Equal("annotations[0].kind: unknown kind \"arrow\"", await Refused("\"kind\": \"marker\"", "\"kind\": \"arrow\""));
        Assert.Equal("annotations[0].radius: a positive number is needed", await Changed("dot", a => a with { Radius = 0 }));
        Assert.Equal("annotations[0].text: a marker has no \"text\"", await Changed("dot", a => a with { Text = "here" }));
        Assert.Equal("annotations[1].text: a callout needs its text", await Changed("note", a => a with { Text = null }));
        Assert.Equal(
            "annotations[1].offset: a callout's text stands away from its anchor; its offset is not zero",
            await Changed("note", a => a with { Offset = CaveVector3.Zero }));
        Assert.Equal(
            "annotations[1].offset: a callout's text stands away from its anchor; it needs an offset",
            await Changed("note", a => a with { Offset = null }));
        Assert.Equal("annotations[2].id: something else in the scene has the id \"dot\"", await Changed("name", a => a with { Id = "dot" }));
        Assert.Equal("annotations[2].id: something else in the scene has the id \"CaveModel\"", await Changed("name", a => a with { Id = "CaveModel" }));
        Assert.Equal(
            "annotations[2].model: there is no model or line \"Elsewhere\"; list \"annotations\" after the modules that make them",
            await Changed("name", a => a with { Model = "Elsewhere" }));
        Assert.Equal("annotations[2].size: a positive number is needed", await Changed("name", a => a with { Size = -1 }));
        Assert.Equal("annotations[3].image: \"photos/flag.gif\" is neither a .png nor a .jpg file", await Changed("view", a => a with { Image = "photos/flag.gif" }));
        Assert.StartsWith("annotations[3].image: file not found: ", await Changed("view", a => a with { Image = "photos/gone.png" }));
        Assert.Equal("annotations[3].image: \"photos/notes.png\" cannot be read as an image", await Changed("view", a => a with { Image = "photos/notes.png" }));
        Assert.Equal("annotations[3].width: a positive number is needed", await Changed("view", a => a with { Width = 0 }));
        Assert.Equal("annotations[3].facing: unknown facing \"north\"", await Refused("\"kind\": \"photo\"", "\"kind\": \"photo\",\n      \"facing\": \"north\""));
        Assert.Equal(
            "annotations[3].normal: only a \"fixed\" photo has one; this one faces the camera",
            await Changed("view", a => a with { Normal = new CaveVector3(0, 1, 0) }));
        Assert.Equal(
            "annotations[3].normal: a fixed photo needs the direction it faces",
            await Changed("view", a => a with { Facing = PlanFacing.Fixed, Normal = CaveVector3.Zero }));
        Assert.Equal(
            "annotations[3].up: the top of a photo points somewhere off the direction it faces",
            await Changed("view", a => a with { Facing = PlanFacing.Fixed, Normal = new CaveVector3(0, 1, 0), Up = new CaveVector3(0, -3, 0) }));
        Assert.Equal(
            "annotations[3].lineWidth: a photo has a line to its anchor only when it stands away from it; give it an offset",
            await Changed("view", a => a with { LineWidth = 0.1 }));
        Assert.Equal(
            "annotations[3].pulse is not known to player " + PlayerFiles.Version + "; a newer player may be needed",
            await Refused("\"kind\": \"photo\"", "\"kind\": \"photo\",\n      \"pulse\": 2"));

        // Made before there is a camera, an annotation has nothing to turn to.
        Assert.Equal(
            "annotations turn to the camera; list \"camera_track\" before \"annotations\" in \"features\"",
            await Refused("\"camera_track\",\n    \"look\",\n    \"annotations\",", "\"annotations\",\n    \"camera_track\",\n    \"look\","));
        // A change that names an annotation finds nothing when it runs before it is made.
        ScenePlanSerializer.WriteFile(Shown(plan with { Annotations = null, Changes = null, Features = ScenePlan.BasicFeatures },
            new PlanChange { Target = new PlanChangeTarget { Id = "view" }, Visible = [new PlanVisibleKey(0, false)] }), job.PlanPath);
        written = await File.ReadAllTextAsync(job.PlanPath);
        Assert.Equal(
            "changes[0].target.id: nothing in the scene has the id \"view\"",
            await Refused("\"annotations\",\n    \"changes\",", "\"changes\",\n    \"annotations\","));

        // A player from before there were annotations has no such module, and says so.
        await File.WriteAllTextAsync(job.PlanPath, written);
        File.Delete(Path.Combine(job.Folder, PlayerFiles.PackageName, "modules", "annotations.py"));
        var (code, text) = await PlayerRunner.RunAsync(job.ScriptPath, "--build-only");
        Assert.Equal(64, code);
        Assert.Equal(["the plan needs the module \"annotations\", which this player does not have"], PlayerRunner.Reported(text, "error"));
    }
}
