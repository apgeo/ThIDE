// The rules every check of a plan, a movie, a spec and a storyboard holds to alike: a name ends
// where the string ends (a line break after it is no part of a name), a base name is good on every
// file system whatever platform checks it, a time far from the clip is still after it, and a
// counter's own keys are held to frames as the player keys them.

using Therion.Blender.Plan;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class PlanRulesTests
{
    private static ScenePlan Full => PlanFixtures.Full();

    private static IEnumerable<string> Paths(ScenePlan plan) => ScenePlanValidator.Validate(plan).Select(e => e.Path);

    // ---- names end where the string ends ----

    [Fact]
    public void AModuleOrARoleWithALineBreakAfterIt_IsNoName()
    {
        Assert.Contains("features[1]", Paths(Full with { Features = ["models", "marker_cube\n"] }));
        var label = Full.Labels!.Texts!.Items[0];
        Assert.Empty(Paths(Full));
        var badRole = Full with
        {
            Labels = Full.Labels with { Texts = Full.Labels.Texts with { Items = [label with { Role = "station-label\n" }] } },
        };
        Assert.Contains(Paths(badRole), p => p.EndsWith(".role", StringComparison.Ordinal));

        var spec = SceneSpecTests.ValidSpec() with
        {
            Annotations = [new AnnotationSpec { At = new CaveVector3(0, 0, 0), Text = "x", Role = "lead\n" }],
        };
        Assert.Contains(SceneSpecValidator.Validate(spec), e => e.Path == "annotations[0].role");
        Assert.DoesNotContain(SceneSpecValidator.Validate(spec with { Annotations = [spec.Annotations[0] with { Role = "lead" }] }),
            e => e.Path == "annotations[0].role");
    }

    // ---- base names ----

    [Theory]
    [InlineData("a:b")]
    [InlineData("x?")]
    [InlineData("star*")]
    [InlineData("<film>")]
    [InlineData("pipe|d")]
    [InlineData("quote\"d")]
    [InlineData("tab\tbed")]
    [InlineData("CON")]
    [InlineData("nul.film")]
    [InlineData("com1")]
    [InlineData("back\\slash")]
    [InlineData("a/b")]
    public void ABaseNameAnyFileSystemRefuses_IsRefusedOnEveryPlatform(string name)
    {
        Assert.Contains("output.baseName", Paths(Full with { Output = Full.Output with { BaseName = name } }));
        var spec = SceneSpecTests.ValidSpec();
        Assert.Contains(SceneSpecValidator.Validate(spec with { Output = spec.Output with { BaseName = name } }), e => e.Path == "output.baseName");
        var movie = new MoviePlan();
        Assert.Contains(MoviePlanValidator.Validate(movie with { Output = movie.Output with { BaseName = name } }), e => e.Path == "output.baseName");
    }

    [Theory]
    [InlineData("cave-render")]
    [InlineData("Peștera de sus, 2026")]
    [InlineData("film.s01")]
    [InlineData("console")]
    [InlineData("CON-tour")]
    public void APlainBaseName_IsGood(string name)
        => Assert.DoesNotContain("output.baseName", Paths(Full with { Output = Full.Output with { BaseName = name } }));

    [Fact]
    public void TheBundlesFolderName_HoldsToTheSameRules()
    {
        Assert.Equal("a_b_", Therion.Blender.Player.RenderBundleWriter.FolderName("a:b?.zip"));
        Assert.Equal("CON_", Therion.Blender.Player.RenderBundleWriter.FolderName("CON.zip"));
        Assert.Equal("nul_.x", Therion.Blender.Player.RenderBundleWriter.FolderName("nul.x.zip"));
        Assert.True(PlanRules.IsPlainBaseName(Therion.Blender.Player.RenderBundleWriter.FolderName("a<b>|c.zip")));
    }

    // ---- times far from the clip ----

    [Theory]
    [InlineData(1e8)]
    [InlineData(1e12)]
    [InlineData(1e300)]
    public void APoseLongAfterTheLastFrame_IsRefused(double seconds)
    {
        var track = Full.Camera.Track;
        var plan = Full with { Camera = Full.Camera with { Track = [.. track, track[^1] with { T = seconds }] } };
        Assert.Contains($"camera.track[{track.Count}].t", Paths(plan));
    }

    [Fact]
    public void TheFrameOfATime_IsHeldToTheNumbersThereAre_OnEveryRuntime()
    {
        Assert.Equal(1, ScenePlanValidator.FrameAt(0, 30));
        Assert.Equal(31, ScenePlanValidator.FrameAt(1, 30));
        Assert.Equal(int.MaxValue, ScenePlanValidator.FrameAt(1e8, 30));
        Assert.Equal(int.MaxValue, ScenePlanValidator.FrameAt(double.PositiveInfinity, 30));
        Assert.Equal(int.MinValue, ScenePlanValidator.FrameAt(-1e300, 30));
        Assert.Equal(int.MinValue, ScenePlanValidator.FrameAt(double.NaN, 30));
    }

    [Fact]
    public void KeysFarAfterTheClip_AreInTheirOrder()
    {
        var keys = new[] { new PlanNumberKey(0, 0), new PlanNumberKey(1e8, 0.5), new PlanNumberKey(2e8, 1) };
        var plan = Full with { Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "CaveModel" }, Opacity = keys }] };
        Assert.Empty(Paths(plan));
        var backwards = new[] { new PlanNumberKey(2e8, 1), new PlanNumberKey(1e8, 0.5) };
        Assert.Contains("changes[0].opacity[1].t",
            Paths(Full with { Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "CaveModel" }, Opacity = backwards }] }));
    }

    // ---- a counter's own keys, by frame ----

    private static ScenePlan WithCounter(params PlanNumberKey[] keys)
    {
        var plan = Full;
        return plan with
        {
            Texts = plan.Texts! with
            {
                Items =
                [
                    .. plan.Texts.Items,
                    new PlanScreenItem
                    {
                        Id = "count", Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.1, 0.1), Text = "{} m", Size = 0.05,
                        Counter = new PlanCounter { Keys = keys },
                    },
                ],
            },
        };
    }

    [Fact]
    public void ACountersKeys_OnOneFrame_AreAJump_AsThePlayerKeysThem()
    {
        int index = Full.Texts!.Items.Count;
        // 25 fps: 0 and 0.01 s both fall on frame 1.
        Assert.Empty(Paths(WithCounter(new PlanNumberKey(0, 0), new PlanNumberKey(1, 5))));
        Assert.Contains($"texts.items[{index}].counter.keys[1].easing", Paths(WithCounter(new PlanNumberKey(0, 0), new PlanNumberKey(0.01, 5))));
        Assert.Empty(Paths(WithCounter(new PlanNumberKey(0, 0), new PlanNumberKey(0.01, 5, PlanEasing.Constant))));
        // No more than three on a frame.
        Assert.Contains($"texts.items[{index}].counter.keys[3].t", Paths(WithCounter(
            new PlanNumberKey(0, 0), new PlanNumberKey(0.005, 1, PlanEasing.Constant), new PlanNumberKey(0.01, 2, PlanEasing.Constant),
            new PlanNumberKey(0.015, 3, PlanEasing.Constant))));
        // Out of order, and not a number.
        Assert.Contains($"texts.items[{index}].counter.keys[1].t", Paths(WithCounter(new PlanNumberKey(1, 0), new PlanNumberKey(0, 5))));
        Assert.Contains($"texts.items[{index}].counter.keys[0].t", Paths(WithCounter(new PlanNumberKey(double.NaN, 0))));
        Assert.Contains($"texts.items[{index}].counter.keys[0].value", Paths(WithCounter(new PlanNumberKey(0, double.PositiveInfinity))));
        Assert.Contains($"texts.items[{index}].counter.keys", Paths(WithCounter()));
    }

    // ---- the value a held reveal starts from ----

    [Fact]
    public void TheValueOfKeysOnAFrame_IsTheOneThePlayerShows()
    {
        static double At(int frame, params PlanNumberKey[] keys) => StoryboardPlanner.ValueAt(keys, frame, 10);
        PlanNumberKey[] even = [new(0, 0), new(1, 10)];
        Assert.Equal([0, 5, 10, 10], new[] { 1, 6, 11, 20 }.Select(f => At(f, even)));
        PlanNumberKey[] smooth = [new(0, 0), new(1, 10, PlanEasing.Smooth)];
        Assert.Equal(5, At(6, smooth), 6);
        Assert.Equal(1.04, At(3, smooth), 6);
        Assert.Equal(0, At(6, new(0, 0), new(1, 10, PlanEasing.Constant)));
        // Of keys on one frame the second is shown there; the way on starts from the last.
        PlanNumberKey[] jumps = [new(0, 0), new(1, 10), new(1, 20, PlanEasing.Constant), new(1, 30, PlanEasing.Constant), new(2, 40)];
        Assert.Equal([5, 20, 35, 40, 40], new[] { 6, 11, 16, 21, 30 }.Select(f => At(f, jumps)));
        Assert.Equal(20, At(30, new(0, 0), new(1, 10), new(1, 20, PlanEasing.Constant)));
        // Before the first key, its value.
        Assert.Equal(5, At(1, new(1, 5), new(2, 6)));
    }
}
