// The overlays of a SceneSpec as the texts of a plan: what is written over the picture, where,
// how large and when, with the north arrow's heading and the scale bar's length worked out from
// the camera's poses. No Blender: the drawing is in PlayerTextsBlenderTests.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class ScreenTextPlannerTests
{
    private static SceneSpec Orbit(OverlaySpec overlays, OutputKind kind = OutputKind.Video) => PlanFixtures.Preset("Orbit showcase") with
    {
        Labels = new LabelsSpec { Overlays = overlays },
        Animation = new AnimationSpec { Fps = 10, DurationSeconds = 4 },
        Output = new OutputSpec { Kind = kind, Width = 640, Height = 360 },
    };

    private static PlanScreenTexts Texts(SceneSpec spec) => PlanFixtures.PlanOf(spec).Texts!;

    private static PlanScreenItem Item(PlanScreenTexts texts, string id) => texts.Items.Single(i => i.Id == id);

    /// <summary>A camera that looks along the ground in the given compass direction.</summary>
    private static CameraKeyframe Looking(int frame, double headingDegrees, double down = 0)
    {
        double heading = headingDegrees * Math.PI / 180;
        return new CameraKeyframe(frame, CaveVector3.Zero, new CaveVector3(Math.Sin(heading) * 10, Math.Cos(heading) * 10, -down), null);
    }

    private static CameraPlan Camera(KeyInterpolation interpolation, params CameraKeyframe[] keys)
        => new() { FrameCount = keys[^1].Frame, Keyframes = keys, Interpolation = interpolation };

    // ---- what a spec asks for ----

    [Fact]
    public void ASpecWithoutOverlays_HasNoTexts_AndItsPlanNeedsNoSecondStage()
    {
        var plan = PlanFixtures.PlanOf(Orbit(new OverlaySpec()));

        Assert.Null(plan.Texts);
        Assert.False(plan.HasTexts);
        Assert.DoesNotContain(ScenePlan.TextsFeature, plan.Features);
        // The built-in presets write the cave's name; without that, nothing.
        Assert.All(
            Therion.Blender.Presets.BuiltInPresets.All,
            preset => Assert.Null(PlanFixtures.PlanOf(preset.Spec with { Labels = preset.Spec.Labels with { Overlays = new OverlaySpec() } }).Texts));
    }

    [Fact]
    public void ATitle_StandsAtTheTopOfThePicture_Throughout_AndThePlanListsTheModuleThatDrawsIt()
    {
        var plan = PlanFixtures.PlanOf(Orbit(new OverlaySpec { Title = "Peștera\nInventată", Subtitle = "ridicare 2026" }));

        Assert.Equal(["models", "camera_track", "look", "texts", "output"], plan.Features);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        var title = Item(plan.Texts!, "title");
        Assert.Equal(
            (PlanScreenItemKind.Text, "title", "Peștera Inventată", new PlanPoint(0.5, 0.95), PlanAlignX.Center, PlanAlignY.Top),
            (title.Kind, title.Role, title.Text, title.At, title.AlignX, title.AlignY));
        Assert.Equal((0.06, null, null, null), (title.Size, title.Start, title.End, title.FadeOut));
        var subtitle = Item(plan.Texts!, "subtitle");
        Assert.Equal(("subtitle", "ridicare 2026", PlanAlignY.Top), (subtitle.Role, subtitle.Text, subtitle.AlignY));
        Assert.True(subtitle.At.Y < title.At.Y - title.Size && subtitle.Size < title.Size);
        Assert.Equal(0, plan.Texts!.Hold);
    }

    [Fact]
    public void ATitleWithATime_IsACardInTheMiddle_ThatFadesWhenItsTimeIsUp()
    {
        var texts = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", Subtitle = "a survey", TitleSeconds = 2.5 }));

        var title = Item(texts, "title");
        Assert.Equal((0.5, PlanAlignY.Bottom, 3.3, 0.8), (title.At.X, title.AlignY, title.End, title.FadeOut));
        Assert.InRange(title.At.Y, 0.45, 0.6);
        Assert.True(title.Size > 0.06);
        var subtitle = Item(texts, "subtitle");
        Assert.Equal((PlanAlignY.Top, 3.3, 0.8), (subtitle.AlignY, subtitle.End, subtitle.FadeOut));
        Assert.True(subtitle.At.Y < title.At.Y);

        // Without a subtitle the card's title is centred on the picture's middle.
        var alone = Item(Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2.5 })), "title");
        Assert.Equal((new PlanPoint(0.5, 0.5), PlanAlignY.Center), (alone.At, alone.AlignY));
    }

    [Fact]
    public void Credits_AndACornerLine_StayInTheirCorners_InsideTheMargin()
    {
        var texts = Texts(Orbit(new OverlaySpec { Credits = "Survey: the invented team", Corner = "Bihor, 2026" }));

        var credits = Item(texts, "credits");
        Assert.Equal(
            ("credits", new PlanPoint(0.95, 0.05), PlanAlignX.Right, PlanAlignY.Bottom, null, null),
            (credits.Role, credits.At, credits.AlignX, credits.AlignY, credits.Start, credits.End));
        var corner = Item(texts, "corner");
        Assert.Equal(("corner", new PlanPoint(0.05, 0.95), PlanAlignX.Left, PlanAlignY.Top), (corner.Role, corner.At, corner.AlignX, corner.AlignY));
        Assert.True(credits.Size < corner.Size);
        Assert.All(texts.Items, i => Assert.True(
            i.At.X >= ScreenTextPlanner.Margin - 1e-9 && i.At.X <= 1 - ScreenTextPlanner.Margin + 1e-9
            && i.At.Y >= ScreenTextPlanner.Margin - 1e-9 && i.At.Y <= 1 - ScreenTextPlanner.Margin + 1e-9, i.Id));
    }

    [Fact]
    public void Captions_ShowOneAtATime_OnABacking_AtTheBottom_AndCornerCaptionsUnderTheCornerLine()
    {
        var texts = Texts(Orbit(new OverlaySpec
        {
            Captions =
            [
                new CaptionSpec { Text = "second", Start = 2, End = 3.5 },
                new CaptionSpec { Text = "first", Start = 0, End = 2.5 },
            ],
            Corner = "Bihor",
            CornerCaptions = [new CaptionSpec { Text = "-120 m", Start = 1, End = 2 }],
        }));

        var first = Item(texts, "caption.1");
        var second = Item(texts, "caption.2");
        Assert.Equal(("first", null, 2.0, null, 0.2), (first.Text, first.Start, first.End, first.FadeIn, first.FadeOut));
        Assert.Equal(("second", 2.0, 3.5, 0.2, 0.2), (second.Text, second.Start, second.End, second.FadeIn, second.FadeOut));
        Assert.All(new[] { first, second }, c =>
        {
            Assert.Equal(("caption", "caption", true, PlanAlignY.Bottom, 0.5), (c.Role, c.Slot, c.Backing, c.AlignY, c.At.X));
            Assert.InRange(c.At.Y, 0.08, 0.2);
        });

        var corner = Item(texts, "corner.1");
        Assert.Equal(("-120 m", "corner", "corner", 1.0, 2.0, PlanAlignX.Left), (corner.Text, corner.Role, corner.Slot, corner.Start, corner.End, corner.AlignX));
        Assert.True(corner.At.Y < Item(texts, "corner").At.Y);
    }

    [Fact]
    public void AnEndCard_HoldsTheLastFrame_UnderABlackCardWithItsText()
    {
        var plan = PlanFixtures.PlanOf(Orbit(new OverlaySpec
        {
            Title = "Invented Cave",
            EndCard = new EndCardSpec { Text = "Thank you", Subtitle = "speotopo.example", Seconds = 3 },
        }));
        var texts = plan.Texts!;

        Assert.Equal(3, texts.Hold);
        Assert.Equal(40 + 30, plan.OutputFrameCount);
        var card = Item(texts, "end-card");
        Assert.Equal(
            (PlanScreenItemKind.Box, "end-card", new PlanPoint(0.5, 0.5), 1.0, 1.0, new ColorRgb(0, 0, 0), 4.0, 0.5),
            (card.Kind, card.Role, card.At, card.Width, card.Height, card.Color, card.Start, card.FadeIn));
        var text = Item(texts, "end-card-text");
        Assert.Equal(("Thank you", 4.0, 0.5), (text.Text, text.Start, text.FadeIn));
        Assert.Equal("speotopo.example", Item(texts, "end-card-subtitle").Text);
        // The card lies over everything that was there before it, and under its own texts.
        var order = texts.Items.Select(i => i.Id).ToList();
        Assert.True(order.IndexOf("title") < order.IndexOf("end-card"));
        Assert.True(order.IndexOf("end-card") < order.IndexOf("end-card-text"));
    }

    [Fact]
    public void Fades_AreTimesOfTheTextsAsAWhole_AndAloneTheyNeedTheSecondStage()
    {
        var plan = PlanFixtures.PlanOf(Orbit(new OverlaySpec { FadeFromBlack = 1.5, FadeToBlack = 2 }));

        Assert.Equal((1.5, 2.0), (plan.Texts!.FromBlack, plan.Texts.ToBlack));
        Assert.Empty(plan.Texts.Items);
        Assert.True(plan.HasTexts);
        Assert.Contains(ScenePlan.TextsFeature, plan.Features);
    }

    [Fact]
    public void AStill_ShowsWhatIsThereAtItsStart_AndHasNoCardNoFadeAndNoTime()
    {
        var texts = Texts(Orbit(
            new OverlaySpec
            {
                Title = "Invented Cave", TitleSeconds = 2, Credits = "the team", NorthArrow = true, ScaleBar = true,
                EndCard = new EndCardSpec { Text = "The end" }, FadeFromBlack = 1, FadeToBlack = 1,
                Captions = [new CaptionSpec { Text = "at once", Start = 0, End = 2 }, new CaptionSpec { Text = "later", Start = 2, End = 3 }],
            },
            OutputKind.Still));

        Assert.Equal((0, 0, 0), (texts.Hold, texts.FromBlack, texts.ToBlack));
        Assert.Equal(
            ["scale-bar", "scale-bar-text", "north-arrow", "credits", "title", "caption.1"],
            texts.Items.Select(i => i.Id));
        Assert.All(texts.Items, i => Assert.Equal((null, null, null, null), (i.Start, i.End, i.FadeIn, i.FadeOut)));
        Assert.Single(Item(texts, "north-arrow").Turn!);
    }

    // ---- depth legend ----

    [Fact]
    public void TheDepthLegend_IsAColumnOfTheDepthTintsColours_TheTopOfTheCaveFirst()
    {
        var texts = Texts(Orbit(new OverlaySpec { DepthLegend = true }));

        var swatches = texts.Items.Where(i => i.Role == "depth-legend").ToList();
        Assert.Equal(DepthRamp.GradientStops.Count, swatches.Count);
        Assert.All(swatches, s => Assert.Equal(PlanScreenItemKind.Box, s.Kind));
        Assert.Equal(swatches.Select(s => s.At.Y).OrderDescending(), swatches.Select(s => s.At.Y));
        Assert.Single(swatches.Select(s => s.At.X).Distinct());
        // Square in a 16:9 picture: a share of the width that is 9/16 of the share of the height.
        Assert.Equal(swatches[0].Height!.Value * 360 / 640, swatches[0].Width!.Value, 6);
        var top = ScriptGenerator.FromSrgb(DepthRamp.GradientStops[0]);
        Assert.Equal(top.R, swatches[0].Color!.Value.R, 5);
        Assert.Equal(top.B, swatches[0].Color!.Value.B, 5);
        Assert.True(swatches[0].Color!.Value.R > swatches[^1].Color!.Value.R, "warm at the top, cool at the bottom");
    }

    // ---- scale bar ----

    [Theory]
    [InlineData(0.9, 0.5)]
    [InlineData(1, 1)]
    [InlineData(1.99, 1)]
    [InlineData(2, 2)]
    [InlineData(4.9, 2)]
    [InlineData(5, 5)]
    [InlineData(99, 50)]
    [InlineData(1234, 1000)]
    [InlineData(0.03, 0.02)]
    public void AScaleBarsLength_IsTheLongestRoundOneThatFits(double room, double expected)
        => Assert.Equal(expected, ScreenTextPlanner.RoundLengthUpTo(room)!.Value, 9);

    [Theory]
    [InlineData(50, "50 m")]
    [InlineData(1, "1 m")]
    [InlineData(2000, "2 km")]
    [InlineData(0.5, "50 cm")]
    [InlineData(0.02, "2 cm")]
    [InlineData(0.005, "5 mm")]
    public void ALength_IsWrittenInTheUnitThatReadsBest(double metres, string expected)
        => Assert.Equal(expected, ScreenTextPlanner.Length(metres));

    [Fact]
    public void RoundLengthUpTo_HasNoAnswerForNoRoom()
    {
        Assert.Null(ScreenTextPlanner.RoundLengthUpTo(0));
        Assert.Null(ScreenTextPlanner.RoundLengthUpTo(double.NaN));
    }

    [Fact]
    public void TheScaleBar_IsARoundLength_AsWideAsThatLengthIsAtTheDistanceTheCameraLooksFrom()
    {
        var spec = Orbit(new OverlaySpec { ScaleBar = true });
        var plan = PlanFixtures.PlanOf(spec);
        var bar = Item(plan.Texts!, "scale-bar");
        var label = Item(plan.Texts!, "scale-bar-text");

        // An orbit keeps its distance: the picture is sensor / lens metres wide at one metre.
        var pose = plan.Camera.Track[0];
        double distance = (pose.Target - pose.Eye).Length;
        double shareOfAMetre = plan.Camera.Lens / (plan.Camera.SensorWidth * distance);
        double metres = bar.Width!.Value / shareOfAMetre;
        Assert.Equal(ScreenTextPlanner.Length(Math.Round(metres, 2)), label.Text);
        Assert.Equal(ScreenTextPlanner.RoundLengthUpTo(0.25 / shareOfAMetre)!.Value, metres, 3);
        Assert.InRange(bar.Width.Value, 0.25 / 2.5 - 1e-6, 0.25 + 1e-6);
        // The bar starts at the margin and its length is written over its middle.
        Assert.Equal(ScreenTextPlanner.Margin, bar.At.X - bar.Width.Value / 2, 5);
        Assert.Equal(bar.At.X, label.At.X, 6);
        Assert.True(label.At.Y > bar.At.Y);
        Assert.Equal((null, null), (bar.Start, bar.End));
    }

    [Fact]
    public void EveryStillOfAStillSet_HasAScaleBarOfItsOwn_ForItsOwnTime()
    {
        var spec = PlanFixtures.Preset("Documentation stills") with
        {
            Labels = new LabelsSpec { Overlays = new OverlaySpec { ScaleBar = true } },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.StillSet,
                Stills = new StillSetParams
                {
                    Views = [StandardView.Top],
                    CustomViews = [new CameraViewpoint { Position = new CaveVector3(0, -20000, 0), Target = CaveVector3.Zero }],
                },
            },
            Animation = new AnimationSpec { Fps = 2, DurationSeconds = 1 },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 320, Height = 320 },
        };
        var texts = Texts(spec);

        var bars = texts.Items.Where(i => i.Kind == PlanScreenItemKind.Box).ToList();
        Assert.Equal(["scale-bar.1", "scale-bar.2"], bars.Select(b => b.Id));
        Assert.Equal((null, 0.5), (bars[0].Start, bars[0].End));
        Assert.Equal((0.5, null), (bars[1].Start, bars[1].End));
        // From twenty kilometres away a bar says kilometres.
        Assert.EndsWith(" km", Item(texts, "scale-bar-text.2").Text);
        Assert.NotEqual(Item(texts, "scale-bar-text.1").Text, Item(texts, "scale-bar-text.2").Text);
    }

    // ---- north arrow ----

    [Fact]
    public void Headings_AreClockwiseFromNorth_AndGoOnPast360WithoutAJump()
    {
        var camera = Camera(KeyInterpolation.Linear,
            Looking(1, 0), Looking(2, 90), Looking(3, 180), Looking(4, 270), Looking(5, 0), Looking(6, 90));

        var headings = ScreenTextPlanner.Headings(camera, 6);

        Assert.Equal([0, 90, 180, 270, 360, 450], headings.Select(h => Math.Round(h, 6)));
    }

    [Fact]
    public void AHeading_TurnsTheShortWay_AndCounterClockwiseIsNegative()
    {
        var camera = Camera(KeyInterpolation.Linear, Looking(1, 10), Looking(2, 350), Looking(3, 300));

        Assert.Equal([10, -10, -60], ScreenTextPlanner.Headings(camera, 3).Select(h => Math.Round(h, 6)));
    }

    [Fact]
    public void ACameraThatLooksStraightDownOrUp_HasNorthAtTheTopOfItsPicture()
    {
        var down = new CameraKeyframe(1, new CaveVector3(5, 5, 100), new CaveVector3(5, 5, 0), null);
        var up = new CameraKeyframe(2, new CaveVector3(5, 5, -100), new CaveVector3(5, 5, 0), null);

        Assert.Equal([0, 0], ScreenTextPlanner.Headings(Camera(KeyInterpolation.Constant, down, up), 2));
        // Tilted down, it still has a heading.
        Assert.Equal(90, ScreenTextPlanner.Headings(Camera(KeyInterpolation.Linear, Looking(1, 90, down: 500)), 1)[0], 6);
    }

    [Fact]
    public void BetweenTwoPoses_TheHeadingIsThatOfTheCameraOnItsWay()
    {
        // The camera stands still and its target moves from north to east in a straight line.
        var camera = Camera(KeyInterpolation.Linear, Looking(1, 0), Looking(11, 90));

        var headings = ScreenTextPlanner.Headings(camera, 11);

        Assert.Equal(45, headings[5], 6);                       // half way along the chord
        Assert.Equal(Math.Atan2(1, 9) * 180 / Math.PI, headings[1], 6);
        // An eased track is slower at its ends: after a tenth of the time it has turned less.
        var eased = ScreenTextPlanner.Headings(camera with { Interpolation = KeyInterpolation.Bezier }, 11);
        Assert.True(eased[1] < headings[1]);
        Assert.Equal(45, eased[5], 6);
        // A track that jumps holds each pose until the next.
        var held = ScreenTextPlanner.Headings(camera with { Interpolation = KeyInterpolation.Constant }, 11);
        Assert.Equal([0, 0, 90], [held[0], held[9], held[10]]);
    }

    [Fact]
    public void AnEasedCurve_IsLevelAtItsEndsAndTurningPoints_AndSmoothBetween()
    {
        // Two keys: level at both, which is the plain ease in and out.
        Assert.Equal([0, 0], ScreenTextPlanner.EasedSlopes([1, 11], [0, 10]));
        // A peak in the middle is level too.
        Assert.Equal([0, 0, 0], ScreenTextPlanner.EasedSlopes([1, 11, 21], [0, 10, 0]));
        // A key on the way up, between two level ones ten frames away on either side: the curve
        // passes it at one and a half times the slope of the straight line.
        var rising = ScreenTextPlanner.EasedSlopes([1, 11, 21], [0, 10, 20]);
        Assert.Equal([0, 1.5, 0], rising.Select(s => Math.Round(s, 9)));
        // Where the straight line to one neighbour is nearly level, the slope is held back to
        // three times that line's, so the curve does not dip below the neighbour.
        var held = ScreenTextPlanner.EasedSlopes([1, 11, 21], [0, 0.1, 20]);
        Assert.Equal(0.03, held[1], 9);
    }

    [Fact]
    public void Turns_LeaveOutTheFramesThatLieOnTheLineBetweenTheirNeighbours()
    {
        // Steady for ten frames, then twice as fast, then still.
        double[] headings = [.. Enumerable.Range(0, 11).Select(i => i * 3.0), .. Enumerable.Range(1, 5).Select(i => 30 + i * 6.0), 60, 60, 60];

        var turns = ScreenTextPlanner.Turns(headings, fps: 10);

        Assert.Equal([new PlanTurn(0, 0), new PlanTurn(1, 30), new PlanTurn(1.5, 60)], turns);
        Assert.Equal([new PlanTurn(0, 12.5)], ScreenTextPlanner.Turns([12.5, 12.5, 12.5], 10));
        Assert.Equal([new PlanTurn(0, 0), new PlanTurn(0.1, 90)], ScreenTextPlanner.Turns([0, 90], 10));
        Assert.Empty(ScreenTextPlanner.Turns([], 10));
    }

    [Fact]
    public void AJumpFromOneStillToTheNext_IsATurnOnEachSideOfIt()
    {
        var turns = ScreenTextPlanner.Turns([0, 0, 0, 90, 90, 90], fps: 1);

        Assert.Equal([new PlanTurn(0, 0), new PlanTurn(2, 0), new PlanTurn(3, 90)], turns);
    }

    [Fact]
    public void TheNorthArrow_OfAnOrbit_GoesOnceRoundWithTheCamera()
    {
        var plan = PlanFixtures.PlanOf(Orbit(new OverlaySpec { NorthArrow = true }));
        var arrow = Item(plan.Texts!, "north-arrow");

        Assert.Equal((PlanScreenItemKind.Arrow, "north-arrow", "N"), (arrow.Kind, arrow.Role, arrow.Text));
        Assert.True(arrow.At.X > 0.8 && arrow.At.Y > 0.8, "top right");
        var turns = arrow.Turn!;
        Assert.Equal(0, turns[0].T);
        Assert.Equal(3.9, turns[^1].T, 6);
        Assert.Equal(360, Math.Abs(turns[^1].Degrees - turns[0].Degrees), 0);
        Assert.Equal(turns.Select(t => t.T).Order(), turns.Select(t => t.T));
        // The first heading is the one of the first pose: from the eye to the target, over the ground.
        var pose = plan.Camera.Track[0];
        double heading = Math.Atan2(pose.Target.X - pose.Eye.X, pose.Target.Y - pose.Eye.Y) * 180 / Math.PI;
        Assert.Equal(heading, turns[0].Degrees, 2);
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Fact]
    public void TheTitleAndTheEndCard_FadeForAsLongAsTheSpecSays()
    {
        // As it always was: a title is there at once, a card goes in 0.8 s, an end card comes in half a second.
        var plain = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2, EndCard = new EndCardSpec { Text = "The end", Seconds = 3 } }));
        Assert.Equal((null, 0.8, 2.8), (Item(plain, "title").FadeIn, Item(plain, "title").FadeOut, Item(plain, "title").End));
        Assert.Equal(0.5, Item(plain, "end-card-text").FadeIn);

        var slow = Texts(Orbit(new OverlaySpec
        {
            Title = "Invented Cave", Subtitle = "a survey", TitleSeconds = 2, TitleFade = 1.5,
            EndCard = new EndCardSpec { Text = "The end", Subtitle = "thanks", Seconds = 3, Fade = 2.5 },
        }));
        Assert.All(new[] { "title", "subtitle" }, id => Assert.Equal((1.5, 1.5, 3.5), (Item(slow, id).FadeIn, Item(slow, id).FadeOut, Item(slow, id).End)));
        Assert.All(new[] { "end-card", "end-card-text", "end-card-subtitle" }, id => Assert.Equal(2.5, Item(slow, id).FadeIn));

        // No fade at all is a cut; a title that stays fades in and never out.
        var cut = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2, TitleFade = 0, EndCard = new EndCardSpec { Text = "The end", Fade = 0 } }));
        Assert.Equal((null, null, 2.0), (Item(cut, "title").FadeIn, Item(cut, "title").FadeOut, Item(cut, "title").End));
        Assert.Equal(0.0, Item(cut, "end-card-text").FadeIn);
        var staying = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleFade = 1 }));
        Assert.Equal((1.0, null, null), (Item(staying, "title").FadeIn, Item(staying, "title").FadeOut, Item(staying, "title").End));

        // The spec says them only when they are set, and is held to what a card can do.
        var spec = Orbit(new OverlaySpec { Title = "T", TitleFade = 1, EndCard = new EndCardSpec { Text = "E", Seconds = 3, Fade = 2 } });
        string json = SceneSpecSerializer.Write(spec);
        Assert.Contains("\"titleFade\": 1", json);
        Assert.Contains("\"fade\": 2", json);
        Assert.Equal((1.0, 2.0), (SceneSpecSerializer.Read(json).Labels.Overlays.TitleFade, SceneSpecSerializer.Read(json).Labels.Overlays.EndCard!.Fade));
        Assert.DoesNotContain("titleFade", SceneSpecSerializer.Write(Orbit(new OverlaySpec { Title = "T", EndCard = new EndCardSpec { Text = "E" } })));
        Assert.DoesNotContain("\"fade\"", SceneSpecSerializer.Write(Orbit(new OverlaySpec { Title = "T", EndCard = new EndCardSpec { Text = "E" } })));
        var errors = SceneSpecValidator.Validate(PlanFixtures.WithPaths(Orbit(new OverlaySpec { Title = "T", TitleFade = -1, EndCard = new EndCardSpec { Text = "E", Seconds = 3, Fade = 4 } })));
        Assert.Equal(["labels.overlays.endCard.fade", "labels.overlays.titleFade"], errors.Select(e => e.Path).Order());
        Assert.Contains("labels.overlays.titleFade", SceneBuilders.NotDrawnByTheGenerator(spec));
    }

    // ---- a picture that is higher than wide ----

    /// <summary>The columns an item takes, as shares of the picture's width: a title at most
    /// its widest line about the middle, an arrow its own size and its letter's room.</summary>
    private static (double Left, double Right) Columns(PlanScreenItem item, double aspect) => item.Kind == PlanScreenItemKind.Arrow
        ? (item.At.X - (item.Size!.Value / 2 + item.Size.Value * 0.45) / aspect, item.At.X + (item.Size.Value / 2 + item.Size.Value * 0.45) / aspect)
        : (item.At.X - item.Wrap!.Value / 2, item.At.X + item.Wrap.Value / 2);

    [Theory]
    [InlineData(360, 640)]
    [InlineData(480, 640)]
    [InlineData(640, 640)]
    public void InANarrowPicture_TheTitleIsSmaller_AndItsLinesEndBeforeTheNorthArrow(int width, int height)
    {
        double aspect = width / (double)height;
        var overlays = new OverlaySpec { Title = "Invented Cave", Subtitle = "a survey of 2024", NorthArrow = true };
        var texts = Texts(Orbit(overlays) with { Output = new OutputSpec { Kind = OutputKind.Video, Width = width, Height = height } });
        var title = Item(texts, "title");
        var subtitle = Item(texts, "subtitle");
        var arrow = Item(texts, "north-arrow");

        // Smaller by the picture's shape: a letter takes the share of the width it takes in a square.
        double narrow = Math.Min(1, aspect);
        Assert.Equal(0.06 * narrow, title.Size!.Value, 6);
        Assert.Equal(0.034 * narrow, subtitle.Size!.Value, 6);
        Assert.Equal(0.1 * narrow, arrow.Size!.Value, 6);
        // "Invented Cave" on one line: thirteen letters, each about 0.6 of its height wide.
        Assert.True(13 * 0.6 * title.Size.Value / aspect <= title.Wrap!.Value, $"the title wraps at {title.Wrap} of the width");

        // The widest line of the title ends before the arrow begins, and the arrow stays in the margin.
        Assert.True(Columns(title, aspect).Right < Columns(arrow, aspect).Left, $"{Columns(title, aspect)} against {Columns(arrow, aspect)}");
        Assert.True(Columns(subtitle, aspect).Right < Columns(arrow, aspect).Left);
        Assert.True(Columns(arrow, aspect).Right <= 1 - ScreenTextPlanner.Margin + 1e-9);
        Assert.True(arrow.At.Y + arrow.Size.Value * 0.95 <= 1 - ScreenTextPlanner.Margin + 1e-9);
        Assert.Equal(title.At.Y - title.Size.Value * 1.35, subtitle.At.Y, 6);
        Assert.Empty(ScenePlanValidator.Validate(PlanFixtures.PlanOf(Orbit(overlays) with { Output = new OutputSpec { Width = width, Height = height } })));
    }

    [Fact]
    public void InAWidePicture_TheTitleAndTheNorthArrowAreAsTheyAlwaysWere_AndATitleAloneHasItsWholeWidth()
    {
        var both = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", NorthArrow = true }));
        Assert.Equal((0.06, 0.6), (Item(both, "title").Size, Item(both, "title").Wrap));
        Assert.Equal(0.1, Item(both, "north-arrow").Size);

        // Without an arrow nothing stands beside the title: only its letters are smaller.
        var alone = Texts(Orbit(new OverlaySpec { Title = "Invented Cave" }) with { Output = new OutputSpec { Width = 360, Height = 640 } });
        Assert.Equal((0.03375, 0.6), (Item(alone, "title").Size, Item(alone, "title").Wrap));

        // A title card is smaller in a narrow picture too.
        var card = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2 }) with { Output = new OutputSpec { Width = 360, Height = 640 } });
        Assert.Equal(0.09 * 0.5625, Item(card, "title").Size!.Value, 6);
    }

    [Fact]
    public void ADraft_WritesWhatThePlanWrites()
    {
        var plan = PlanFixtures.PlanOf(Orbit(new OverlaySpec { Title = "Invented Cave", NorthArrow = true }));

        var draft = ScenePlanBuilder.Draft(plan);

        Assert.Same(plan.Texts, draft.Texts);
        Assert.Contains(ScenePlan.TextsFeature, draft.Features);
        Assert.Empty(ScenePlanValidator.Validate(draft));
    }

    [Fact]
    public void TheTitleFade_CanBeSplitIntoAFadeInAndAFadeOut_AndKeepsItsMeaningWhenAloneAsItWas()
    {
        // In and out apart; the out one is only for a card.
        var apart = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", Subtitle = "a survey", TitleSeconds = 2, TitleFadeIn = 0.3, TitleFadeOut = 1.5 }));
        Assert.All(new[] { "title", "subtitle" }, id => Assert.Equal((0.3, 1.5, 3.5), (Item(apart, id).FadeIn, Item(apart, id).FadeOut, Item(apart, id).End)));

        // Either one alone: the other is the old field's, or the default for going.
        var inOnly = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2, TitleFadeIn = 0.25 }));
        Assert.Equal((0.25, ScreenTextPlanner.TitleFadeSeconds), (Item(inOnly, "title").FadeIn, Item(inOnly, "title").FadeOut));
        var outOnly = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2, TitleFadeOut = 0.4 }));
        Assert.Equal((null, 0.4), (Item(outOnly, "title").FadeIn, Item(outOnly, "title").FadeOut));
        var withOld = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2, TitleFade = 1, TitleFadeIn = 0 }));
        Assert.Equal((null, 1.0), (Item(withOld, "title").FadeIn, Item(withOld, "title").FadeOut));

        // A title that stays only fades in.
        var staying = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleFade = 1, TitleFadeIn = 0.5 }));
        Assert.Equal((0.5, null), (Item(staying, "title").FadeIn, Item(staying, "title").FadeOut));

        // The field alone is what it was.
        var old = Texts(Orbit(new OverlaySpec { Title = "Invented Cave", TitleSeconds = 2, TitleFade = 1.5 }));
        Assert.Equal((1.5, 1.5, 3.5), (Item(old, "title").FadeIn, Item(old, "title").FadeOut, Item(old, "title").End));

        // Written and read, and held to what a card can do.
        var spec = Orbit(new OverlaySpec { Title = "T", TitleSeconds = 2, TitleFadeIn = 0.5, TitleFadeOut = 1 });
        string json = SceneSpecSerializer.Write(spec);
        Assert.Contains("\"titleFadeIn\": 0.5", json);
        Assert.Equal((0.5, 1.0), (SceneSpecSerializer.Read(json).Labels.Overlays.TitleFadeIn, SceneSpecSerializer.Read(json).Labels.Overlays.TitleFadeOut));
        Assert.DoesNotContain("titleFadeIn", SceneSpecSerializer.Write(Orbit(new OverlaySpec { Title = "T" })));
        Assert.Empty(SceneSpecValidator.Validate(PlanFixtures.WithPaths(spec)));
        var errors = SceneSpecValidator.Validate(PlanFixtures.WithPaths(Orbit(new OverlaySpec { Title = "T", TitleFadeIn = -1, TitleFadeOut = 1 })));
        Assert.Equal(["labels.overlays.titleFadeIn", "labels.overlays.titleFadeOut"], errors.Select(e => e.Path).Order());
        Assert.Contains("labels.overlays.titleFadeIn", SceneBuilders.NotDrawnByTheGenerator(spec));
    }
}
