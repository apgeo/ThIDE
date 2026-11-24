// A spec's visibility events in a plan, without Blender: the events of a label group become the
// changes of its labels, on the frames a generated script shows and hides them on, and the events
// of the overlays become the times of the texts that stand for them. Against the generated script
// in Blender they are in PlayerVisibilityEventsBlenderTests.

using Therion.Blender.Plan;
using Therion.Blender.Presets;

namespace Therion.Blender.Tests.Player;

public class ScenePlanVisibilityEventsTests
{
    private static readonly LabelsSpec EveryGroup = new()
    {
        Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named, MaxCount = 5 },
        Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 },
        Leads = new LeadMarkerSpec { Show = true, ShowText = true },
    };

    /// <summary>An orbit of 4 seconds at 30 fps (120 frames) with every label group and these
    /// events, on the invented medium cave with its dead ends as leads.</summary>
    private static ScenePlan Planned(params VisibilityEvent[] events) => Planned(EveryGroup, events);

    private static ScenePlan Planned(LabelsSpec labels, params VisibilityEvent[] events)
        => ScenePlanBuilder.FromSpec(Spec(labels, events), PlanFixtures.MediumFraming, PlanFixtures.MediumWithLeads);

    private static SceneSpec Spec(LabelsSpec labels, params VisibilityEvent[] events) => PlanFixtures.WithPaths(new SceneSpec
    {
        Camera = new CameraSpec { Template = CameraTemplate.Orbit },
        Animation = new AnimationSpec { Fps = 30, DurationSeconds = 4 },
        Labels = labels with { Events = events },
    });

    private static VisibilityEvent Event(VisibilityTarget target, int? show = null, int? hide = null, double fade = 0)
        => new() { Target = target, ShowFrame = show, HideFrame = hide, FadeSeconds = fade };

    private static IReadOnlyList<PlanVisibleKey> KeysOf(ScenePlan plan, string role)
        => plan.Changes!.Single(c => c.Target!.Role == role).Visible!;

    [Fact]
    public void AGroupsEvents_BecomeItsRolesKeys_OnTheFramesTheGeneratedScriptKeys()
    {
        var plan = Planned(Event(VisibilityTarget.StationLabels, show: 31, hide: 91, fade: 0.5));

        Assert.Equal(120, plan.Timeline.FrameCount);
        var change = Assert.Single(plan.Changes!);
        Assert.Equal(new PlanChangeTarget { Role = ScenePlanBuilder.StationLabelRole }, change.Target);
        // Frame 31 is one second in, frame 91 three; the script grows the labels in over 15 frames.
        Assert.Equal([new PlanVisibleKey(1, true, 0.5), new PlanVisibleKey(3, false)], change.Visible);
        Assert.Equal((31, 91), (ScenePlanValidator.FrameAt(1, 30), ScenePlanValidator.FrameAt(3, 30)));
        Assert.Null(change.Opacity);

        Assert.Equal(["models", "camera_track", "look", "labels", "changes", "output"], plan.Features);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, ScenePlanSerializer.Write(plan)));
        Assert.Empty(ScenePlanBuilder.NotCarried(Spec(EveryGroup, Event(VisibilityTarget.StationLabels, show: 31))));
    }

    [Fact]
    public void ALeadsCaption_GoesWithItsMarker_AndEveryGroupHasItsOwnKeys()
    {
        var plan = Planned(
            Event(VisibilityTarget.LeadMarkers, show: 16),
            Event(VisibilityTarget.ComponentLabels, hide: 61),
            Event(VisibilityTarget.StationLabels, show: 1, fade: 1));

        Assert.Equal(
            [ScenePlanBuilder.StationLabelRole, ScenePlanBuilder.ComponentLabelRole, ScenePlanBuilder.LeadMarkerRole, ScenePlanBuilder.LeadTextRole],
            plan.Changes!.Select(c => c.Target!.Role));
        Assert.Equal([new PlanVisibleKey(0.5, true)], KeysOf(plan, ScenePlanBuilder.LeadMarkerRole));
        Assert.Equal(KeysOf(plan, ScenePlanBuilder.LeadMarkerRole), KeysOf(plan, ScenePlanBuilder.LeadTextRole));
        // Hidden from a frame on: shown until then.
        Assert.Equal([new PlanVisibleKey(2, false)], KeysOf(plan, ScenePlanBuilder.ComponentLabelRole));
        // Shown from the first frame, fading in.
        Assert.Equal([new PlanVisibleKey(0, true, 1)], KeysOf(plan, ScenePlanBuilder.StationLabelRole));
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Fact]
    public void TheMapRevealPreset_FadesItsComponentLabelsIn_OverThreeSeconds()
    {
        var plan = PlanFixtures.PlanOf(BuiltInPresets.MapReveal().Spec with
        {
            Labels = BuiltInPresets.MapReveal().Spec.Labels with { Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 } },
        });

        var change = Assert.Single(plan.Changes!);
        Assert.Equal(ScenePlanBuilder.ComponentLabelRole, change.Target!.Role);
        Assert.Equal([new PlanVisibleKey(0, true, 3)], change.Visible);
        Assert.Contains(ScenePlan.ChangesFeature, plan.Features);
        Assert.Empty(ScenePlanBuilder.NotCarried(BuiltInPresets.MapReveal().Spec));
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Fact]
    public void EventsThatChangeNothing_MakeNoChange()
    {
        // Shown from the first frame at once; hidden only after the clip; a group with no labels.
        Assert.Null(Planned(Event(VisibilityTarget.StationLabels, show: 1)).Changes);
        Assert.Null(Planned(Event(VisibilityTarget.StationLabels, hide: 500)).Changes);
        Assert.Null(Planned(new LabelsSpec { Components = EveryGroup.Components }, Event(VisibilityTarget.StationLabels, show: 31)).Changes);
        Assert.DoesNotContain(ScenePlan.ChangesFeature, Planned(Event(VisibilityTarget.StationLabels, show: 1)).Features);
        // Without the cave's metadata there are no labels to show or hide.
        var bare = ScenePlanBuilder.FromSpec(Spec(EveryGroup, Event(VisibilityTarget.StationLabels, show: 31)), PlanFixtures.MediumFraming);
        Assert.Null(bare.Changes);
    }

    [Fact]
    public void AGroupThatIsToAppearAfterTheClip_IsHiddenThroughout()
        => Assert.Equal(
            [new PlanVisibleKey(0, false)],
            KeysOf(Planned(Event(VisibilityTarget.StationLabels, show: 500, fade: 1)), ScenePlanBuilder.StationLabelRole));

    [Fact]
    public void SeveralEventsOfOneGroup_AreOneRunOfKeys_AndAFadeEndsWhereTheNextKeyStarts()
    {
        var plan = Planned(
            Event(VisibilityTarget.StationLabels, show: 91, fade: 0.2),
            Event(VisibilityTarget.StationLabels, hide: 31),
            Event(VisibilityTarget.StationLabels, show: 1, hide: 7, fade: 2));

        Assert.Equal(
            [
                new PlanVisibleKey(0, true, 0.2),   // six frames of the two seconds asked for
                new PlanVisibleKey(0.2, false),
                new PlanVisibleKey(3, true, 0.2),
            ],
            KeysOf(plan, ScenePlanBuilder.StationLabelRole));
        Assert.Empty(ScenePlanValidator.Validate(plan));

        // Of two events on one frame the later decides; within one event, the hiding.
        var same = Planned(Event(VisibilityTarget.StationLabels, show: 31, hide: 31), Event(VisibilityTarget.ComponentLabels, hide: 31), Event(VisibilityTarget.ComponentLabels, show: 31));
        Assert.Equal([new PlanVisibleKey(1, false)], KeysOf(same, ScenePlanBuilder.StationLabelRole));
        Assert.Equal([new PlanVisibleKey(1, true)], KeysOf(same, ScenePlanBuilder.ComponentLabelRole));
    }

    [Fact]
    public void ADraft_KeepsTheChanges()
    {
        var plan = Planned(Event(VisibilityTarget.StationLabels, show: 31, fade: 0.5));

        var draft = ScenePlanBuilder.Draft(plan);

        Assert.Same(plan.Changes, draft.Changes);
        Assert.Contains(ScenePlan.ChangesFeature, draft.Features);
        Assert.Empty(ScenePlanValidator.Validate(draft));
    }

    // ---- the events of the overlays: the times of the texts ----

    private static readonly LabelsSpec WithOverlays = new()
    {
        Overlays = new OverlaySpec { Title = "Peștera", ScaleBar = true, NorthArrow = true, DepthLegend = true, Credits = "cine a măsurat" },
    };

    private static IEnumerable<PlanScreenItem> Overlays(ScenePlan plan)
        => plan.Texts!.Items.Where(i => i.Role != ScreenTextPlanner.CreditsRole);

    [Fact]
    public void TheOverlaysEvents_AreTheTimesOfTheirTexts_AndNoChangeOfTheScene()
    {
        var plan = Planned(WithOverlays, Event(VisibilityTarget.Overlays, show: 31, hide: 91, fade: 0.5));

        Assert.Null(plan.Changes);
        Assert.DoesNotContain(ScenePlan.ChangesFeature, plan.Features);
        Assert.NotEmpty(Overlays(plan));
        Assert.Equal(
            ["depth-legend", "north-arrow", "scale-bar", "title"],
            Overlays(plan).Select(i => i.Role).Distinct().Order(StringComparer.Ordinal));
        Assert.All(Overlays(plan), item => Assert.Equal((1.0, 3.0, 0.5, null), (item.Start, item.End, item.FadeIn, item.FadeOut)));
        // What a generated script does not draw in the scene is not its events' to show or hide.
        var credits = plan.Texts!.Items.Single(i => i.Role == ScreenTextPlanner.CreditsRole);
        Assert.Equal((null, null, null), (credits.Start, credits.End, credits.FadeIn));
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Empty(ScenePlanBuilder.NotCarried(Spec(WithOverlays, Event(VisibilityTarget.Overlays, show: 31))));
    }

    [Fact]
    public void OverlaysShownTwice_AreWrittenOnceForEachTime_AndATitleCardKeepsItsOwnEnd()
    {
        var twice = Planned(WithOverlays, Event(VisibilityTarget.Overlays, hide: 31), Event(VisibilityTarget.Overlays, show: 91, fade: 1));

        var titles = twice.Texts!.Items.Where(i => i.Role == ScreenTextPlanner.TitleRole).ToList();
        Assert.Equal(["title", "title#2"], titles.Select(i => i.Id));
        Assert.Equal((null, 1.0, null), (titles[0].Start, titles[0].End, titles[0].FadeIn));
        Assert.Equal((3.0, null, 1.0), (titles[1].Start, titles[1].End, titles[1].FadeIn));
        Assert.Empty(ScenePlanValidator.Validate(twice));

        // A title card stands for two seconds and fades out by itself; hidden from one second on,
        // it goes there, at once; hidden only later, it goes as it would have.
        var card = WithOverlays with { Overlays = new OverlaySpec { Title = "Peștera", TitleSeconds = 2 } };
        var cut = Planned(card, Event(VisibilityTarget.Overlays, hide: 31)).Texts!.Items.Single();
        Assert.Equal((1.0, null), (cut.End, cut.FadeOut));
        var kept = Planned(card, Event(VisibilityTarget.Overlays, hide: 119)).Texts!.Items.Single();
        Assert.Equal((2 + ScreenTextPlanner.TitleFadeSeconds, ScreenTextPlanner.TitleFadeSeconds), (kept.End, kept.FadeOut));
        // Shown only after the card has gone: no title at all.
        Assert.Null(Planned(card, Event(VisibilityTarget.Overlays, show: 100)).Texts);
    }

    [Fact]
    public void AStill_HasTheOverlaysThatAreThereOnItsFrame()
    {
        ScenePlan Still(params VisibilityEvent[] events) => ScenePlanBuilder.FromSpec(
            Spec(WithOverlays, events) with { Output = new OutputSpec { Kind = OutputKind.Still, OutputDirectory = "out" } },
            PlanFixtures.MediumFraming, PlanFixtures.MediumWithLeads);

        Assert.NotEmpty(Overlays(Still(Event(VisibilityTarget.Overlays, hide: 31))));
        Assert.All(Still(Event(VisibilityTarget.Overlays, hide: 31)).Texts!.Items, item => Assert.Null(item.End));
        Assert.Empty(Overlays(Still(Event(VisibilityTarget.Overlays, show: 31))));
    }
}
