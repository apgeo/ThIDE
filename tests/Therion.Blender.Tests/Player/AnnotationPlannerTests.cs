// From the annotations a person asks for to what a plan says: a station becomes a point, an open
// size a share of the cave, a time or a distance from the camera the keys of a change that shows
// and hides. Invented caves and invented camera moves only.

using Therion.Blender.Plan;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class AnnotationPlannerTests
{
    /// <summary>A camera that flies along the X axis at ten metres a second, from −50 to +50,
    /// with a pose every second, and the clip it is flown in: ten seconds at ten frames each.</summary>
    private static readonly PlanCamera FlyBy = new()
    {
        Track = [.. Enumerable.Range(0, 11).Select(s => new PlanCameraKey(s, new CaveVector3(-50 + 10 * s, 0, 0), new CaveVector3(1000, 0, 0)))],
    };

    private static readonly PlanTimeline TenSeconds = new() { Fps = 10, FrameCount = 101 };

    private static readonly BoundingBox HundredMetres = new(new CaveVector3(-30, -40, 0), new CaveVector3(30, 40, 0));

    private static AnnotationPlan Plan(params AnnotationSpec[] annotations)
        => AnnotationPlanner.Plan(annotations, meta: null, HundredMetres, FlyBy, TenSeconds);

    private static AnnotationSpec Marker(double? from = null, double? to = null, double? within = null, double fade = 0)
        => new() { Kind = AnnotationKind.Marker, At = new CaveVector3(0, 30, 0), From = from, To = to, Within = within, Fade = fade };

    private static IReadOnlyList<PlanVisibleKey> Keys(AnnotationPlan plan) => Assert.Single(plan.Changes).Visible!;

    // ---- where and what ----

    [Fact]
    public void AStation_BecomesItsPlaceInThePlansFrame_AndAPoint_IsMovedByThePlansOrigin()
    {
        var meta = PlanFixtures.MediumMeta;
        var station = meta.Stations[7];
        var world = new CaveVector3(station.Position.X + meta.Offset.X, station.Position.Y + meta.Offset.Y, station.Position.Z + meta.Offset.Z);

        var plan = AnnotationPlanner.Plan(
            [
                new AnnotationSpec { Kind = AnnotationKind.Marker, Station = station.Name },
                new AnnotationSpec { Kind = AnnotationKind.Marker, At = world },
            ],
            meta, PlanFixtures.Medium.LocalBounds, FlyBy, TenSeconds);

        var at = new CaveVector3(station.Position.X, station.Position.Y, station.Position.Z);
        Assert.True((plan.Annotations[0].At - at).Length < 1e-5, $"{plan.Annotations[0].At} for {at}");
        Assert.True((plan.Annotations[1].At - at).Length < 1e-5, $"{plan.Annotations[1].At} for {at}");
        Assert.Equal(["annotation-1", "annotation-2"], plan.Annotations.Select(a => a.Id));
        Assert.All(plan.Annotations, a => Assert.Null(a.Model));
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.Notices);
    }

    [Fact]
    public void AStationTheModelDoesNotHave_IsRefused_ByItsPlaceInTheList()
    {
        var ex = Assert.Throws<ArgumentException>(() => AnnotationPlanner.Plan(
            [new AnnotationSpec { Kind = AnnotationKind.Marker, Station = "upper0" }, new AnnotationSpec { Kind = AnnotationKind.Marker, Station = "no.such" }],
            PlanFixtures.MediumMeta, PlanFixtures.Medium.LocalBounds, FlyBy, TenSeconds));
        Assert.StartsWith("annotations[1].station: the model has no station 'no.such'.", ex.Message);

        // Without the model's stations nothing can be named by one.
        ex = Assert.Throws<ArgumentException>(() => Plan(new AnnotationSpec { Kind = AnnotationKind.Marker, Station = "upper0" }));
        Assert.StartsWith("annotations[0].station:", ex.Message);
    }

    [Fact]
    public void SizesThatAreLeftOpen_AreSharesOfTheCave_AndThoseThatAreGiven_AreTaken()
    {
        // The cave is 60 by 80 metres: a diagonal of a hundred.
        var plan = Plan(
            new AnnotationSpec { Id = "l", Kind = AnnotationKind.Label, At = CaveVector3.Zero, Text = "Sala Mare" },
            new AnnotationSpec { Id = "c", Kind = AnnotationKind.Callout, At = CaveVector3.Zero, Text = "Sifon", Role = "place-name" },
            new AnnotationSpec { Id = "m", Kind = AnnotationKind.Marker, At = CaveVector3.Zero, Color = new ColorRgb(1, 0.8, 0) },
            new AnnotationSpec { Id = "p", Kind = AnnotationKind.Photo, At = CaveVector3.Zero, Image = "/pictures/hall.jpg" },
            new AnnotationSpec { Id = "c2", Kind = AnnotationKind.Callout, At = CaveVector3.Zero, Text = "x", Size = 2, Offset = new CaveVector3(3, 0, -1) },
            new AnnotationSpec
            {
                Id = "p2", Kind = AnnotationKind.Photo, At = CaveVector3.Zero, Image = "/pictures/hall.jpg", Size = 5, Offset = new CaveVector3(0, 0, 4),
                Framed = true, Facing = PhotoFacing.Fixed, Normal = new CaveVector3(0, -1, 0),
            });

        var byId = plan.Annotations.ToDictionary(a => a.Id);
        Assert.Equal(new PlanAnnotation { Id = "l", Kind = PlanAnnotationKind.Label, Text = "Sala Mare", Size = 1.2 }, byId["l"]);
        Assert.Equal(
            new PlanAnnotation { Id = "c", Role = "place-name", Kind = PlanAnnotationKind.Callout, Text = "Sifon", Size = 1.2, Offset = new CaveVector3(0, 0, 4.8) },
            byId["c"]);
        Assert.Equal(new PlanAnnotation { Id = "m", Kind = PlanAnnotationKind.Marker, Radius = 1, Color = new ColorRgb(1, 0.8, 0) }, byId["m"]);
        Assert.Equal(new PlanAnnotation { Id = "p", Kind = PlanAnnotationKind.Photo, Image = "photos/hall.jpg", Width = 10 }, byId["p"]);
        Assert.Equal((2, new CaveVector3(3, 0, -1)), (byId["c2"].Size, byId["c2"].Offset));
        var card = byId["p2"];
        Assert.Equal((5, new CaveVector3(0, 0, 4), 0.05, PlanFacing.Fixed, new CaveVector3(0, -1, 0)), (card.Width, card.Offset, card.LineWidth, card.Facing, card.Normal));
        Assert.Equal(0.1, card.Frame!.Width);
        // A plan that says only this is one the validator takes.
        var whole = ScenePlanBuilder.WithNeededFeatures(PlanFixtures.PlanOf(PlanFixtures.Preset("Orbit showcase")) with { Annotations = plan.Annotations });
        Assert.Empty(ScenePlanValidator.Validate(whole));
    }

    [Fact]
    public void Photos_GoIntoOneFolder_UnderNamesEveryFileSystemTakes_EachFileOnce()
    {
        AnnotationSpec Photo(string image) => new() { Kind = AnnotationKind.Photo, At = CaveVector3.Zero, Image = image };

        var files = AnnotationPlanner.Files(
        [
            Photo("/trip/Sala Mare (1).JPG"), Photo("/trip/hall.png"), Photo("/other/hall.png"), Photo("/trip/hall.png"),
            Photo("C:\\poze\\șanț.jpeg"), Photo("/trip/.hidden.png"), Marker(),
        ]);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["photos/Sala_Mare__1_.JPG"] = "/trip/Sala Mare (1).JPG",
                ["photos/hall.png"] = "/trip/hall.png",
                ["photos/2-hall.png"] = "/other/hall.png",
                ["photos/_an_.jpeg"] = "C:\\poze\\șanț.jpeg",
                ["photos/hidden.png"] = "/trip/.hidden.png",
            },
            files);
        Assert.All(files.Keys, path => Assert.True(ScenePlanValidator.IsPathBesideThePlan(path)));

        var plan = Plan(Photo("/trip/hall.png"), Photo("/other/hall.png"), Photo("/trip/hall.png"));
        Assert.Equal(["photos/hall.png", "photos/2-hall.png", "photos/hall.png"], plan.Annotations.Select(a => a.Image));
        Assert.Equal(2, plan.Files.Count);
    }

    // ---- when: times ----

    [Fact]
    public void AnAnnotationWithoutATime_IsThereThroughout_AndNothingChanges()
    {
        var plan = Plan(Marker());

        Assert.Single(plan.Annotations);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void ATimeWindow_IsTwoKeys_ThatFadeItInFromItsStart_AndOutByItsEnd()
    {
        var keys = Keys(Plan(Marker(from: 2, to: 6, fade: 0.5) with { Id = "m" }));

        // It appears from 2 s over half a second, and has gone at 6 s: it starts to go at 5.5.
        Assert.Equal([new PlanVisibleKey(2, true, 0.5), new PlanVisibleKey(5.5, false, 0.5)], keys);
        Assert.Equal("m", Plan(Marker(from: 2, to: 6) with { Id = "m" }).Changes[0].Target!.Id);
        // Without a fade it is cut in and out.
        Assert.Equal([new PlanVisibleKey(2, true), new PlanVisibleKey(6, false)], Keys(Plan(Marker(from: 2, to: 6))));
    }

    [Fact]
    public void WhatIsThereAtTheStartOfTheClip_DoesNotFadeIn_AndWhatIsThereAtItsEnd_DoesNotFadeOut()
    {
        // Before its first key a thing is the other way round: one key that hides it is "there until then".
        Assert.Equal([new PlanVisibleKey(3.5, false, 0.5)], Keys(Plan(Marker(to: 4, fade: 0.5))));
        Assert.Equal([new PlanVisibleKey(3.5, false, 0.5)], Keys(Plan(Marker(from: 0, to: 4, fade: 0.5))));
        Assert.Equal([new PlanVisibleKey(7, true, 0.5)], Keys(Plan(Marker(from: 7, fade: 0.5))));
        Assert.Equal([new PlanVisibleKey(7, true, 0.5)], Keys(Plan(Marker(from: 7, to: 60, fade: 0.5))));
        // From the start to beyond the end: there throughout.
        Assert.Empty(Plan(Marker(from: 0, to: 60, fade: 0.5)).Changes);
    }

    [Fact]
    public void AWindowShorterThanItsFades_SharesItselfBetweenThem()
    {
        // Half a second, and two fades of two seconds each: two frames to appear, two to go.
        var keys = Keys(Plan(Marker(from: 3, to: 3.5, fade: 2)));

        Assert.Equal([new PlanVisibleKey(3, true, 0.2), new PlanVisibleKey(3.3, false, 0.2)], keys);
        // One frame: no fade at all, and the keys stand on frames of their own.
        Assert.Equal([new PlanVisibleKey(3, true), new PlanVisibleKey(3.1, false)], Keys(Plan(Marker(from: 3, to: 3.1, fade: 2))));
    }

    [Fact]
    public void ATimeAfterTheClip_HidesTheAnnotationThroughout_AndIsSaid()
    {
        var plan = Plan(Marker(from: 30, to: 40) with { Id = "late" });

        Assert.Equal([new PlanVisibleKey(0, false)], Keys(plan));
        Assert.Equal(["the annotation 'late' is not shown: its time lies outside the clip, or the camera is not near it then"], plan.Notices);
    }

    // ---- when: the camera is near ----

    [Fact]
    public void WhileTheCameraIsWithinADistance_IsTheFramesOnWhichItIs()
    {
        // The anchor is 30 m beside the path: the camera is within 50 m of it from x = −40 to +40,
        // which is from 1 s to 9 s.
        var windows = AnnotationPlanner.Near(FlyBy, TenSeconds, new CaveVector3(0, 30, 0), 50);
        Assert.Equal([new PlanWindow(1, 9.1)], windows);

        var keys = Keys(Plan(Marker(within: 50, fade: 0.5)));
        Assert.Equal([new PlanVisibleKey(1, true, 0.5), new PlanVisibleKey(8.6, false, 0.5)], keys);

        // Frame by frame: there on exactly the frames the camera is that near.
        for (int frame = 1; frame <= 101; frame++)
        {
            double distance = (AnnotationPlanner.EyeAt(FlyBy, frame, 10) - new CaveVector3(0, 30, 0)).Length;
            bool near = distance <= 50, inWindow = frame >= 11 && frame < 92;
            Assert.True(near == inWindow, $"frame {frame}: {distance:F2} m away, in the window {inWindow}");
        }
    }

    [Fact]
    public void ACameraThatComesNearTwice_ShowsItTwice_AndOneThatNeverDoes_NotAtAll()
    {
        // There and back: near from 1 s to 4 s and again from 6 s to 9 s.
        var thereAndBack = new PlanCamera
        {
            Track = [.. Enumerable.Range(0, 11).Select(s => new PlanCameraKey(s, new CaveVector3(-50 + 20 * (s <= 5 ? s : 10 - s), 0, 0), new CaveVector3(0, 30, 0)))],
        };
        var plan = AnnotationPlanner.Plan([Marker(within: 31) with { At = new CaveVector3(0, 30, 0) }], null, HundredMetres, thereAndBack, TenSeconds);
        var near = AnnotationPlanner.Near(thereAndBack, TenSeconds, new CaveVector3(0, 30, 0), 31);

        Assert.Equal(2, near.Count);
        var keys = Keys(plan);
        Assert.Equal([true, false, true, false], keys.Select(k => k.Value));
        Assert.Equal([near[0].From, near[0].To!.Value, near[1].From, near[1].To!.Value], keys.Select(k => k.T));
        Assert.True(near[0].To < 5 && near[1].From > 5, $"{near[0]} and {near[1]}");

        var never = Plan(Marker(within: 10) with { Id = "far" });
        Assert.Equal([new PlanVisibleKey(0, false)], Keys(never));
        Assert.Equal(["the camera never comes within 10 m of the annotation 'far'; it is not shown"], never.Notices);
        // A camera that is near from the first frame to the last: there throughout.
        Assert.Empty(Plan(Marker(within: 500)).Changes);
    }

    [Fact]
    public void ATimeAndADistance_AreBothHeldTo()
    {
        // Near from 1 s to 9 s, and wanted from 4 s to 20 s: there from 4 s to 9 s.
        var keys = Keys(Plan(Marker(from: 4, to: 20, within: 50)));

        Assert.Equal([new PlanVisibleKey(4, true), new PlanVisibleKey(9.1, false)], keys);
    }

    [Fact]
    public void TheCameraOfAPlan_IsWhereItsPosesPutIt_BetweenThemToo()
    {
        Assert.Equal(new CaveVector3(-50, 0, 0), AnnotationPlanner.EyeAt(FlyBy, 1, 10));
        Assert.Equal(new CaveVector3(-45, 0, 0), AnnotationPlanner.EyeAt(FlyBy, 6, 10));
        Assert.Equal(new CaveVector3(50, 0, 0), AnnotationPlanner.EyeAt(FlyBy, 101, 10));
        Assert.Equal(new CaveVector3(50, 0, 0), AnnotationPlanner.EyeAt(FlyBy, 500, 10));
        // A camera that jumps from pose to pose stands on the earlier one meanwhile.
        var stills = FlyBy with { Interpolation = PlanInterpolation.Constant };
        Assert.Equal(new CaveVector3(-50, 0, 0), AnnotationPlanner.EyeAt(stills, 10, 10));
        Assert.Equal(new CaveVector3(-40, 0, 0), AnnotationPlanner.EyeAt(stills, 11, 10));
        // One pose is a camera that stands still.
        var still = new PlanCamera { Track = [new PlanCameraKey(0, new CaveVector3(1, 2, 3), CaveVector3.Zero)] };
        Assert.Equal(new CaveVector3(1, 2, 3), AnnotationPlanner.EyeAt(still, 50, 10));
    }

    [Fact]
    public void Shown_IsForWhoeverWritesAPlan_AndGivesKeysTheValidatorTakes()
    {
        var plan = PlanFixtures.Full();
        var timeline = plan.Timeline;   // 51 frames at 25 a second: two seconds

        var change = AnnotationPlanner.Shown("dig", [new PlanWindow(0.4, 0.8), new PlanWindow(0.8, 1.2), new PlanWindow(1.6, null)], timeline, 0.08, 0.16)!;

        // Two windows that touch are one; the last runs to the end and does not fade out.
        Assert.Equal(
            [new PlanVisibleKey(0.4, true, 0.08), new PlanVisibleKey(1.04, false, 0.16), new PlanVisibleKey(1.6, true, 0.08)],
            change.Visible);
        Assert.Empty(ScenePlanValidator.Validate(plan with { Changes = [change] }));
        Assert.Null(AnnotationPlanner.Shown("dig", [new PlanWindow(0, null)], timeline));
        Assert.Equal([new PlanVisibleKey(0, false)], AnnotationPlanner.Shown("dig", [], timeline)!.Visible);
    }

    // ---- from a spec ----

    [Fact]
    public void FromSpec_CarriesTheAnnotations_TheirChanges_AndListsTheModules()
    {
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Annotations =
            [
                new AnnotationSpec { Id = "way-in", Kind = AnnotationKind.Callout, Station = "upper0", Text = "Intrarea" },
                new AnnotationSpec { Kind = AnnotationKind.Photo, Station = "lower3", Image = "/pictures/hall.jpg", From = 1, To = 3 },
            ],
        };

        var plan = PlanFixtures.PlanOf(spec);

        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Equal(["way-in", "annotation-2"], plan.Annotations!.Select(a => a.Id));
        Assert.Equal("photos/hall.jpg", plan.Annotations![1].Image);
        var features = plan.Features.ToList();
        Assert.True(features.IndexOf("camera_track") < features.IndexOf("annotations") && features.IndexOf("annotations") < features.IndexOf("changes"));
        var shown = Assert.Single(plan.Changes!, c => c.Target?.Id == "annotation-2");
        Assert.Equal([new PlanVisibleKey(1, true, 0.5), new PlanVisibleKey(2.5, false, 0.5)], shown.Visible);
        Assert.Equal(new Dictionary<string, string> { ["photos/hall.jpg"] = "/pictures/hall.jpg" }, ScenePlanBuilder.AnnotationFiles(spec));
        Assert.Empty(ScenePlanBuilder.NotCarried(spec));
        Assert.Empty(ScenePlanBuilder.Notices(PlanFixtures.WithPaths(spec), PlanFixtures.MediumMeta, PlanFixtures.MediumFraming));

        // A spec without annotations has the plan it had.
        var bare = PlanFixtures.PlanOf(PlanFixtures.Preset("Orbit showcase"));
        Assert.Null(bare.Annotations);
        Assert.DoesNotContain("annotations", bare.Features);

        // A station the model does not have stops the plan, by name.
        var wrong = spec with { Annotations = [new AnnotationSpec { Kind = AnnotationKind.Marker, Station = "nowhere9" }] };
        Assert.Contains("the model has no station 'nowhere9'", Assert.Throws<ArgumentException>(() => PlanFixtures.PlanOf(wrong)).Message);
    }

    [Fact]
    public void Notices_NameTheAnnotationTheCameraNeverComesNear()
    {
        var spec = PlanFixtures.WithPaths(PlanFixtures.Preset("Orbit showcase") with
        {
            Annotations = [new AnnotationSpec { Id = "close-up", Kind = AnnotationKind.Marker, Station = "upper0", Within = 0.01 }],
        });

        var notices = ScenePlanBuilder.Notices(spec, PlanFixtures.MediumMeta, PlanFixtures.MediumFraming);

        Assert.Equal(["the camera never comes within 0.01 m of the annotation 'close-up'; it is not shown"], notices);
        // Without the framing nothing is known of the camera.
        Assert.Empty(ScenePlanBuilder.Notices(spec, PlanFixtures.MediumMeta));
    }

    [Fact]
    public void AStill_ShowsWhatIsThereOnItsOneFrame()
    {
        var still = new PlanTimeline { Fps = 10, FrameCount = 1 };

        var plan = AnnotationPlanner.Plan(
            [Marker(from: 0, to: 4) with { Id = "now" }, Marker(from: 2) with { Id = "later" }], null, HundredMetres, FlyBy, still);

        Assert.Equal("later", Assert.Single(plan.Changes).Target!.Id);
        Assert.Equal([new PlanVisibleKey(0, false)], plan.Changes[0].Visible);
    }
}
