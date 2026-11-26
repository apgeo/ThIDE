// What the plan gained for a producer of its own, in a real Blender on invented caves and invented
// images: markers with a symbol and a size by the camera, an outline, a change by id that wins
// over a change by role, a reveal of several models of one role in step, and a title card that
// fades in and out apart. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1, and
// THIDE_BLENDER_E2E=1 for the pictures; THIDE_BLENDER_PATH picks the Blender).

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Picture = Therion.Blender.Tests.Player.RenderedPicture;

namespace Therion.Blender.Tests.Player;

public partial class PlayerAnnotationsBlenderTests
{
    [BlenderFact]
    public async Task AMarker_CanBeAPinAConeACubeARingOrAPicture_EachWhereItsShapeStands()
    {
        const double r = 1.5;
        var pin = new CaveVector3(-20, 5, 3);
        var cone = new CaveVector3(-10, -5, -2);
        var cube = new CaveVector3(0, 0, 8);
        var ring = new CaveVector3(12, 4, 6);
        var pic = new CaveVector3(25, -8, -4);
        PlanAnnotation Dot(string id, CaveVector3 at, PlanMarkerSymbol symbol) => new() { Id = id, Kind = PlanAnnotationKind.Marker, At = at, Radius = r, Symbol = symbol };
        var (plan, job) = PlayerRunner.WriteJob("gaps-symbols", Small.Value, Still(), p => With(p,
        [
            Dot("pin", pin, PlanMarkerSymbol.Pin), Dot("cone", cone, PlanMarkerSymbol.Cone), Dot("cube", cube, PlanMarkerSymbol.Cube),
            Dot("ring", ring, PlanMarkerSymbol.Ring), Dot("pic", pic, PlanMarkerSymbol.Image) with { Image = FlagPng },
        ]));
        Assert.Empty(ScenePlanValidator.Validate(plan));

        var parts = (await Parts(job, 1)).Frame(1);
        string log = await File.ReadAllTextAsync(Path.Combine(job.Folder, "parts.log"));
        Assert.Equal(["5"], PlayerRunner.Reported(log, "annotations"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        Assert.Equal(["cone", "cube", "pic", "pin", "ring"], parts.Parts.Select(p => p.Name));
        Assert.Equal(["cone", "cube", "picture", "pin", "ring"], parts.Parts.Select(p => p.PartName));
        Assert.All(parts.Parts, p => Assert.Equal("marker", p.Role));
        Assert.All(parts.Parts, p => Assert.False(p.Shadow, p.Name + " casts a shadow"));

        void Bounds(string name, double[] low, double[] high, double tolerance = 1e-3)
        {
            var part = parts.Part(name);
            Near(V(low), part.Bounds![0], $"{name}: the low corner of its mesh", tolerance);
            Near(V(high), part.Bounds[1], $"{name}: the high corner of its mesh", tolerance);
        }
        // A pin and a cone stand with their point on the anchor; a cube is centred on it.
        Near(pin, parts.Part("pin").Location, "the pin");
        Assert.True(Math.Abs(parts.Part("pin").Bounds![0][2]) < 1e-4, "the pin's point is on its anchor");
        Near(3.8 * r, parts.Part("pin").Bounds![1][2], "the top of the pin's ball", 1e-3);
        Near(cone, parts.Part("cone").Location, "the cone");
        Bounds("cone", [-r, -r, 0], [r, r, 2 * r], tolerance: 0.1 * r);
        Assert.True(Math.Abs(parts.Part("cone").Bounds![0][2]) < 1e-4 && Math.Abs(parts.Part("cone").Bounds![1][2] - 2 * r) < 1e-4, "the cone is twice its radius high");
        Near(cube, parts.Part("cube").Location, "the cube");
        Bounds("cube", [-r, -r, -r], [r, r, r]);
        Assert.Equal((8, 6), (parts.Part("cube").Vertices, parts.Part("cube").Faces));

        // A ring is flat and turns to the camera as a text does.
        var ringPart = parts.Part("ring");
        Near(ring, ringPart.Location, "the ring");
        Assert.Contains("TRACK_TO", ringPart.Constraints);
        Assert.True(ringPart.Bounds![1][2] < 0.2 * r && ringPart.Bounds[0][2] > -0.2 * r, "the ring is flat");
        Assert.True(ringPart.Bounds[1][0] > 1.1 * r && ringPart.Bounds[1][0] < 1.2 * r, $"the ring's reach {ringPart.Bounds[1][0]}");
        Assert.True(Degrees(V(ringPart.Z), V(parts.Camera.Location) - ring) < 0.05, "the ring does not face the camera");

        // A picture is twice the radius wide, faces the camera and shows its image as it is.
        var picture = parts.Part("pic");
        Near(pic, picture.Location, "the picture");
        Near(2 * r, (V(picture.Corners!["bottomRight"]) - V(picture.Corners["bottomLeft"])).Length, "the picture's width");
        Assert.True(picture.Image!.Emitted, "the picture does not show its image");
        Near(V(parts.Camera.Back), picture.Z, "the way the picture faces");
    }

    [BlenderFact]
    public async Task AMarkerSizedByTheCamera_HasTheSizeItsDistanceGives_AtEveryFrame_ByDistanceAndByScreen()
    {
        var a = new CaveVector3(-5, 5, 0);
        var b = new CaveVector3(5, -5, 2);
        var c = new CaveVector3(0, 0, 4);
        PlanAnnotation Dot(string id, CaveVector3 at, Func<PlanAnnotation, PlanAnnotation> more) => more(new PlanAnnotation { Id = id, Kind = PlanAnnotationKind.Marker, At = at, Radius = 1 });
        var (plan, job) = PlayerRunner.WriteJob("gaps-size", Small.Value, Still(11), p =>
        {
            // The camera walks 60 % of the way to what it looks at: the distances change.
            var pose = p.Camera.Track[0];
            var closer = pose with { T = 2.0, Eye = pose.Eye + (pose.Target - pose.Eye) * 0.6 };
            return With(p with { Camera = p.Camera with { Track = [pose, closer] } },
            [
                Dot("dist", a, m => m with { SizeBy = PlanMarkerSizing.Distance, Factor = 0.02, MinRadius = 0.001, MaxRadius = 1000 }),
                Dot("screen", b, m => m with { SizeBy = PlanMarkerSizing.Screen, Factor = 0.03, MinRadius = 0.001, MaxRadius = 1000, Symbol = PlanMarkerSymbol.Cube }),
                Dot("capped", c, m => m with { SizeBy = PlanMarkerSizing.Distance, Factor = 5, MinRadius = 0.4, MaxRadius = 0.5 }),
                Dot("floored", c, m => m with { SizeBy = PlanMarkerSizing.Distance, Factor = 1e-6, MinRadius = 0.4, MaxRadius = 0.5 }),
                Dot("plain", c, m => m),
            ]);
        });
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var report = await Parts(job, 1, 6, 11);
        double sensorOverLens = plan.Camera.SensorWidth / plan.Camera.Lens;

        var apparent = new List<double>();
        foreach (int f in new[] { 1, 6, 11 })
        {
            var frame = report.Frame(f);
            var camera = V(frame.Camera.Location);
            double Distance(string id) => (camera - V(frame.Part(id).Location)).Length;
            Near(Distance("dist") * 0.02, frame.Part("dist").Scale[0], $"frame {f}: the size by distance", 1e-3);
            Assert.All(new[] { 1, 2 }, axis => Near(frame.Part("dist").Scale[0], frame.Part("dist").Scale[axis], $"frame {f}: the same on every axis", 1e-6));
            double screen = Distance("screen") * 0.03 * sensorOverLens;
            Near(screen, frame.Part("screen").Scale[0], $"frame {f}: the size by screen", 1e-3);
            apparent.Add(frame.Part("screen").Scale[0] / Distance("screen"));
            Near(0.5, frame.Part("capped").Scale[0], $"frame {f}: held to its largest", 1e-6);
            Near(0.4, frame.Part("floored").Scale[0], $"frame {f}: held to its smallest", 1e-6);
            Near(1, frame.Part("plain").Scale[0], $"frame {f}: a marker without a size by the camera", 1e-6);
        }
        // By distance it shrank with the camera's approach; by screen it kept its share of the picture.
        Assert.True(report.Frame(11).Part("dist").Scale[0] < report.Frame(1).Part("dist").Scale[0]);
        Assert.All(apparent, share => Near(apparent[0], share, "the share of the picture", 1e-6));
    }

    [BlenderFact]
    public async Task AnOutline_IsATubeThroughItsPoints_DrawnAsALineIs_AndShownByAChange()
    {
        var points = new CaveVector3[] { new(-20, 0, 0), new(0, 10, 0), new(20, 0, 3) };
        var (plan, job) = PlayerRunner.WriteJob("gaps-outline", Small.Value, Still(11), p => With(p,
        [
            new PlanAnnotation { Id = "edge", Kind = PlanAnnotationKind.Outline, At = points[0], Points = points, Width = 0.4, Color = new ColorRgb(0, 1, 0) },
            new PlanAnnotation { Id = "rider", Kind = PlanAnnotationKind.Outline, At = points[0], Points = points, Width = 0.1, Model = ScenePlanBuilder.ModelId },
        ],
        new PlanChange { Target = new PlanChangeTarget { Id = "edge" }, Visible = [new PlanVisibleKey(0.8, false)] }));
        Assert.Empty(ScenePlanValidator.Validate(plan));

        var report = await Parts(job, 1, 4, 5, 11);
        string log = await File.ReadAllTextAsync(Path.Combine(job.Folder, "parts.log"));
        Assert.Equal(["2"], PlayerRunner.Reported(log, "annotations"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        var edge = report.Frame(1).Part("edge.outline");
        Assert.Equal(("line", "outline", "MESH", 18), (edge.PartName, edge.Role, edge.Type, edge.Vertices));
        Assert.Equal("edge", edge.Id);
        for (int i = 0; i < points.Length; i++) Near(points[i], edge.Rings![i], $"the outline's point {i}");
        Near(0.4, edge.Width!.Value, "the outline's width");
        Assert.Equal(report.Frame(1).Part("edge.outline").Materials, [report.Frame(1).Part("edge.outline").Materials[0]]);
        // It is hidden from 0.8 s (frame 5) on, as any annotation is; the other is there throughout.
        Assert.False(report.Frame(4).Part("edge.outline").Hidden);
        Assert.True(report.Frame(5).Part("edge.outline").Hidden);
        Assert.False(report.Frame(11).Part("rider.outline").Hidden);
        // One of a model rides it, and is anchored to it.
        Assert.Contains(report.Frame(1).Anchors, an => an.Of == "rider" && an.Parent == ScenePlanBuilder.ModelId);
    }
}

public partial class PlayerChangesBlenderTests
{
    [BlenderFact]
    public async Task AChangeByIdAndAChangeByRole_OfOneProperty_Play_TheIdsWinning_InEitherOrder()
    {
        var plan0 = ScenePlanBuilder.AddModel(
            PlayerRunner.PlanFor(Small.Value, Orbit(6)),
            new PlanModel { Id = "Twin", Path = PlayerRunner.PlanFor(Small.Value, Orbit(6)).Models[0].Path, Translation = new CaveVector3(0, 0, 500) },
            PlayerRunner.PlanFor(Small.Value, Orbit(6)).Look.Materials[0]);
        var byRole = Of(role: "model") with
        {
            Opacity = [new PlanNumberKey(0, 0.3)],
            Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 2))],
        };
        var byId = Of("Twin") with { Opacity = [new PlanNumberKey(0, 0.9)] };
        int run = 0;
        foreach (var changes in new[] { new[] { byRole, byId }, new[] { byId, byRole } })
        {
            var plan = ScenePlanBuilder.SaveBlend(Changed(plan0, changes), render: false);
            Assert.Empty(ScenePlanValidator.Validate(plan));
            var job = PlayerWriter.WriteJob(plan, PlayerRunner.NewCase("changes-id-over-role-" + run++), PlayerRunner.MeshOf(Small.Value));
            var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath);
            Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
            Assert.Empty(PlayerRunner.Reported(log, "warning"));
            var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [1, 4, 6] });
            foreach (int f in new[] { 1, 4, 6 })
            {
                var frame = report.Frame(f);
                // The role's change has the cave's opacity; the id's has the twin's; the role's scale is both's.
                Near(0.3, frame.Thing(ScenePlanBuilder.ModelId).Opacity, $"run {run}, frame {f}: the cave's opacity");
                Near(0.9, frame.Thing("Twin").Opacity, $"run {run}, frame {f}: the twin's opacity");
                Near([1, 1, 2], frame.Thing(ScenePlanBuilder.ModelId).Scale, $"run {run}, frame {f}: the cave's scale");
                Near([1, 1, 2], frame.Thing("Twin").Scale, $"run {run}, frame {f}: the twin's scale");
            }
        }
    }
}

public partial class PlayerRevealBlenderTests
{
    [BlenderFact]
    public async Task SeveralModelsOfOneRole_AreRevealedInStep_ByOneChangeOfTheRole()
    {
        const int frames = 16;
        var spec = Clip(frames, new RevealSpec { By = RevealBy.Distance, Start = 0.2, End = 1.2 });
        var assets = RevealFixtures.Prepare("reveal-role", InventedCave.Build(InventedCaveSize.Medium), spec, asLox: true);
        var plan = PlayerRunner.PlanFor(assets, spec);
        var values = assets.Conversion.Meta.Reveal!;
        // The cave and its twin, one role, and one change of it: the numbers the spec's change has.
        plan = plan with { Models = [plan.Models[0] with { Role = "cave" }] };
        plan = ScenePlanBuilder.AddModel(
            plan, new PlanModel { Id = "Twin", Role = "cave", Path = plan.Models[0].Path, Translation = new CaveVector3(0, 0, 500) }, plan.Look.Materials[0]);
        var change = RevealPlanner.Change(RevealPlanner.ByRole("cave"), values.Min, values.Max, 0.2, 1.2, 0.03, true, fps: Fps);
        plan = plan with { Changes = [change] };
        var job = await Built(plan, "reveal-role", assets);

        var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = [.. Enumerable.Range(1, frames)] });
        double before = -1;
        foreach (int f in Enumerable.Range(1, frames))
        {
            var (cave, twin) = (report.Frame(f).Thing(ScenePlanBuilder.ModelId), report.Frame(f).Thing("Twin"));
            double threshold = At(change.Reveal!, f);
            Near(threshold, cave.Reveal, $"frame {f}: the cave's threshold");
            Near(threshold, twin.Reveal, $"frame {f}: the twin's threshold");
            Near(change.RevealFront!.Edge, twin.RevealEdge, $"frame {f}: the twin's edge");
            // The same mesh at the same threshold is the same part of it.
            Near(cave.Area!.Value, twin.Area, $"frame {f}: what is there of the twin", 1e-6);
            Assert.True(cave.Area >= before, $"frame {f}: it has not shrunk");
            before = cave.Area.Value;
        }
        Assert.True(before > 0 && report.Frame(1).Thing("Twin").Area == 0, "the reveal runs from nothing to the whole");
    }
}

public partial class PlayerTwoStageBlenderTests
{
    [BlenderFact]
    public async Task ATitleCard_CanFadeInAndOutApart_AndTheOldFieldStillFadesBoth()
    {
        const int frames = 16;
        // The card is 1 s long at 8 fps; its text is in the middle of the picture.
        var middle = new Region(0.2, 0.35, 0.8, 0.65);
        async Task<Func<int, int>> Ink(string name, OverlaySpec overlays)
        {
            var (_, job) = PlayerRunner.WriteJob(name, Small.Value, Orbit(overlays, frames), Quick);
            await Run(job, "--keep-frames");
            var written = Enumerable.Range(1, frames).Select(n => Picture.Read(Frame(job, n))).ToList();
            var bare = Enumerable.Range(1, frames).Select(n => Picture.Read(StageFrame(job, n))).ToList();
            return frame => written[frame - 1].Differing(bare[frame - 1], middle, inside: true, tolerance: 100);
        }
        var card = new OverlaySpec { Title = "TITLE", TitleSeconds = 1.0 };

        // In over half a second and cut at the end (1 s): small at first, full by the end, gone after.
        var inOnly = await Ink("title-fade-in", card with { TitleFadeIn = 0.5, TitleFadeOut = 0 });
        Assert.True(inOnly(1) < inOnly(8) / 2, $"it does not fade in: {inOnly(1)} then {inOnly(8)} pixels");
        Assert.True(inOnly(8) > 40, $"the card is not there at the end of its time: {inOnly(8)} pixels");
        Assert.Equal(0, inOnly(9));

        // At once and out over half a second after its time (to 1.5 s): full at first, fading, gone.
        var outOnly = await Ink("title-fade-out", card with { TitleFadeIn = 0, TitleFadeOut = 0.5 });
        Assert.True(outOnly(1) > 40, $"the card is not there at once: {outOnly(1)} pixels");
        Assert.True(outOnly(11) < outOnly(6) / 2 && outOnly(11) > 0, $"it does not fade out: {outOnly(6)} then {outOnly(11)} pixels");
        Assert.Equal(0, outOnly(14));

        // The old field alone does both, as it did.
        var both = await Ink("title-fade-both", card with { TitleFade = 0.5 });
        Assert.True(both(1) < both(6) / 2 && both(11) < both(6) / 2, $"{both(1)}, {both(6)}, {both(11)} pixels");
    }
}
