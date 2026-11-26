// What the plan, the planners and the reveal field gained for a producer that writes plans of its
// own, without Blender: markers that are sized by the camera and have a symbol, an outline, a change
// by id over a change by role, a model's point that moves with the model, photos laid out, a reveal
// over a plain graph of legs, and a lamp that reaches by the day. Invented caves only. The player's
// side of these is in PlayerGapsBlenderTests.

using Therion.Blender.Plan;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class BlenderGapsTests
{
    private static ScenePlan Full => PlanFixtures.Full();

    private static IEnumerable<string> Paths(ScenePlan plan) => ScenePlanValidator.Validate(plan).Select(e => e.Path);

    private static ScenePlan WithAnnotation(PlanAnnotation annotation) => Full with { Annotations = [.. Full.Annotations!, annotation] };

    private static PlanAnnotation Marker(Func<PlanAnnotation, PlanAnnotation>? change = null)
    {
        var marker = new PlanAnnotation { Id = "mk", Kind = PlanAnnotationKind.Marker, At = new CaveVector3(1, 2, 3), Radius = 0.5 };
        return change is null ? marker : change(marker);
    }

    // ---- markers: a symbol and a size by the camera ----

    [Fact]
    public void AMarker_HasASymbol_AndASizeByDistanceOrScreen_WrittenAndReadBack()
    {
        foreach (var symbol in new[] { PlanMarkerSymbol.Ball, PlanMarkerSymbol.Pin, PlanMarkerSymbol.Cone, PlanMarkerSymbol.Cube, PlanMarkerSymbol.Ring })
            Assert.Empty(Paths(WithAnnotation(Marker(m => m with { Symbol = symbol }))));
        var sized = WithAnnotation(Marker(m => m with
        {
            Symbol = PlanMarkerSymbol.Pin, SizeBy = PlanMarkerSizing.Screen, Factor = 0.03, MinRadius = 0.1, MaxRadius = 2,
        }));
        Assert.Empty(Paths(sized));
        string json = ScenePlanSerializer.Write(sized);
        Assert.Contains("\"symbol\": \"pin\"", json);
        Assert.Contains("\"sizeBy\": \"screen\"", json);
        var back = ScenePlanSerializer.Read(json).Annotations!.Last();
        Assert.Equal((PlanMarkerSymbol.Pin, PlanMarkerSizing.Screen, 0.03, 0.1, 2.0), (back.Symbol, back.SizeBy, back.Factor, back.MinRadius, back.MaxRadius));

        // An image marker names its file, which travels with the plan.
        var image = WithAnnotation(Marker(m => m with { Symbol = PlanMarkerSymbol.Image, Image = "photos/dot.png" }));
        Assert.Empty(Paths(image));
        Assert.Contains("photos/dot.png", Therion.Blender.Player.PlayerWriter.FilesOf(image).Select(f => f.Path));
    }

    [Fact]
    public void AMarkersSymbolAndSize_AreHeldToWhatTheyCanBe()
    {
        Assert.Equal(["annotations[" + Full.Annotations!.Count + "].factor"], Paths(WithAnnotation(Marker(m => m with { SizeBy = PlanMarkerSizing.Distance }))));
        Assert.Contains("annotations[" + Full.Annotations.Count + "].factor", Paths(WithAnnotation(Marker(m => m with { Factor = 1 }))));
        Assert.Contains("annotations[" + Full.Annotations.Count + "].minRadius",
            Paths(WithAnnotation(Marker(m => m with { SizeBy = PlanMarkerSizing.Distance, Factor = 1, MinRadius = 3, MaxRadius = 2 }))));
        Assert.Contains("annotations[" + Full.Annotations.Count + "].image", Paths(WithAnnotation(Marker(m => m with { Image = "photos/dot.png" }))));
        Assert.Contains("annotations[" + Full.Annotations.Count + "].image", Paths(WithAnnotation(Marker(m => m with { Symbol = PlanMarkerSymbol.Image }))));
        Assert.Contains("annotations[" + Full.Annotations.Count + "].image",
            Paths(WithAnnotation(Marker(m => m with { Symbol = PlanMarkerSymbol.Image, Image = "../dot.png" }))));
        // A label has none of these.
        Assert.Contains("annotations[0].symbol", Paths(Full with { Annotations = [Full.Annotations[0] with { Symbol = PlanMarkerSymbol.Pin }, .. Full.Annotations.Skip(1)] }));
    }

    [Fact]
    public void TheSpecsMarker_KeepsItsSymbolAndSize_AndItsImageFileTravels()
    {
        var png = Path.Combine(Path.GetTempPath(), "dot.png");
        var specs = new[]
        {
            new AnnotationSpec { Id = "a", Kind = AnnotationKind.Marker, At = new CaveVector3(0, 30, 0), Symbol = PlanMarkerSymbol.Cone, Size = 2, SizeBy = PlanMarkerSizing.Distance, SizeFactor = 0.05 },
            new AnnotationSpec { Id = "b", Kind = AnnotationKind.Marker, At = new CaveVector3(0, 30, 0), Symbol = PlanMarkerSymbol.Image, Image = png, Size = 2 },
            new AnnotationSpec { Id = "c", Kind = AnnotationKind.Outline, Points = [new CaveVector3(0, 0, 0), new CaveVector3(5, 0, 0), new CaveVector3(5, 5, 0)], Size = 0.2 },
        };
        var files = AnnotationPlanner.Files(specs);
        Assert.Equal(["photos/dot.png"], files.Keys);
        var plan = AnnotationPlanner.Plan(specs, null, new BoundingBox(new CaveVector3(-30, -40, 0), new CaveVector3(30, 40, 0)),
            new PlanCamera { Track = [new PlanCameraKey(0, new CaveVector3(0, -50, 0), new CaveVector3(0, 0, 0))] }, new PlanTimeline { Fps = 10, FrameCount = 11 });
        var (a, b, c) = (plan.Annotations[0], plan.Annotations[1], plan.Annotations[2]);
        Assert.Equal((PlanMarkerSymbol.Cone, PlanMarkerSizing.Distance, 0.05, 2.0), (a.Symbol, a.SizeBy, a.Factor, a.Radius));
        Assert.Equal(("photos/dot.png", PlanMarkerSymbol.Image), (b.Image, b.Symbol));
        Assert.Equal((PlanAnnotationKind.Outline, 3, 0.2, "outline"), (c.Kind, c.Points!.Count, c.Width, PlanAnnotation.DefaultRole(c.Kind)));
        Assert.Equal(c.Points[0], c.At);

        // The spec's file reads and writes them too.
        var back = AnnotationsFile.Read(AnnotationsFile.Write(specs));
        Assert.Equal((PlanMarkerSymbol.Cone, 0.05), (back[0].Symbol, back[0].SizeFactor));
        Assert.Equal(specs[2].Points, back[2].Points);
    }

    // ---- an outline ----

    [Fact]
    public void AnOutline_IsAPolylineWithAWidthAndAColour_WrittenReadAndHeldToWhatItCanBe()
    {
        var outline = new PlanAnnotation
        {
            Id = "edge", Kind = PlanAnnotationKind.Outline, At = new CaveVector3(0, 0, 0), Width = 0.3, Color = new ColorRgb(0, 1, 0),
            Points = [new CaveVector3(0, 0, 0), new CaveVector3(4, 0, 0), new CaveVector3(4, 3, 1)],
        };
        var plan = WithAnnotation(outline);
        Assert.Empty(Paths(plan));
        string json = ScenePlanSerializer.Write(plan);
        Assert.Contains("\"kind\": \"outline\"", json);
        Assert.Equal(outline.Points, ScenePlanSerializer.Read(json).Annotations!.Last().Points);
        int at = Full.Annotations!.Count;
        Assert.Equal([$"annotations[{at}].points"], Paths(WithAnnotation(outline with { Points = [new CaveVector3(0, 0, 0)] })));
        Assert.Equal([$"annotations[{at}].points"], Paths(WithAnnotation(outline with { Points = null })));
        Assert.Equal([$"annotations[{at}].width"], Paths(WithAnnotation(outline with { Width = null })));
        Assert.Equal([$"annotations[{at}].text"], Paths(WithAnnotation(outline with { Text = "x" })));
        // It is shown in windows as the others are: by its id, or by its role, which is its kind.
        var shown = plan with { Changes = [new PlanChange { Target = new PlanChangeTarget { Role = "outline" }, Visible = [new PlanVisibleKey(1, false)] }] };
        Assert.Empty(Paths(shown));
    }

    [Fact]
    public void AnOutlineOfTheSpec_IsShownWhileTheCameraIsNearItsAnchor()
    {
        var spec = new AnnotationSpec { Id = "edge", Kind = AnnotationKind.Outline, Points = [new CaveVector3(0, 10, 0), new CaveVector3(10, 10, 0)], Within = 25, Fade = 0 };
        var camera = new PlanCamera { Track = [.. Enumerable.Range(0, 11).Select(s => new PlanCameraKey(s, new CaveVector3(-50 + 10 * s, 0, 0), new CaveVector3(1000, 0, 0)))] };
        var plan = AnnotationPlanner.Plan([spec], null, new BoundingBox(new CaveVector3(-30, -40, 0), new CaveVector3(30, 40, 0)), camera, new PlanTimeline { Fps = 10, FrameCount = 101 });
        var keys = Assert.Single(plan.Changes).Visible!;
        Assert.Equal(("edge", true), (plan.Changes[0].Target!.Id, keys[^1].Value == false));
        Assert.Equal(2, keys.Count);
    }

    // ---- a change by id over a change by role ----

    [Fact]
    public void AChangeByIdAndOneByRole_MayHaveTheSamePropertyOfOneTarget_TheIdsWinning()
    {
        var one = new[] { new PlanNumberKey(0, 1) };
        PlanChange Of(string? id, string? role) => new() { Target = new PlanChangeTarget { Id = id, Role = role }, Opacity = one };
        // In either order.
        Assert.Empty(Paths(Full with { Changes = [Of(null, "lead-marker"), Of("lead:far.7", null)] }));
        Assert.Empty(Paths(Full with { Changes = [Of("lead:far.7", null), Of(null, "lead-marker")] }));
        // Two by id, or two by role, are still refused: and a third does not get past the second.
        Assert.Equal(["changes[1].opacity"], Paths(Full with { Changes = [Of("lead:far.7", null), Of("lead:far.7", null)] }));
        Assert.Equal(["changes[1].opacity"], Paths(Full with { Changes = [Of(null, "lead-marker"), Of(null, "lead-marker")] }));
        Assert.Equal(["changes[2].opacity"], Paths(Full with { Changes = [Of(null, "lead-marker"), Of("lead:far.7", null), Of(null, "lead-marker")] }));
        // The world's properties have no id or role: two of them are refused as they were.
        PlanChange World() => new() { WorldColor = [new PlanColorKey(0, new ColorRgb(0, 0, 1))] };
        Assert.Equal(["changes[1].worldColor"], Paths(Full with { Changes = [World(), World()] }));
    }

    // ---- a model's point, through the model's own changes ----

    private static readonly PlanTimeline Eleven = new() { Fps = 10, FrameCount = 11 };

    private static PlanCamera Still(CaveVector3 eye) => new() { Track = [new PlanCameraKey(0, eye, eye + new CaveVector3(0, 1, 0))] };

    [Fact]
    public void NearAPointOfAModel_FollowsTheModelsOffsetAndScaleKeys_FrameByFrame()
    {
        var model = new PlanModel { Id = "Cave", Path = "cave.ply", Translation = new CaveVector3(100, 0, 0) };
        var camera = Still(new CaveVector3(100, 0, 0));
        var point = new CaveVector3(30, 0, 0);
        // The model slides 100 m along X in a second: the point leaves the camera's 40 m.
        var slide = new PlanChange { Target = new PlanChangeTarget { Id = "Cave" }, Offset = [new PlanVectorKey(0, CaveVector3.Zero), new PlanVectorKey(1, new CaveVector3(100, 0, 0))] };
        var moving = AnnotationPlanner.Near(camera, Eleven, point, 40, model, [slide]);
        // The point is 30 m away, then 30 + 100 t m: within 40 until t = 0.1, which is frame 2.
        Assert.Equal([new PlanWindow(0, 0.2)], moving);
        // Without changes, or with the plain overload, it is as the model stands.
        Assert.Equal([new PlanWindow(0, null)], AnnotationPlanner.Near(camera, Eleven, point, 40, model, null));
        Assert.Empty(AnnotationPlanner.Near(camera, Eleven, point, 40));

        // Stretched along its own X: the point goes from 30 m to 90 m out.
        var stretch = new PlanChange { Target = new PlanChangeTarget { Id = "Cave" }, Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 1)), new PlanVectorKey(1, new CaveVector3(3, 1, 1))] };
        var stretched = AnnotationPlanner.Near(camera, Eleven, point, 40, model, [stretch]);
        // 30 · (1 + 2t) ≤ 40 until t = 1/6: frames 1 and 2 (t = 0, 0.1).
        Assert.Equal([new PlanWindow(0, 0.2)], stretched);

        // A turn: a quarter about the vertical takes (30, 0, 0) to (0, 30, 0); the same distance.
        var turned = model with { Rotation = new PlanRotation { Axis = CaveVector3.UnitZ, Degrees = 90 } };
        Assert.Equal(new CaveVector3(100, 30, 0), AnnotationPlanner.ModelPointAt(turned, null, point, 0).Rounded());
    }

    [Fact]
    public void TheChangeByIdHasTheModelsKeys_BeforeTheOneByRole()
    {
        var model = new PlanModel { Id = "Cave", Path = "cave.ply" };
        var byRole = new PlanChange { Target = new PlanChangeTarget { Role = "model" }, Offset = [new PlanVectorKey(0, new CaveVector3(0, 0, 50))] };
        var byId = new PlanChange { Target = new PlanChangeTarget { Id = "Cave" }, Offset = [new PlanVectorKey(0, new CaveVector3(0, 0, 7))] };
        var here = new CaveVector3(1, 2, 3);
        Assert.Equal(new CaveVector3(1, 2, 53), AnnotationPlanner.ModelPointAt(model, [byRole], here, 0));
        Assert.Equal(new CaveVector3(1, 2, 10), AnnotationPlanner.ModelPointAt(model, [byRole, byId], here, 0));
        Assert.Equal(new CaveVector3(1, 2, 10), AnnotationPlanner.ModelPointAt(model, [byId, byRole], here, 0));
        // Between keys: smooth easing, constant before the first and after the last.
        var keys = new[] { new PlanVectorKey(1, CaveVector3.Zero), new PlanVectorKey(2, new CaveVector3(10, 0, 0), PlanEasing.Smooth) };
        Assert.Equal(0, AnnotationPlanner.ModelPointAt(model, [new PlanChange { Target = new PlanChangeTarget { Id = "Cave" }, Offset = keys }], CaveVector3.Zero, 0).X);
        Assert.Equal(5, AnnotationPlanner.ModelPointAt(model, [new PlanChange { Target = new PlanChangeTarget { Id = "Cave" }, Offset = keys }], CaveVector3.Zero, 1.5).X, 9);
        Assert.Equal(10, AnnotationPlanner.ModelPointAt(model, [new PlanChange { Target = new PlanChangeTarget { Id = "Cave" }, Offset = keys }], CaveVector3.Zero, 9).X);
    }

    // ---- photos laid out ----

    private static readonly PlanTimeline TenSeconds = new() { Fps = 10, FrameCount = 101 };

    private static readonly PlanCamera FlyBy = new()
    {
        Track = [.. Enumerable.Range(0, 11).Select(s => new PlanCameraKey(s, new CaveVector3(-50 + 10 * s, 0, 0), new CaveVector3(-50 + 10 * s, 100, 0)))],
    };

    private static PhotoCard Card(string id, double x, double y = 40, double width = 6, double priority = 0, string? caption = null, double? within = 30)
        => new(id, new CaveVector3(x, y, 0), "photos/" + id + ".png", width, 1.5, priority, caption, within);

    private static IReadOnlyList<PlanVisibleKey> Keys(PhotoLayout layout, string id) => layout.Changes.Single(c => c.Target!.Id == id).Visible!;

    [Fact]
    public void PhotosOneAtATime_ThatOverlapInTime_AreSerialisedByDistanceOrByPriority()
    {
        // Two cards 10 m apart along the flight, each there within 30 m of the camera: they overlap
        // for most of the time either is there.
        var cards = new[] { Card("a", -10, 10, priority: 1), Card("b", 0, 10, priority: 5) };
        var apart = AnnotationPlanner.Layout(cards, FlyBy, TenSeconds, new PhotoLayoutOptions { Fade = 0, Gap = 0 });
        // By distance: a comes into range first and is held until it goes; b then follows.
        var a = Keys(apart, "a");
        var b = Keys(apart, "b");
        Assert.Equal(2, a.Count);
        Assert.Equal(2, b.Count);
        Assert.True(a[1].T <= b[0].T + 1e-9, "a goes before b comes");
        // No frame has two, and b is there before a is gone in its own right (it is in range at 6.8 s).
        foreach (int f in Enumerable.Range(1, 101))
        {
            double t = (f - 1) / 10.0;
            bool On(IReadOnlyList<PlanVisibleKey> keys) => keys.Where(k => k.T <= t + 1e-9).Select(k => (bool?)k.Value).LastOrDefault() ?? false;
            Assert.False(On(a) && On(b), $"both are shown at {t} s");
        }
        Assert.True(a[0].T < 2 && b[1].T > 7, $"a from {a[0].T}, b to {b[1].T}");

        // Together: both are shown at once where their times overlap.
        var together = AnnotationPlanner.Layout(cards, FlyBy, TenSeconds, new PhotoLayoutOptions { OneAtATime = false, Fade = 0 });
        Assert.True(Keys(together, "a")[0].T < Keys(together, "b")[0].T);
        Assert.True(Keys(together, "b")[0].T < Keys(together, "a")[1].T, "b comes while a is still there");

        // By priority, for a camera that stands still: a is nearer, b has the priority; b is shown
        // first, for its own time, and then a.
        var still = Still(new CaveVector3(0, -20, 0));
        var both = new[] { Card("near", 0, 5, priority: 1, within: null), Card("high", 0, 8, priority: 5, within: null) with { To = 0.5 } };
        var byDistance = AnnotationPlanner.Layout(both, still, Eleven, new PhotoLayoutOptions { Fade = 0, Gap = 0 });
        Assert.Empty(byDistance.Changes.Where(c => c.Target!.Id == "near"));          // there throughout
        Assert.Equal([(0.0, false)], Keys(byDistance, "high").Select(k => (k.T, k.Value)));  // never
        var byPriority = AnnotationPlanner.Layout(both, still, Eleven, new PhotoLayoutOptions { Order = PhotoOrder.Priority, Fade = 0, Gap = 0 });
        Assert.Equal([(0.5, true)], Keys(byPriority, "near").Select(k => (k.T, k.Value)));
        Assert.Empty(byPriority.Changes.Where(c => c.Target!.Id == "high" && c.Visible!.Count > 1));
    }

    [Fact]
    public void PhotosShownTogether_AreMovedApartOnTheScreen_AndALineJoinsEachToItsAnchor()
    {
        // The same place for both anchors; a camera that stands still.
        var camera = Still(new CaveVector3(0, -20, 0));
        var cards = new[] { Card("left", 0, 0, 4, within: null), Card("right", 0.5, 0, 4, within: null), Card("far", 40, 0, 4, within: null) };
        var layout = AnnotationPlanner.Layout(cards, camera, Eleven, new PhotoLayoutOptions { OneAtATime = false, Gap = 0.02, PictureAspect = 16.0 / 9 });
        var photos = layout.Annotations.Where(x => x.Kind == PlanAnnotationKind.Photo).ToDictionary(x => x.Id);
        // Apart on the screen, which here is X across and Z up, by the gap of 0.02 of the picture's width (20.57 m here).
        (double X, double Z) Place(string id) => (photos[id].At.X + (photos[id].Offset?.X ?? 0), photos[id].At.Z + (photos[id].Offset?.Z ?? 0));
        double gap = 0.02 * 36.0 / 35.0 * 20;
        var (l, r) = (Place("left"), Place("right"));
        Assert.True(Math.Abs(l.X - r.X) >= 4 + gap - 1e-6 || Math.Abs(l.Z - r.Z) >= 4 / 1.5 + gap - 1e-6, $"{l} and {r} still overlap");
        // Moved ones have a line to their anchor; one that did not move has none.
        Assert.NotNull(photos["left"].Offset);
        Assert.NotNull(photos["left"].LineWidth);
        Assert.Null(photos["far"].Offset);
        Assert.Null(photos["far"].LineWidth);
        // None that is moved leaves the picture: 10.3 m is its half width at that distance. The far card's anchor is off it, and it is left alone.
        Assert.All(new[] { photos["left"], photos["right"] }, p => Assert.InRange(Math.Abs((p.At + (p.Offset ?? CaveVector3.Zero)).X), 0, 10.29));
        Assert.Empty(layout.Changes);  // all the time: nothing to show or hide
    }

    [Fact]
    public void ACard_MayHaveACaptionUnderIt_ShownAndHiddenWithIt()
    {
        var camera = Still(new CaveVector3(0, -20, 0));
        var card = Card("view", 0, 0, 4, caption: "Invented hall", within: null) with { To = 0.5 };
        var layout = AnnotationPlanner.Layout([card], camera, Eleven, new PhotoLayoutOptions { Fade = 0 });
        var photo = layout.Annotations[0];
        var caption = layout.Annotations[1];
        Assert.Equal(("view-caption", PlanAnnotationKind.Label, "Invented hall", "photo-caption"), (caption.Id, caption.Kind, caption.Text, caption.Role));
        // Under the card (the picture's up is Z here), by half its height and a little more.
        Assert.True(caption.At.Z < photo.At.Z - 4 / 1.5 / 2, $"{caption.At} under {photo.At}");
        Assert.Equal(photo.At.X, caption.At.X, 6);
        Assert.Equal(Keys(layout, "view").Select(k => (k.T, k.Value)), Keys(layout, "view-caption").Select(k => (k.T, k.Value)));
        Assert.Equal([(0.5, false)], Keys(layout, "view").Select(k => (k.T, k.Value)));
        Assert.Equal("photos/view.png", photo.Image);
        Assert.Empty(layout.Notices);

        // A card that is never there is said so, and stays hidden.
        var never = AnnotationPlanner.Layout([Card("gone", 500, within: 5)], FlyBy, TenSeconds);
        Assert.Single(never.Notices);
        Assert.False(Assert.Single(Keys(never, "gone")).Value);
    }

    // ---- a reveal over a plain graph of legs ----

    private static readonly IReadOnlyList<RevealGraphLeg> Graph =
    [
        new("A", "B", new CaveVector3(0, 0, 0), new CaveVector3(10, 0, 0), new DateOnly(1990, 1, 1)),
        new("B", "C", new CaveVector3(10, 0, 0), new CaveVector3(10, 20, 0), new DateOnly(1995, 7, 2)),
        new("B", "D", new CaveVector3(10, 0, 0), new CaveVector3(30, 0, 0)),
        new("X", "Y", new CaveVector3(100, 0, 0), new CaveVector3(100, 5, 0), new DateOnly(2001, 1, 1)),
    ];

    [Fact]
    public void ARevealOfAPlainLegGraph_NeedsNoCaveModel_ByDateByOrderAndByDistance()
    {
        var byDate = RevealField.FromLegs(Graph, new RevealOptions { Source = RevealSource.Date })!;
        Assert.Equal(1, byDate.LegsWithoutDate);
        Assert.Equal(1990.0, byDate.Min, 6);
        Assert.Equal(2001.0, byDate.Max, 6);
        Assert.Equal(1990.0, byDate.Legs[2].FromValue, 6); // the leg without a date takes the earliest

        var byOrder = RevealField.FromLegs(Graph, new RevealOptions { Source = RevealSource.Order })!;
        Assert.Equal((0.0, 4.0), (byOrder.Min, byOrder.Max));

        // From A, the distances along the passages: B is 10 m, C 30 m, D 30 m. The part X-Y is measured from its first station.
        var byDistance = RevealField.FromLegs(Graph, new RevealOptions { Source = RevealSource.Distance, FromStation = "A" })!;
        Assert.Equal([0.0, 10.0, 10.0, 0.0], byDistance.Legs.Select(l => Math.Min(l.FromValue, l.ToValue)));
        Assert.Equal(30.0, byDistance.Legs.Max(l => Math.Max(l.FromValue, l.ToValue)) , 6);
        Assert.Equal(["A"], byDistance.Starts.Take(1));
        // An entrance names where its part is measured from.
        var fromD = RevealField.FromLegs(Graph, new RevealOptions { Source = RevealSource.Distance }, entrances: ["D"])!;
        Assert.Equal(30.0, fromD.Legs[0].FromValue, 6);  // A is 30 m from D along D-B-A
        Assert.Contains("D", fromD.Starts);

        Assert.Throws<ArgumentException>(() => RevealField.FromLegs(Graph, new RevealOptions { FromStation = "Nowhere" }));
        Assert.Null(RevealField.FromLegs([], new RevealOptions()));
        Assert.Null(RevealField.FromLegs([Graph[0] with { Date = null }], new RevealOptions { Source = RevealSource.Date }));
    }

    [Fact]
    public void TheRuleForARevealsKeys_IsAHelper_AndThePlanOfASpecUsesIt()
    {
        // The first key is the lowest value, the last a little past the highest by the edge or the glow.
        var change = RevealPlanner.Change(new PlanChangeTarget { Id = "Cave" }, min: 0, max: 100, start: 1, end: 5, edge: 0.03, glow: true);
        Assert.Equal([new PlanNumberKey(1, 0), new PlanNumberKey(5, 100 + 4 + 0.1)], change.Reveal);
        Assert.Equal((3.0, 4.0, 3.0), (change.RevealFront!.Edge, change.RevealFront.Glow!.Width, change.RevealFront.Glow.Strength));
        // No edge and no glow: no front; the end is kept a frame after the start.
        var hard = RevealPlanner.Change(new PlanChangeTarget { Id = "Cave" }, 0, 10, 2, 1, edge: 0, glow: false, fps: 10);
        Assert.Null(hard.RevealFront);
        Assert.Equal([2.0, 2.1], hard.Reveal!.Select(k => k.T));
        Assert.Equal(10.01, hard.Reveal![1].Value, 6);
        // The plan of a spec is made by it: ScenePlanRevealTests holds its numbers.
    }

    [Fact]
    public void TheCounter_IsAlsoForDistanceAndOrder_WithAFormatThatHasABraceBrace()
    {
        var target = new PlanChangeTarget { Id = "Cave" };
        var metres = RevealPlanner.Counter(target, 0, 123.7, "{} m", decimals: 1);
        Assert.Equal(("{} m", 1, 0.0, 123.7), (metres.Text, metres.Counter!.Decimals, metres.Counter.Min, metres.Counter.Max));
        var years = RevealPlanner.Counter(target, 1987.4, 2003.9);
        Assert.Equal(("{}", 1987.0, 2003.0), (years.Text, years.Counter!.Min, years.Counter.Max));

        // The spec asks for one by a format; without a format only a reveal by date has a counter, as before.
        Assert.Empty(ScenePlanValidatorPaths(metres));
        var reveal = new RevealSpec { By = RevealBy.Distance, CounterFormat = "{} m" };
        Assert.Empty(SceneSpecValidator.Validate(PlanFixtures.WithPaths(PlanFixtures.Preset("Orbit showcase") with { Reveal = reveal })));
        var bad = SceneSpecValidator.Validate(PlanFixtures.WithPaths(PlanFixtures.Preset("Orbit showcase") with { Reveal = reveal with { CounterFormat = "metres", CounterDecimals = 9 } }));
        Assert.Equal(["reveal.counterDecimals", "reveal.counterFormat"], bad.Select(e => e.Path).Order());
        string json = SceneSpecSerializer.Write(PlanFixtures.Preset("Orbit showcase") with { Reveal = reveal });
        Assert.Equal("{} m", SceneSpecSerializer.Read(json).Reveal!.CounterFormat);
        Assert.DoesNotContain("counterFormat", SceneSpecSerializer.Write(PlanFixtures.Preset("Orbit showcase") with { Reveal = new RevealSpec() }));
    }

    private static IEnumerable<string> ScenePlanValidatorPaths(PlanScreenItem item) => ScenePlanValidator.Validate(
        Full with { Texts = new PlanScreenTexts { Items = [item with { Counter = null, Text = item.Text }] } }).Select(e => e.Path).Where(p => p.Contains("text"));

    [Fact]
    public void SeveralModelsOfOneRole_AreRevealedFromOneTrack_AndTheValidatorTakesIt()
    {
        var plan = Full with
        {
            Models = [new PlanModel { Id = "Walls", Role = "cave", Path = "w.ply", Material = Full.Look.Materials[0].Id }, new PlanModel { Id = "Floor", Role = "cave", Path = "f.ply", Material = Full.Look.Materials[0].Id }],
            Changes = [RevealPlanner.Change(RevealPlanner.ByRole("cave"), 0, 50, 0, 3)],
            Annotations = null, Labels = null, Lines = null, Texts = null, TexturedMaterials = null,
        };
        Assert.Empty(Paths(plan));
        Assert.Equal("cave", plan.Changes![0].Target!.Role);
        Assert.Throws<ArgumentException>(() => RevealPlanner.ByRole(""));
    }

    // ---- a lamp that reaches by the day ----

    [Fact]
    public void ALampsReach_IsRaisedOnlyByTheShareOfDaylightGone()
    {
        var distances = Enumerable.Range(0, 11).Select(i => (T: i / 10.0, Distance: 6.0)).ToList();
        // All of the reach at night (80 → 320), none in full day, half of it at half.
        Assert.Equal([320.0], LampReach.Keys(distances, 80).Select(k => k.Value));
        Assert.Equal([80.0], LampReach.Keys(distances, 80, daylight: distances.Select(_ => 1.0).ToList()).Select(k => k.Value));
        Assert.Equal([200.0], LampReach.Keys(distances, 80, daylight: distances.Select(_ => 0.5).ToList()).Select(k => k.Value));
        // The day goes while the clip runs: the lamp comes up with it.
        var dusk = LampReach.Keys(distances, 80, daylight: distances.Select((_, i) => 1 - i / 10.0).ToList());
        Assert.Equal((0.0, 80.0, 1.0, 320.0), (dusk[0].T, dusk[0].Value, dusk[^1].T, dusk[^1].Value));
        Assert.Throws<ArgumentException>(() => LampReach.Keys(distances, 80, daylight: [1.0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LampReach.Keys(distances, 80, daylight: distances.Select(_ => 1.5).ToList()));
    }

    [Fact]
    public void TheKeysOfALampsReach_AreAsTheyWere_AndTakeTimeInProportionToTheTimesNotTheirSquare()
    {
        // The same keys as a straight reading of the rule: from a key, as far as every key between
        // is within the tolerance of the line to it.
        static List<(double T, double V)> Reference(List<(double T, double Distance)> d, double energy, double tolerance)
        {
            var all = d.Select(x => (x.T, V: Math.Round(LampReach.Energy(energy, x.Distance), 6))).ToList();
            var kept = new List<(double, double)> { all[0] };
            int from = 0;
            while (from < all.Count - 1)
            {
                int to = from + 1;
                for (int next = from + 2; next < all.Count; next++)
                {
                    double span = all[next].T - all[from].T;
                    bool straight = span > 0;
                    for (int i = from + 1; straight && i < next; i++)
                    {
                        double line = all[from].V + (all[next].V - all[from].V) * (all[i].T - all[from].T) / span;
                        straight = Math.Abs(line - all[i].V) <= tolerance * Math.Max(all[i].V, 1e-9);
                    }
                    if (!straight) break;
                    to = next;
                }
                kept.Add(all[to]);
                from = to;
            }
            return kept.All(k => k.Item2 == kept[0].Item2) ? [kept[0]] : kept;
        }

        var random = new Random(7);
        for (int run = 0; run < 40; run++)
        {
            double distance = 10;
            var d = Enumerable.Range(0, 120).Select(i =>
            {
                distance = Math.Max(1, distance + (random.NextDouble() - 0.5) * (run % 3 == 0 ? 0.2 : 4));
                return (T: i / 10.0, Distance: distance);
            }).ToList();
            var expected = Reference(d, 80, LampReach.DefaultTolerance);
            var actual = LampReach.Keys(d, 80).Select(k => (k.T, k.Value)).ToList();
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++) Assert.Equal(expected[i], actual[i]);
        }

        // A straight run of 200 000 times: seconds with the square, a blink without it.
        var run2 = Enumerable.Range(0, 200_000).Select(i => (T: i / 10.0, Distance: 10.0 + i * 1e-4)).ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var keys = LampReach.Keys(run2, 80, tolerance: 0.5);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"{clock.Elapsed} for 200 000 times");
        Assert.True(keys.Count < 20, $"{keys.Count} keys");
    }
}

internal static class VectorRounding
{
    public static CaveVector3 Rounded(this CaveVector3 v) => new(Math.Round(v.X, 6), Math.Round(v.Y, 6), Math.Round(v.Z, 6));
}
