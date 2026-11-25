// A reveal in a plan and in a spec, without Blender: how it is written and read, what the schema
// and the validator hold it to, how a spec's reveal is lowered over the reveal values of a
// converted invented cave, and which builder draws it.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class ScenePlanRevealTests
{
    private static ScenePlan Revealed => PlanFixtures.Revealed();

    private static IEnumerable<string> Paths(ScenePlan plan) => ScenePlanValidator.Validate(plan).Select(e => e.Path);

    private static IReadOnlyList<string> Problems(string plan) => JsonSchemaSubset.Validate(ScenePlanSchema.Text, plan);

    /// <summary>The fixture with its reveal's change changed.</summary>
    private static ScenePlan WithChange(Func<PlanChange, PlanChange> change)
        => Revealed with { Changes = [.. Revealed.Changes!.Select(c => c.Reveal is null ? c : change(c))] };

    private static ScenePlan WithCounter(Func<PlanScreenItem, PlanScreenItem> change)
        => Revealed with { Texts = Revealed.Texts! with { Items = [.. Revealed.Texts.Items.Select(i => i.Counter is null ? i : change(i))] } };

    private static ScenePlan WithStrips(Func<PlanLineStrip, int, PlanLineStrip> change)
        => Revealed with { Lines = [Revealed.Lines![0] with { Strips = [.. Revealed.Lines[0].Strips.Select(change)] }] };

    // ---- written and read ----

    [Fact]
    public void AReveal_IsWrittenWithAKeyToALine_ItsEdgeAsAnObject_AndReadsBackTheSame()
    {
        string json = ScenePlanSerializer.Write(Revealed);

        Assert.Contains(
            "      \"reveal\": [\n" +
            "        { \"t\": 0.4, \"value\": 1987.5 },\n" +
            "        { \"t\": 1.8, \"value\": 2005.3, \"easing\": \"smooth\" }\n" +
            "      ],\n" +
            "      \"revealFront\": {\n" +
            "        \"attribute\": \"reveal\",\n" +
            "        \"edge\": 0.5,\n" +
            "        \"glow\": {\n" +
            "          \"color\": [1, 0.6, 0.1],\n" +
            "          \"strength\": 2.5,\n" +
            "          \"width\": 1.2\n" +
            "        }\n" +
            "      }\n" +
            "    },\n", json);
        Assert.Contains(
            "        { \"points\": [[10, 0, -3], [14, -2, -9]], \"colors\": [[0, 1, 0], [1, 1, 0]], \"reveal\": [2003, 2003.8] }\n", json);
        Assert.Contains(
            "      { \"id\": \"year\", \"role\": \"reveal-counter\", \"kind\": \"text\", \"at\": [0.05, 0.17], \"text\": \"anul {}\", \"size\": 0.08, " +
            "\"alignX\": \"left\", \"alignY\": \"bottom\", \"start\": 0.4, \"fadeIn\": 0.2, " +
            "\"counter\": { \"target\": { \"id\": \"Legs\" }, \"decimals\": 1, \"min\": 1987, \"max\": 2003 } }\n", json);
        Assert.Empty(Problems(json));

        var read = ScenePlanSerializer.Read(json);
        var (written, back) = (Revealed.Changes!.Single(c => c.Reveal is not null), read.Changes!.Single(c => c.Reveal is not null));
        Assert.Equal(written.Reveal, back.Reveal);
        Assert.Equal(written.RevealFront, back.RevealFront);
        Assert.Equal(Revealed.Lines![0].Strips.Select(s => s.Reveal!), read.Lines![0].Strips.Select(s => s.Reveal!));
        Assert.Equal(Revealed.Texts!.Items[^1].Counter, read.Texts!.Items[^1].Counter);
        Assert.Equal(json, ScenePlanSerializer.Write(read));
        Assert.Empty(Paths(read));
    }

    [Fact]
    public void WhatAnEdgeLeavesToTheDefaults_IsNotWritten_AndAPlanWithoutARevealIsAsItWas()
    {
        // A clean cut of the default attribute: an empty object. A counter of whole numbers
        // without limits: its target alone.
        string plain = ScenePlanSerializer.Write(WithCounter(i => i with { Counter = new PlanCounter { Target = i.Counter!.Target } }) with
        {
            Changes = [.. Revealed.Changes!.Select(c => c.Reveal is null ? c : c with { RevealFront = new PlanRevealFront() })],
        });
        Assert.Contains("      \"revealFront\": {}\n", plain);
        Assert.Contains("\"counter\": { \"target\": { \"id\": \"Legs\" } } }\n", plain);
        Assert.Empty(Problems(plain));
        var front = ScenePlanSerializer.Read(plain).Changes!.Single(c => c.Reveal is not null).RevealFront!;
        Assert.Equal((null, 0.0, null), (front.Attribute, front.Edge, front.Glow));

        string full = ScenePlanSerializer.Write(PlanFixtures.Full());
        Assert.DoesNotContain("reveal", full);
        Assert.DoesNotContain("counter", full);
    }

    [Theory]
    [InlineData("\"reveal\": [2003, 2003.8]", "\"reveal\": [2003]", "$.lines[0].strips[1].reveal: fewer than 2 items")]
    [InlineData("\"reveal\": [2003, 2003.8]", "\"reveal\": [2003, \"late\"]", "$.lines[0].strips[1].reveal[1]: expected number, found String")]
    [InlineData("{ \"t\": 0.4, \"value\": 1987.5 },", "{ \"t\": 0.4 },", "$.changes[5].reveal[0]: 'value' is missing")]
    [InlineData("\"edge\": 0.5,", "\"edge\": -1,", "$.changes[5].revealFront.edge: below 0")]
    [InlineData("\"edge\": 0.5,", "\"edge\": 0.5, \"blur\": 2,", "$.changes[5].revealFront: 'blur' is not a known property")]
    [InlineData("\"attribute\": \"reveal\",", "\"attribute\": \"\",", "$.changes[5].revealFront.attribute: shorter than 1")]
    [InlineData("\"width\": 1.2", "\"width\": 0", "$.changes[5].revealFront.glow.width: not above 0")]
    [InlineData("\"strength\": 2.5,", "\"strength\": -1,", "$.changes[5].revealFront.glow.strength: below 0")]
    [InlineData("\"decimals\": 1,", "\"decimals\": 7,", "$.texts.items[4].counter.decimals: above 6")]
    [InlineData("\"decimals\": 1,", "\"decimals\": 0.5,", "$.texts.items[4].counter.decimals: expected integer, found Number")]
    [InlineData("\"counter\": { \"target\": { \"id\": \"Legs\" },", "\"counter\": { \"target\": {},", "$.texts.items[4].counter.target: fewer than 1 properties")]
    [InlineData("\"counter\": { \"target\": { \"id\": \"Legs\" },", "\"counter\": { \"round\": true, \"target\": { \"id\": \"Legs\" },", "$.texts.items[4].counter: 'round' is not a known property")]
    public void WhatTheSchemaForbids_IsNamed(string from, string to, string problem)
    {
        string json = ScenePlanSerializer.Write(Revealed);
        Assert.Contains(from, json);

        Assert.Contains(Problems(json.Replace(from, to)), p => p.StartsWith(problem, StringComparison.Ordinal));
    }

    [Fact]
    public void AKeyThisBuildDoesNotKnow_IsRefusedOnReading_AsThePlayerRefusesIt()
    {
        string json = ScenePlanSerializer.Write(Revealed);

        var front = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(json.Replace("\"edge\": 0.5,", "\"edge\": 0.5, \"blur\": 2,")));
        Assert.StartsWith("changes[5].revealFront.blur is not known to this build", front.Message);
        var counter = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(json.Replace("\"decimals\": 1,", "\"decimals\": 1, \"round\": true,")));
        Assert.StartsWith("texts.items[4].counter.round is not known to this build", counter.Message);
    }

    // ---- the validator ----

    [Fact]
    public void AReveal_IsOfAModelOrALineWithAMaterial_AndItsKeysAreThoseOfAnyNumber()
    {
        Assert.Empty(Paths(Revealed));
        Assert.Empty(Paths(WithChange(c => c with { RevealFront = null })));

        // Its target: there, a model or a line, with a material, and with values where the plan has them.
        Assert.Equal(["changes[0].target"], Paths(Revealed with { Changes = [new PlanChange { Reveal = [new PlanNumberKey(0, 1)] }] }).Take(1));
        var label = Revealed with { Changes = [new PlanChange { Target = new PlanChangeTarget { Role = "station-label" }, Reveal = [new PlanNumberKey(0, 1)] }] };
        Assert.Contains("changes[0].reveal", Paths(label));
        Assert.Contains(ScenePlanValidator.Validate(label), e => e.Message == "Only a model or a line is revealed part by part; 'stn:entrance' is shown and hidden as a whole ('visible').");
        var light = Revealed with { Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "Key" }, Reveal = [new PlanNumberKey(0, 1)] }] };
        Assert.Contains(ScenePlanValidator.Validate(light), e => e.Path == "changes[0].reveal" && e.Message == "'Key' is a light; of a light only 'visible' and 'energy' change.");
        // A model's values are in its file, which only the player reads; a PLY without a material has nothing to cut.
        var model = Revealed with { Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "CaveModel" }, Reveal = [new PlanNumberKey(0, 1)] }] };
        Assert.DoesNotContain("changes[0].reveal", Paths(model));
        var bare = Revealed with { Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "Terrain" }, Reveal = [new PlanNumberKey(0, 1)] }] };
        Assert.DoesNotContain("changes[0].reveal", Paths(bare)); // draped: it has a material
        var noMaterial = Revealed with
        {
            Models = [.. Revealed.Models, new PlanModel { Id = "Bare", Path = "bare.ply" }],
            Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "Bare" }, Reveal = [new PlanNumberKey(0, 1)] }],
        };
        Assert.Contains(ScenePlanValidator.Validate(noMaterial), e => e.Path == "changes[0].reveal" && e.Message == "The model 'Bare' has no material to change; give it one.");

        // Two changes do not reveal the same thing.
        var twice = Revealed with { Changes = [.. Revealed.Changes!, new PlanChange { Target = new PlanChangeTarget { Role = "survey-lines" }, Reveal = [new PlanNumberKey(0, 1)] }] };
        Assert.Contains($"changes[{Revealed.Changes!.Count}].reveal", Paths(twice));

        // Its keys.
        Assert.Equal(["changes[5].reveal"], Paths(WithChange(c => c with { Reveal = [] })));
        Assert.Equal(["changes[5].reveal[1].value"], Paths(WithChange(c => c with { Reveal = [c.Reveal![0], new PlanNumberKey(1, double.NaN)] })));
        Assert.Equal(["changes[5].reveal[1].t"], Paths(WithChange(c => c with { Reveal = [new PlanNumberKey(1, 1990), new PlanNumberKey(0.5, 2000)] })));
        Assert.Empty(Paths(WithChange(c => c with { Reveal = [new PlanNumberKey(0, 2000), new PlanNumberKey(30, -5)] }))); // down, and past the end
    }

    [Fact]
    public void AnEdge_GoesWithAReveal_AndItsNumbersAreNotNegative()
    {
        // (The counter of the fixture then counts nothing.)
        Assert.Equal(["changes[5].revealFront", "texts.items[4].counter.target"], Paths(WithChange(c => c with { Reveal = null })));
        Assert.Equal(["changes[5].revealFront.attribute"], Paths(WithChange(c => c with { RevealFront = c.RevealFront! with { Attribute = " " } })));
        Assert.Equal(["changes[5].revealFront.edge"], Paths(WithChange(c => c with { RevealFront = c.RevealFront! with { Edge = -0.1 } })));
        Assert.Equal(["changes[5].revealFront.edge"], Paths(WithChange(c => c with { RevealFront = c.RevealFront! with { Edge = double.PositiveInfinity } })));
        PlanChange Glow(PlanChange c, PlanRevealGlow glow) => c with { RevealFront = c.RevealFront! with { Glow = glow } };
        Assert.Equal(["changes[5].revealFront.glow.color"], Paths(WithChange(c => Glow(c, new PlanRevealGlow { Color = new ColorRgb(2, 0, 0) }))));
        Assert.Equal(["changes[5].revealFront.glow.strength"], Paths(WithChange(c => Glow(c, new PlanRevealGlow { Strength = -1 }))));
        Assert.Equal(["changes[5].revealFront.glow.width"], Paths(WithChange(c => Glow(c, new PlanRevealGlow { Width = 0 }))));
        Assert.Empty(Paths(WithChange(c => Glow(c, new PlanRevealGlow()))));
    }

    [Fact]
    public void ALinesValues_AreOneForEveryPoint_OfEveryStripOrOfNone_AndARevealedLineHasThem()
    {
        Assert.Equal(["lines[0].strips[0].reveal"], Paths(WithStrips((s, i) => i == 0 ? s with { Reveal = [1, 2] } : s)));
        Assert.Equal(["lines[0].strips[1].reveal"], Paths(WithStrips((s, i) => i == 1 ? s with { Reveal = [1, double.NaN] } : s)));
        // One strip with and one without; and the change that reveals the line still finds values.
        Assert.Equal(["lines[0].strips"], Paths(WithStrips((s, i) => i == 1 ? s with { Reveal = null } : s)));
        // No values at all: the line is as any line, and cannot be revealed by the default attribute.
        var none = WithStrips((s, _) => s with { Reveal = null });
        Assert.Equal(["changes[5].reveal"], Paths(none));
        Assert.Contains(ScenePlanValidator.Validate(none), e => e.Message == "The line 'Legs' has no reveal values; give every point of its strips one ('reveal').");
        Assert.Empty(Paths(none with { Changes = [.. none.Changes!.Select(c => c with { Reveal = null, RevealFront = null })], Texts = null, Features = none.Features.Where(f => f != "texts").ToList() }));
    }

    [Fact]
    public void ACounter_IsATextThatSaysWhereItsNumberGoes_OfATargetAChangeReveals()
    {
        Assert.Equal(["texts.items[4].text"], Paths(WithCounter(i => i with { Text = "anul" })));
        Assert.Equal(["texts.items[4].counter"], Paths(WithCounter(i => i with { Kind = PlanScreenItemKind.Box, Text = null, Size = null, Width = 0.1, Height = 0.1 })));
        Assert.Equal(["texts.items[4].counter.decimals"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Decimals = 7 } })));
        Assert.Equal(["texts.items[4].counter.decimals"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Decimals = -1 } })));
        Assert.Equal(["texts.items[4].counter.max"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Min = 2000, Max = 1990 } })));
        Assert.Equal(["texts.items[4].counter.min"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Min = double.NaN, Max = null } })));
        Assert.Empty(Paths(WithCounter(i => i with { Counter = i.Counter! with { Min = null, Max = null, Decimals = 0 } })));

        // Its target is named as the change names it: by the same id, or by the same role.
        Assert.Equal(["texts.items[4].counter.target"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Target = new PlanChangeTarget() } })));
        Assert.Equal(["texts.items[4].counter.target"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Target = new PlanChangeTarget { Id = "Legs", Role = "line" } } })));
        Assert.Equal(["texts.items[4].counter.target"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Target = new PlanChangeTarget { Id = "CaveModel" } } })));
        Assert.Equal(["texts.items[4].counter.target"], Paths(WithCounter(i => i with { Counter = i.Counter! with { Target = new PlanChangeTarget { Role = "survey-lines" } } })));
        var byRole = WithCounter(i => i with { Counter = i.Counter! with { Target = new PlanChangeTarget { Role = "survey-lines" } } }) with
        {
            Changes = [new PlanChange { Target = new PlanChangeTarget { Role = "survey-lines" }, Reveal = [new PlanNumberKey(0, 1987)] }],
        };
        Assert.Empty(Paths(byRole));
    }

    // ---- a spec's reveal ----

    private static readonly RevealSpec ByDistance = new() { By = RevealBy.Distance, Start = 1, End = 6 };

    /// <summary>The preset without the cave's name at the top: the only text is the reveal's.</summary>
    private static SceneSpec Orbit(RevealSpec? reveal) => PlanFixtures.Preset("Orbit showcase") with
    {
        Labels = PlanFixtures.Preset("Orbit showcase").Labels with { Overlays = new OverlaySpec() },
        Reveal = reveal,
    };

    private static GeometryResult Geometry(CaveModel model, SceneSpec spec) => GeometryStage.Build(model, RevealPlanner.GeometryFor(spec));

    private static (ScenePlan Plan, SceneMeta Meta, GeometryResult Geometry) Planned(SceneSpec spec, CaveModel? model = null)
    {
        var geometry = Geometry(model ?? InventedCave.Build(InventedCaveSize.Medium), spec);
        var meta = SceneMeta.Build(geometry);
        return (ScenePlanBuilder.FromSpec(PlanFixtures.WithPaths(spec), CameraFraming.FromGeometry(geometry), meta), meta, geometry);
    }

    [Fact]
    public void ASpecsReveal_IsOneChangeOfTheCave_FromTheLowestValueToJustPastTheHighest()
    {
        var (plan, meta, geometry) = Planned(Orbit(ByDistance));

        var values = meta.Reveal!;
        Assert.Equal(("distance", 0.0, geometry.Reveal!.Max), (values.By, values.Min, values.Max));
        Assert.Equal(geometry.Reveal.Starts, values.From);
        Assert.Null(values.LegsWithoutDate);
        double range = values.Max;

        var change = Assert.Single(plan.Changes!);
        Assert.Equal(new PlanChangeTarget { Id = ScenePlanBuilder.ModelId }, change.Target);
        // The edge is 3 % of the range and the glow 4 %: the threshold ends past the glow.
        var front = change.RevealFront!;
        Assert.Equal(Math.Round(0.03 * range, 6), front.Edge);
        Assert.Equal((new ColorRgb(1.0, 0.55, 0.15), 3.0, Math.Round(0.04 * range, 6)), (front.Glow!.Color, front.Glow.Strength, front.Glow.Width));
        Assert.Null(front.Attribute);
        Assert.Equal(
            [new PlanNumberKey(1, 0), new PlanNumberKey(6, Math.Round(range + front.Glow.Width + range * 1e-3, 6))],
            change.Reveal);

        Assert.Equal(["models", "camera_track", "look", "labels", "changes", "output"], plan.Features);
        Assert.Null(plan.Texts);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Empty(ScenePlanBuilder.NotCarried(Orbit(ByDistance)));
        Assert.Empty(ScenePlanBuilder.Notices(Orbit(ByDistance), meta));
        Assert.Empty(Problems(ScenePlanSerializer.Write(plan)));
    }

    [Fact]
    public void WithoutAnEnd_TheRevealEndsOnTheLastFrame_AndACleanCutHasNoEdge()
    {
        var spec = Orbit(new RevealSpec { By = RevealBy.Order, Edge = 0, Glow = false });
        var (plan, meta, _) = Planned(spec, InventedCave.Build(InventedCaveSize.Small));

        var change = Assert.Single(plan.Changes!);
        Assert.Null(change.RevealFront);
        Assert.Equal(("order", 0.0, 16.0), (meta.Reveal!.By, meta.Reveal.Min, meta.Reveal.Max));
        // 360 frames at 30 fps: the last one is at 359/30 s.
        Assert.Equal([new PlanNumberKey(0, 0), new PlanNumberKey(Math.Round(359 / 30.0, 6), 16.016)], change.Reveal);

        // An end after the clip is a key after the clip: the clip ends on the way.
        var late = Planned(Orbit(new RevealSpec { By = RevealBy.Order, Edge = 0, Glow = false, End = 60 }), InventedCave.Build(InventedCaveSize.Small)).Plan;
        Assert.Equal(60, late.Changes![0].Reveal![1].T);
        Assert.Empty(ScenePlanValidator.Validate(late));
    }

    [Fact]
    public void AModelOfOneValue_IsStillRevealed_AllAtOnce()
    {
        var day = new DateOnly(1999, 1, 1);
        var cave = RevealFixtures.Cave([new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)], [(0, 1, CaveShotFlags.None, day), (1, 2, CaveShotFlags.None, day)]);
        var (plan, meta, _) = Planned(Orbit(new RevealSpec { By = RevealBy.Date, Start = 2, End = 4 }), cave);

        Assert.Equal((1999.0, 1999.0), (meta.Reveal!.Min, meta.Reveal.Max));
        var change = plan.Changes!.Single(c => c.Reveal is not null);
        // No range to take a share of: the threshold moves by a thousandth of a year, the edge
        // is a cut, the glow as wide as that move.
        Assert.Equal([new PlanNumberKey(2, 1999), new PlanNumberKey(4, 1999.002)], change.Reveal);
        Assert.Equal((0.0, 0.001), (change.RevealFront!.Edge, change.RevealFront.Glow!.Width));
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Fact]
    public void ARevealByDate_HasACounterOfItsYears_BesideWhateverElseIsWritten()
    {
        var spec = Orbit(new RevealSpec { By = RevealBy.Date, Start = 1, End = 9 });
        var (plan, meta, _) = Planned(spec, RevealFixtures.MediumByExpedition());

        Assert.Equal(("date", 0), (meta.Reveal!.By, meta.Reveal.LegsWithoutDate));
        var counter = Assert.Single(plan.Texts!.Items);
        Assert.Equal((RevealPlanner.CounterId, RevealPlanner.CounterRole, PlanScreenItemKind.Text, "{}"), (counter.Id, counter.Role, counter.Kind, counter.Text));
        Assert.Equal(new PlanCounter { Target = new PlanChangeTarget { Id = ScenePlanBuilder.ModelId }, Min = 1987, Max = 2003 }, counter.Counter);
        Assert.Equal((PlanAlignX.Left, PlanAlignY.Bottom, new PlanPoint(0.05, 0.17)), (counter.AlignX, counter.AlignY, counter.At));
        Assert.Equal(["models", "camera_track", "look", "labels", "changes", "texts", "output"], plan.Features);
        Assert.True(plan.HasTexts);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Empty(Problems(ScenePlanSerializer.Write(plan)));

        // With a title too, the counter comes after it; without the counter there is no text.
        var titled = spec with { Labels = spec.Labels with { Overlays = new OverlaySpec { Title = "Invented" } } };
        Assert.Equal(["title", RevealPlanner.CounterId], Planned(titled, RevealFixtures.MediumByExpedition()).Plan.Texts!.Items.Select(i => i.Id));
        Assert.Null(Planned(spec with { Reveal = spec.Reveal! with { Counter = false } }, RevealFixtures.MediumByExpedition()).Plan.Texts);
        // Other reveals have no years to count.
        Assert.Null(Planned(Orbit(ByDistance with { Counter = true })).Plan.Texts);
    }

    [Fact]
    public void LegsWithoutADate_AreSaid_AndAModelWithoutAnyIsRenderedWithoutTheReveal()
    {
        var spec = Orbit(new RevealSpec { By = RevealBy.Date });
        var partly = RevealFixtures.Dated(InventedCave.Build(InventedCaveSize.Small), (_, i) => i < 10 ? new DateOnly(2001, 5, 1) : null);
        var (plan, meta, _) = Planned(spec, partly);
        Assert.Equal(6, meta.Reveal!.LegsWithoutDate);
        Assert.NotNull(plan.Changes);
        Assert.Equal(["6 legs have no survey date; they are shown from the start of the reveal."], ScenePlanBuilder.Notices(spec, meta));

        var (bare, none, geometry) = Planned(spec, InventedCave.Build(InventedCaveSize.Small));
        Assert.Null(none.Reveal);
        Assert.Null(geometry.Walls.Reveal);
        Assert.Null(bare.Changes);
        Assert.Null(bare.Texts);
        Assert.Equal(
            ["The model has no survey dates to reveal it by (a .lox file has none; name the .3d file of the same cave); rendering without the reveal."],
            ScenePlanBuilder.Notices(spec, none));
        Assert.Empty(ScenePlanValidator.Validate(bare));

        // Values of another kind than the spec asks for are not the spec's.
        var byDistance = SceneMeta.Build(Geometry(InventedCave.Build(InventedCaveSize.Small), Orbit(ByDistance)));
        Assert.Null(ScenePlanBuilder.FromSpec(PlanFixtures.WithPaths(spec), PlanFixtures.MediumFraming, byDistance).Changes);
    }

    [Fact]
    public void AStill_ShowsTheWholeCave_AndSaysSo()
    {
        var still = PlanFixtures.Preset("Documentation stills") with { Reveal = ByDistance with { End = null, Start = 0 } };
        Assert.Empty(SceneSpecValidator.Validate(PlanFixtures.WithPaths(still)));
        var one = Orbit(ByDistance) with { Output = Orbit(null).Output with { Kind = OutputKind.Still } };
        Assert.False(RevealPlanner.Applies(still) || RevealPlanner.Applies(one) || RevealPlanner.Applies(Orbit(null)));
        Assert.True(RevealPlanner.Applies(Orbit(ByDistance)));
        Assert.Null(Planned(one).Plan.Changes);

        // No values are worked out for a still, and none are used.
        Assert.Null(RevealPlanner.GeometryFor(still).Reveal);
        var (plan, meta, _) = Planned(still);
        Assert.Null(meta.Reveal);
        Assert.Null(plan.Changes);
        Assert.Equal(["A still shows the whole cave; the reveal is left out."], ScenePlanBuilder.Notices(still, meta));
        Assert.DoesNotContain("reveal", SceneBuilders.NotDrawnByTheGenerator(still));
    }

    [Fact]
    public void TheMapRevealPreset_RevealsTheCaveFromItsEntrances_ThenShowsItsLabels()
    {
        var spec = BuiltInPresets.ByName("Map reveal")!.Spec with
        {
            Labels = BuiltInPresets.MapReveal().Spec.Labels with { Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 } },
        };
        var (plan, meta, _) = Planned(spec);

        Assert.Equal(["upper0", "lower49"], meta.Reveal!.From);
        var reveal = plan.Changes!.Single(c => c.Reveal is not null);
        var labels = plan.Changes!.Single(c => c.Visible is not null);
        Assert.Equal((0.5, 9.5), (reveal.Reveal![0].T, reveal.Reveal[1].T));
        Assert.Equal(0, reveal.Reveal[0].Value);
        Assert.True(reveal.Reveal[1].Value > meta.Reveal.Max);
        Assert.NotNull(reveal.RevealFront!.Glow);
        Assert.Equal([new PlanVisibleKey(9, true, 2)], labels.Visible);
        Assert.Equal(SceneBuilder.Player, SceneBuilders.Choose(spec).Builder);
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    // ---- which builder ----

    [Fact]
    public void OnlyThePlayerDrawsAReveal()
    {
        var spec = Orbit(ByDistance);

        var auto = SceneBuilders.Choose(spec);
        Assert.Equal((SceneBuilder.Player, 0, 0), (auto.Builder, auto.NotCarried.Count, auto.LeftOut.Count));
        var generator = SceneBuilders.Choose(spec, new RenderOptions { Builder = SceneBuilder.Generator });
        Assert.Equal(SceneBuilder.Generator, generator.Builder);
        Assert.Equal(["reveal"], generator.LeftOut);
        Assert.Null(generator.Refusal);

        // Under an HDRI image the player draws the reveal too: the image no longer sends the
        // scene to the generator, which would leave the reveal out.
        var hdri = spec with { Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "/somewhere/sky.exr" } };
        var routed = SceneBuilders.Choose(hdri);
        Assert.Equal((SceneBuilder.Player, 0, 0), (routed.Builder, routed.NotCarried.Count, routed.LeftOut.Count));
        Assert.Null(SceneBuilders.Choose(hdri, new RenderOptions { Builder = SceneBuilder.Player }).Refusal);
        Assert.Equal(["reveal"], SceneBuilders.Choose(hdri, new RenderOptions { Builder = SceneBuilder.Generator }).LeftOut);
        Assert.Equal(SceneBuilder.Player, SceneBuilders.Choose(hdri, new RenderOptions { Draft = true }).Builder);
    }

    // ---- the conversion ----

    [Fact]
    public void TheConversion_WritesTheValuesIntoThePly_AndTheirRangeIntoTheMetadata()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"thide-reveal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // A .lox without dates and, beside it, the .3d of the same cave with them.
            var model = RevealFixtures.MediumByExpedition();
            string lox = Path.Combine(dir, "cave.lox");
            Therion.Cave3D.Parsing.LoxWriter.WriteFile(model, lox);
            File.WriteAllBytes(Path.Combine(dir, "cave.3d"), RevealFixtures.As3d(model));
            var source = new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
            var spec = Orbit(new RevealSpec { By = RevealBy.Date });

            var found = CaveConversionPipeline.ConvertResolvedFull(source, new ConversionOptions
            {
                OutputDirectory = Path.Combine(dir, "found"), Geometry = RevealPlanner.GeometryFor(spec, null, lox),
            });
            Assert.Equal(("date", 0), (found.Meta.Reveal!.By, found.Meta.Reveal.LegsWithoutDate));
            Assert.Equal(RevealField.DecimalYear(RevealFixtures.UpperDay), found.Meta.Reveal.Min);
            Assert.Contains("property float reveal\n", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(found.Manifest.ModelPath), 0, 400));
            string written = File.ReadAllText(found.Manifest.SceneMetaPath);
            Assert.Contains("\"reveal\": {\n    \"by\": \"date\",", written.ReplaceLineEndings("\n"));
            Assert.Equal(found.Meta.Reveal, Writers.SceneMetaWriter.Read(written).Reveal);
            Assert.Equal(found.Geometry.Walls.Vertices.Count, found.Manifest.WallVertexCount);

            // Named, it is read from where it is; without one, the .lox has no dates.
            File.Move(Path.Combine(dir, "cave.3d"), Path.Combine(dir, "dates.3d"));
            Assert.Null(RevealPlanner.GeometryFor(spec, null, lox).Reveal!.Dates);
            var named = RevealPlanner.GeometryFor(spec with { Reveal = spec.Reveal! with { DatesPath = Path.Combine(dir, "dates.3d") } }, null, lox);
            Assert.Equal(model.Shots.Count, named.Reveal!.Dates!.Shots.Count);
            var missing = Assert.Throws<ArgumentException>(() =>
                RevealPlanner.GeometryFor(spec with { Reveal = spec.Reveal with { DatesPath = Path.Combine(dir, "nowhere.3d") } }, null, lox));
            Assert.StartsWith("reveal.datesPath: file not found: ", missing.Message);
            File.WriteAllText(Path.Combine(dir, "notes.3d"), "not a model");
            var unreadable = Assert.Throws<ArgumentException>(() =>
                RevealPlanner.GeometryFor(spec with { Reveal = spec.Reveal with { DatesPath = Path.Combine(dir, "notes.3d") } }, null, lox));
            Assert.StartsWith("reveal.datesPath: 'notes.3d' cannot be read as a cave model: ", unreadable.Message);

            // Without a reveal the conversion is what it was: no values, no word of them.
            var plain = CaveConversionPipeline.ConvertResolvedFull(source, new ConversionOptions
            {
                OutputDirectory = Path.Combine(dir, "plain"), Geometry = RevealPlanner.GeometryFor(Orbit(null)),
            });
            Assert.Null(plain.Meta.Reveal);
            Assert.DoesNotContain("\"reveal\"", File.ReadAllText(plain.Manifest.SceneMetaPath));
            Assert.DoesNotContain("reveal", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(plain.Manifest.ModelPath), 0, 400));
            // A station to measure from that the model has not is refused by name.
            var nowhere = Assert.Throws<ArgumentException>(() => CaveConversionPipeline.ConvertResolvedFull(source, new ConversionOptions
            {
                OutputDirectory = Path.Combine(dir, "nowhere"), Geometry = RevealPlanner.GeometryFor(Orbit(ByDistance with { From = "nowhere" })),
            }));
            Assert.Equal("The model has no station 'nowhere' to measure the reveal from.", nowhere.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheCentreline_IsALineOfOneStripALeg_WithTheValuesOfItsEnds()
    {
        var geometry = Geometry(InventedCave.Build(InventedCaveSize.Small), Orbit(ByDistance));
        var line = RevealPlanner.Centreline(geometry.Reveal!, "Centreline", 0.4, "rock");

        Assert.Equal(("Centreline", RevealPlanner.CentrelineRole, 0.4, "rock", 16), (line.Id, line.Role, line.Width, line.Material, line.Strips.Count));
        var first = geometry.Reveal!.Legs[0];
        Assert.Equal(2, line.Strips[0].Points.Count);
        Assert.Equal(Math.Round(first.To.X, 6), line.Strips[0].Points[1].X);
        Assert.Equal([0, Math.Round(first.ToValue, 6)], line.Strips[0].Reveal!);
        var plan = ScenePlanBuilder.WithNeededFeatures(PlanFixtures.PlanOf(Orbit(null)) with
        {
            Lines = [line with { Material = ScenePlanBuilder.RockMaterialId }],
            Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "Centreline" }, Reveal = [new PlanNumberKey(0, 0), new PlanNumberKey(5, 200)] }],
        });
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }
}
