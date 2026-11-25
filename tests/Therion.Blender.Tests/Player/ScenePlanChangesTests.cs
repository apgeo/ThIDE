// The plan's changes — what appears, goes, fades, takes a tint or is stretched while the clip
// runs — without Blender: how they are written and read, and what the validator and the schema
// refuse. In Blender they are in PlayerChangesBlenderTests.

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class ScenePlanChangesTests
{
    private static ScenePlan Full => PlanFixtures.Full();

    private static string FullJson => ScenePlanSerializer.Write(Full);

    // ---- written and read ----

    [Fact]
    public void AKey_TakesOneLine_AndATarget_AndAnEasingIsWrittenOnlyWhenItIsNotEven()
    {
        Assert.Contains(
            "  \"changes\": [\n" +
            "    {\n" +
            "      \"target\": { \"role\": \"station-label\" },\n" +
            "      \"visible\": [\n" +
            "        { \"t\": 0.4, \"value\": true, \"fade\": 0.6 },\n" +
            "        { \"t\": 1.6, \"value\": false }\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"target\": { \"id\": \"CaveModel\" },\n" +
            "      \"opacity\": [\n" +
            "        { \"t\": 0, \"value\": 1 },\n" +
            "        { \"t\": 2, \"value\": 0.25, \"easing\": \"smooth\" }\n" +
            "      ],\n" +
            "      \"tint\": [\n" +
            "        { \"t\": 0.52, \"color\": [1, 0.5, 0], \"strength\": 0 },\n" +
            "        { \"t\": 1, \"color\": [1, 0.5, 0], \"strength\": 0.7, \"easing\": \"constant\" }\n" +
            "      ],\n" +
            "      \"scale\": [\n" +
            "        { \"t\": 0, \"value\": [1, 1, 1] },\n" +
            "        { \"t\": 2, \"value\": [1, 1, 2.5], \"easing\": \"smooth\" }\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"worldColor\": [\n" +
            "        { \"t\": 0, \"value\": [0.05, 0.05, 0.05] },\n" +
            "        { \"t\": 1.2, \"value\": [0.2, 0.4, 0.8] }\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"target\": { \"id\": \"Key\" },\n" +
            "      \"energy\": [\n" +
            "        { \"t\": 0.4, \"value\": 4 },\n" +
            "        { \"t\": 1.2, \"value\": 0, \"easing\": \"smooth\" }\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"target\": { \"id\": \"Headlamp\" },\n" +
            "      \"visible\": [\n" +
            "        { \"t\": 1.2, \"value\": true }\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"target\": { \"id\": \"Legs\" },\n" +
            "      \"visible\": [\n" +
            "        { \"t\": 0, \"value\": true, \"fade\": 0.8, \"easing\": \"smooth\" }\n" +
            "      ],\n" +
            "      \"tint\": [\n" +
            "        { \"t\": 0, \"color\": [1, 0, 0], \"strength\": 1 },\n" +
            "        { \"t\": 1, \"color\": [0, 1, 0], \"strength\": 1, \"mix\": \"srgb\" },\n" +
            "        { \"t\": 1, \"color\": [0, 0, 1], \"strength\": 1, \"easing\": \"constant\" }\n" +
            "      ],\n" +
            "      \"offset\": [\n" +
            "        { \"t\": 0, \"value\": [0, 0, 0] },\n" +
            "        { \"t\": 3.5, \"value\": [0, 25, -4], \"easing\": \"smooth\" }\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"worldStrength\": [\n" +
            "        { \"t\": 0, \"value\": 0.5 },\n" +
            "        { \"t\": 1.2, \"value\": 0.05 }\n" +
            "      ]\n" +
            "    },\n" +
            "    {\n" +
            "      \"target\": { \"id\": \"photo-hall\" },\n" +
            "      \"visible\": [\n" +
            "        { \"t\": 0.4, \"value\": true, \"fade\": 0.2 },\n" +
            "        { \"t\": 1.4, \"value\": false, \"fade\": 0.2, \"easing\": \"smooth\" }\n" +
            "      ],\n" +
            "      \"opacity\": [\n" +
            "        { \"t\": 0, \"value\": 0.9 }\n" +
            "      ]\n" +
            "    }\n" +
            "  ],\n" +
            "  \"texts\": {\n", FullJson);
    }

    [Fact]
    public void Changes_ReadBackTheSame()
    {
        var read = ScenePlanSerializer.Read(FullJson).Changes!;

        var written = Full.Changes!;
        Assert.Equal(written.Count, read.Count);
        Assert.Equal(written.Select(c => c.Target), read.Select(c => c.Target));
        Assert.Equal(written[0].Visible, read[0].Visible);
        Assert.Equal(written[1].Opacity, read[1].Opacity);
        Assert.Equal(written[1].Tint, read[1].Tint);
        Assert.Equal(written[1].Scale, read[1].Scale);
        Assert.Equal(written[2].WorldColor, read[2].WorldColor);
        Assert.Null(read[0].Opacity);
        Assert.Null(read[2].Target);
    }

    [Fact]
    public void APlanWithoutChanges_WritesNoSuchSection_AndTheEmptyListItsNameWasReservedWith_StillReads()
    {
        Assert.DoesNotContain("\"changes\":", ScenePlanSerializer.Write(Full with { Changes = null }));

        string reserved = ScenePlanSerializer.Write(Full with { Changes = null }).TrimEnd().TrimEnd('}') + ",\n  \"changes\": []\n}\n";
        var plan = ScenePlanSerializer.Read(reserved);

        Assert.Empty(plan.Changes!);
        Assert.Null(plan.Sections);
        // Nothing changes, so no module is needed for it.
        Assert.Empty(ScenePlanValidator.Validate(plan with { Features = Full.Features.Where(f => f != ScenePlan.ChangesFeature).ToList() }));
    }

    [Theory]
    [InlineData("\"target\": { \"id\": \"CaveModel\" },", "\"target\": { \"id\": \"CaveModel\" },\n      \"image\": [{ \"t\": 1, \"value\": \"map.png\" }],", "changes[1].image")]
    [InlineData("\"value\": false }", "\"value\": false, \"curve\": \"smooth\" }", "changes[0].visible[1].curve")]
    [InlineData("\"strength\": 0 }", "\"strength\": 0, \"blend\": \"add\" }", "changes[1].tint[0].blend")]
    [InlineData("{ \"role\": \"station-label\" }", "{ \"role\": \"station-label\", \"ids\": [\"a\"] }", "changes[0].target.ids")]
    [InlineData("\"value\": [0.2, 0.4, 0.8] }", "\"value\": [0.2, 0.4, 0.8], \"strength\": 2 }", "changes[2].worldColor[1].strength")]
    public void AKeyOfAChangeThisBuildDoesNotKnow_IsRefusedByItsPath(string original, string changed, string path)
    {
        Assert.Contains(original, FullJson);

        var ex = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(FullJson.Replace(original, changed)));

        Assert.Equal($"{path} is not known to this build (scene plan version 1); a newer build may be needed.", ex.Message);
    }

    [Fact]
    public void AnEasingThatIsNotOne_IsRefused()
    {
        string json = FullJson.Replace("\"easing\": \"constant\"", "\"easing\": \"bounce\"");

        var ex = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(json));

        Assert.StartsWith("Malformed scene plan JSON", ex.Message);
    }

    // ---- the validator ----

    private static ScenePlan With(params PlanChange[] changes) => Full with { Changes = changes };

    private static PlanChange Of(string? id = null, string? role = null) => new() { Target = new PlanChangeTarget { Id = id, Role = role } };

    private static IEnumerable<string> Paths(ScenePlan plan) => ScenePlanValidator.Validate(plan).Select(e => e.Path);

    private static readonly IReadOnlyList<PlanNumberKey> Solid = [new PlanNumberKey(0, 1)];

    [Fact]
    public void Changes_NeedTheirModule()
    {
        var errors = ScenePlanValidator.Validate(Full with { Features = Full.Features.Where(f => f != ScenePlan.ChangesFeature).ToList() });

        var error = Assert.Single(errors);
        Assert.Equal("changes", error.Path);
        Assert.Contains("'changes'", error.Message);
    }

    [Fact]
    public void ATarget_IsAnIdOrARole_OfSomethingThePlanHas()
    {
        Assert.Equal(["changes[0].target"], Paths(With(Of() with { Opacity = Solid })));
        Assert.Equal(["changes[0].target"], Paths(With(Of("CaveModel", "model") with { Opacity = Solid })));
        Assert.Equal(["changes[0].target.role"], Paths(With(Of(role: "Station Label") with { Opacity = Solid })));
        Assert.Equal(["changes[0].target.id"], Paths(With(Of("NoSuchThing") with { Opacity = Solid })));
        Assert.Equal(["changes[0].target.role"], Paths(With(Of(role: "no-such-role") with { Opacity = Solid })));
        Assert.Equal(["changes[0].target"], Paths(With(new PlanChange { Opacity = Solid })));
        Assert.Equal(["changes[0]"], Paths(With(Of("CaveModel"))));

        // A text and a marker without a role have the roles the player gives them.
        Assert.Empty(Paths(With(Of(role: "label") with { Opacity = Solid }, Of(role: "marker") with { Opacity = Solid })));
        Assert.Empty(Paths(With(Of("lead:far.7") with { Opacity = Solid }, Of(role: "terrain") with { Opacity = Solid })));
    }

    [Fact]
    public void AModuleFromElsewhere_MayMakeWhatAChangeNames()
    {
        var plan = With(Of(role: "cube") with { Opacity = Solid }) with { Features = [.. Full.Features, "marker_cube"] };

        Assert.Empty(Paths(plan));
    }

    [Fact]
    public void Keys_AreInRisingOrder_AndMayLieAfterTheClip()
    {
        ScenePlan Opacity(params double[] times) => With(Of("CaveModel") with { Opacity = times.Select(t => new PlanNumberKey(t, 0.5)).ToList() });

        Assert.Empty(Paths(Opacity(0, 0.04, 2)));
        Assert.Equal(["changes[0].opacity"], Paths(Opacity()));
        Assert.Equal(["changes[0].opacity[1].t"], Paths(Opacity(1, 0.5)));
        Assert.Equal(["changes[0].opacity[0].t"], Paths(Opacity(-1)));
        Assert.Equal(["changes[0].opacity[0].t"], Paths(Opacity(double.NaN)));
        // 51 frames at 25 fps end at 2 seconds: a change that ends after the clip is two keys,
        // and the clip ends on the way.
        Assert.Empty(Paths(Opacity(0, 2.04)));
        Assert.Empty(Paths(Opacity(1.5, 600)));
        Assert.Empty(Paths(Opacity(30, 60)));
        Assert.Empty(Paths(With(Of("CaveModel") with { Visible = [new PlanVisibleKey(1.9, false, 4), new PlanVisibleKey(9, true)] })));
    }

    [Fact]
    public void TwoKeys_MayFallOnOneFrame_AMoveThatEndsInAJump_AndNoMore()
    {
        ScenePlan Opacity(params PlanNumberKey[] keys) => With(Of("CaveModel") with { Opacity = keys });
        PlanNumberKey Jump(double t) => new(t, 1, PlanEasing.Constant);

        Assert.Empty(Paths(Opacity(new(0, 0), new(1, 0.5), Jump(1), new(2, 0, PlanEasing.Smooth))));
        // 25 fps: a hundredth of a second later is the same frame.
        Assert.Empty(Paths(Opacity(new(0, 0), new(1, 0.5, PlanEasing.Smooth), Jump(1.01))));
        Assert.Empty(Paths(Opacity(new(1, 0.5), Jump(1))));
        // The second key of a frame is the jump: it says so.
        Assert.Equal(["changes[0].opacity[2].easing"], Paths(Opacity(new(0, 0), new(1, 0.5), new(1, 1))));
        Assert.Equal(["changes[0].opacity[2].easing"], Paths(Opacity(new(0, 0), new(1, 0.5), new(1, 1, PlanEasing.Smooth))));
        Assert.Equal(["changes[0].opacity[3].t"], Paths(Opacity(new(0, 0), new(1, 0.5), Jump(1), Jump(1))));

        // What is only switched has one key to a frame.
        Assert.Equal(
            ["changes[0].visible[1].t"],
            Paths(With(Of("CaveModel") with { Visible = [new PlanVisibleKey(1, true), new PlanVisibleKey(1.01, false)] })));

        // Every track that moves takes them: a colour, a stretch, a place, a light, the world.
        var red = new ColorRgb(1, 0, 0);
        Assert.Empty(Paths(With(
            Of("CaveModel") with
            {
                Tint = [new PlanTintKey(0, red, 0), new PlanTintKey(1, red, 1), new PlanTintKey(1, red, 0.2, PlanEasing.Constant)],
                Scale = [new PlanVectorKey(1, new CaveVector3(1, 1, 2)), new PlanVectorKey(1, new CaveVector3(1, 1, 1), PlanEasing.Constant)],
                Offset = [new PlanVectorKey(1, new CaveVector3(0, 0, 2)), new PlanVectorKey(1, new CaveVector3(0, 0, 0), PlanEasing.Constant)],
            },
            Of("Key") with { Energy = [new PlanNumberKey(0, 4), new PlanNumberKey(1, 2), new PlanNumberKey(1, 0, PlanEasing.Constant)] },
            new PlanChange
            {
                WorldColor = [new PlanColorKey(0, red), new PlanColorKey(1, red), new PlanColorKey(1, new ColorRgb(0, 0, 0), PlanEasing.Constant)],
                WorldStrength = [new PlanNumberKey(1, 1), new PlanNumberKey(1, 0, PlanEasing.Constant)],
            })));
    }

    [Fact]
    public void AFade_IsEvenOrSmooth_AndAColourIsMixedAsLightOrAsAScreen()
    {
        ScenePlan Fading(PlanEasing easing, double fade = 1) => With(Of("CaveModel") with { Visible = [new PlanVisibleKey(0, true, fade, easing)] });
        Assert.Empty(Paths(Fading(PlanEasing.Linear)));
        Assert.Empty(Paths(Fading(PlanEasing.Smooth)));
        Assert.Equal(["changes[0].visible[0].easing"], Paths(Fading(PlanEasing.Constant)));
        Assert.Equal(["changes[0].visible[0].easing"], Paths(Fading(PlanEasing.Constant, fade: 0)));

        var red = new ColorRgb(1, 0, 0);
        Assert.Empty(Paths(With(
            Of("CaveModel") with { Tint = [new PlanTintKey(0, red, 1), new PlanTintKey(1, new ColorRgb(0, 1, 0), 1) { Mix = PlanColorMix.Srgb }] },
            new PlanChange { WorldColor = [new PlanColorKey(0, red), new PlanColorKey(1, new ColorRgb(0, 0, 1), PlanEasing.Smooth) { Mix = PlanColorMix.Srgb }] })));
        Assert.Equal(["changes[0].tint[0].mix"], Paths(With(Of("CaveModel") with { Tint = [new PlanTintKey(0, red, 1) { Mix = (PlanColorMix)7 }] })));
        Assert.Equal(
            ["changes[0].worldColor[0].mix"], Paths(With(new PlanChange { WorldColor = [new PlanColorKey(0, red) { Mix = (PlanColorMix)7 }] })));
        Assert.Equal(["changes[0].opacity[0].easing"], Paths(With(Of("CaveModel") with { Opacity = [new PlanNumberKey(0, 1, (PlanEasing)9)] })));
    }

    [Fact]
    public void ALight_IsShownHiddenAndTurnedUpAndDown_AndNothingElse()
    {
        var on = new[] { new PlanNumberKey(0, 4), new PlanNumberKey(1, 0) };
        Assert.Empty(Paths(With(Of("Key") with { Energy = on, Visible = [new PlanVisibleKey(1, false)] })));
        // Every light has the role "light".
        Assert.Empty(Paths(With(Of(role: "light") with { Energy = on })));
        Assert.Equal(["changes[1].energy"], Paths(With(Of(role: "light") with { Energy = on }, Of("Headlamp") with { Energy = on })));

        Assert.Equal(["changes[0].energy[1].value"], Paths(With(Of("Key") with { Energy = [new PlanNumberKey(0, 1), new PlanNumberKey(1, -1)] })));
        Assert.Equal(["changes[0].energy"], Paths(With(Of("CaveModel") with { Energy = on })));
        Assert.Equal(["changes[0].energy"], Paths(With(Of("lead:far.7") with { Energy = on })));
        Assert.Equal(["changes[0].target"], Paths(With(new PlanChange { Energy = on })));

        // A light has no material: nothing of it fades, and it has no colour to lay a tint over.
        Assert.Equal(["changes[0].visible"], Paths(With(Of("Key") with { Visible = [new PlanVisibleKey(1, false, 0.5)] })));
        Assert.Equal(["changes[0].opacity"], Paths(With(Of("Key") with { Opacity = Solid })));
        Assert.Equal(["changes[0].tint"], Paths(With(Of("Key") with { Tint = [new PlanTintKey(0, new ColorRgb(1, 0, 0), 1)] })));
        Assert.Equal(["changes[0].scale"], Paths(With(Of("Key") with { Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 2))] })));
        Assert.Equal(["changes[0].offset"], Paths(With(Of("Key") with { Offset = [new PlanVectorKey(0, new CaveVector3(1, 1, 2))] })));
    }

    [Fact]
    public void OnlyAModelIsMoved_AndTheWorldsStrengthNeedsAWorld()
    {
        var aside = new[] { new PlanVectorKey(0, new CaveVector3(0, 0, 0)), new PlanVectorKey(1, new CaveVector3(-3, 0, 12.5)) };
        Assert.Empty(Paths(With(Of("CaveModel") with { Offset = aside }, Of(role: "terrain") with { Offset = aside }, Of("Legs") with { Offset = aside })));
        Assert.Equal(["changes[0].offset"], Paths(With(Of("lead:far.7") with { Offset = aside })));
        Assert.Equal(["changes[0].offset"], Paths(With(Of(role: "station-label") with { Offset = aside })));
        Assert.Equal(
            ["changes[0].offset[1].value"],
            Paths(With(Of("CaveModel") with { Offset = [aside[0], new PlanVectorKey(1, new CaveVector3(0, double.NaN, 0))] })));
        Assert.Equal(["changes[1].offset"], Paths(With(Of("CaveModel") with { Offset = aside }, Of(role: "model") with { Offset = aside })));

        var dim = new[] { new PlanNumberKey(0, 1), new PlanNumberKey(1, 0) };
        Assert.Empty(Paths(With(new PlanChange { WorldStrength = dim })));
        // A sky has a strength too.
        Assert.Empty(Paths(With(new PlanChange { WorldStrength = dim }) with { Look = Full.Look with { World = new PlanWorld { Kind = PlanWorldKind.Sky } } }));
        Assert.Equal(["changes[0].worldStrength"], Paths(With(new PlanChange { WorldStrength = dim }) with { Look = Full.Look with { World = null } }));
        Assert.Equal(["changes[0].worldStrength"], Paths(With(Of("CaveModel") with { WorldStrength = dim })));
        Assert.Equal(["changes[0].worldStrength[1].value"], Paths(With(new PlanChange { WorldStrength = [dim[0], new PlanNumberKey(1, -0.5)] })));
        Assert.Equal(
            ["changes[1].worldStrength"], Paths(With(new PlanChange { WorldStrength = dim }, new PlanChange { WorldStrength = dim })));
        // The colour and the strength are two properties of the world.
        Assert.Empty(Paths(With(
            new PlanChange { WorldStrength = dim }, new PlanChange { WorldColor = [new PlanColorKey(0, new ColorRgb(0, 0, 1))] })));
    }

    [Fact]
    public void Values_AreWithinTheirRanges()
    {
        var red = new ColorRgb(1, 0, 0);
        Assert.Equal(["changes[0].opacity[0].value"], Paths(With(Of("CaveModel") with { Opacity = [new PlanNumberKey(0, 1.5)] })));
        Assert.Equal(["changes[0].opacity[0].value"], Paths(With(Of("CaveModel") with { Opacity = [new PlanNumberKey(0, double.NaN)] })));
        Assert.Equal(["changes[0].tint[0].strength"], Paths(With(Of("CaveModel") with { Tint = [new PlanTintKey(0, red, -0.1)] })));
        Assert.Equal(["changes[0].tint[0].color"], Paths(With(Of("CaveModel") with { Tint = [new PlanTintKey(0, new ColorRgb(2, 0, 0), 1)] })));
        Assert.Equal(["changes[0].scale[0].value"], Paths(With(Of("CaveModel") with { Scale = [new PlanVectorKey(0, new CaveVector3(1, 1, 0))] })));
        Assert.Equal(["changes[0].worldColor[0].value"], Paths(With(new PlanChange { WorldColor = [new PlanColorKey(0, new ColorRgb(0, 0, -1))] })));
        Assert.Equal(["changes[0].visible[0].fade"], Paths(With(Of("CaveModel") with { Visible = [new PlanVisibleKey(0, true, -1)] })));
        Assert.Equal(
            ["changes[0].visible[1].value"],
            Paths(With(Of("CaveModel") with { Visible = [new PlanVisibleKey(0, true), new PlanVisibleKey(1, true)] })));
    }

    [Fact]
    public void OnlyAModelIsScaled_AndOnlyAColouredWorldChangesItsColour()
    {
        var doubled = new[] { new PlanVectorKey(0, new CaveVector3(1, 1, 2)) };
        Assert.Empty(Paths(With(Of("CaveModel") with { Scale = doubled }, Of(role: "terrain") with { Scale = doubled })));
        Assert.Equal(["changes[0].scale"], Paths(With(Of("lead:far.7") with { Scale = doubled })));
        Assert.Equal(["changes[0].scale"], Paths(With(Of(role: "station-label") with { Scale = doubled })));

        var blue = new[] { new PlanColorKey(0, new ColorRgb(0, 0, 1)) };
        Assert.Equal(["changes[0].worldColor"], Paths(With(Of("CaveModel") with { WorldColor = blue })));
        Assert.Equal(
            ["changes[0].worldColor"],
            Paths(With(new PlanChange { WorldColor = blue }) with { Look = Full.Look with { World = new PlanWorld { Kind = PlanWorldKind.Sky } } }));
        Assert.Equal(["changes[0].worldColor"], Paths(With(new PlanChange { WorldColor = blue }) with { Look = Full.Look with { World = null } }));
    }

    [Fact]
    public void TwoChanges_DoNotChangeTheSamePropertyOfTheSameThing()
    {
        Assert.Equal(
            ["changes[1].opacity"],
            Paths(With(Of(role: "lead-marker") with { Opacity = Solid }, Of("lead:far.7") with { Opacity = Solid })));
        Assert.Equal(
            ["changes[1].worldColor"],
            Paths(With(
                new PlanChange { WorldColor = [new PlanColorKey(0, new ColorRgb(0, 0, 1))] },
                new PlanChange { WorldColor = [new PlanColorKey(1, new ColorRgb(0, 0, 1))] })));
        // Two properties of one thing are two changes of it.
        Assert.Empty(Paths(With(
            Of("lead:far.7") with { Opacity = Solid },
            Of(role: "lead-marker") with { Visible = [new PlanVisibleKey(1, true, 0.5)] })));
    }

    [Fact]
    public void WhatIsMadeSeeThroughOrTinted_NeedsAMaterial_AndAHardCutDoesNot()
    {
        // A PLY brings no material; the model "Bare" is given none.
        var plan = Full with { Models = [.. Full.Models, new PlanModel { Id = "Bare", Path = "bare.ply" }] };
        ScenePlan Bare(PlanChange change) => plan with { Changes = [change with { Target = new PlanChangeTarget { Id = "Bare" } }] };

        Assert.Equal(["changes[0].opacity"], Paths(Bare(new PlanChange { Opacity = Solid })));
        Assert.Equal(["changes[0].tint"], Paths(Bare(new PlanChange { Tint = [new PlanTintKey(0, new ColorRgb(1, 0, 0), 1)] })));
        Assert.Equal(["changes[0].visible"], Paths(Bare(new PlanChange { Visible = [new PlanVisibleKey(1, true, 0.5)] })));
        Assert.Empty(Paths(Bare(new PlanChange { Visible = [new PlanVisibleKey(1, true)] })));
        // A GLB may bring one, and a terrain wears the material that drapes its image.
        Assert.Empty(Paths(With(Of("Far") with { Opacity = Solid }, Of("Terrain") with { Opacity = Solid })));
    }

    // ---- the schema ----

    [Theory]
    [InlineData("{ \"role\": \"station-label\" }", "{ \"role\": \"station-label\", \"id\": \"stn:entrance\" }", "$.changes[0].target: more than 1 properties")]
    [InlineData("{ \"role\": \"station-label\" }", "{}", "$.changes[0].target: fewer than 1 properties")]
    [InlineData("\"value\": 0.25,", "\"value\": 1.25,", "$.changes[1].opacity[1].value: above 1")]
    [InlineData("\"easing\": \"constant\"", "\"easing\": \"bounce\"", "$.changes[1].tint[1].easing: \"bounce\" is not one of")]
    [InlineData("\"fade\": 0.6", "\"fade\": -1", "$.changes[0].visible[0].fade: below 0")]
    [InlineData("\"value\": true,", "\"value\": 1,", "$.changes[0].visible[0].value: expected boolean, found Number")]
    [InlineData("\"value\": [1, 1, 2.5]", "\"value\": [1, 1, 0]", "$.changes[1].scale[1].value[2]: not above 0")]
    [InlineData("\"strength\": 0.7,", "", "$.changes[1].tint[1]: 'strength' is missing")]
    [InlineData("\"target\": { \"id\": \"CaveModel\" },", "\"target\": { \"id\": \"CaveModel\" }, \"image\": [],", "$.changes[1]: 'image' is not a known property")]
    [InlineData("{ \"t\": 0, \"value\": [0.05, 0.05, 0.05] },\n        { \"t\": 1.2, \"value\": [0.2, 0.4, 0.8] }", "", "$.changes[2].worldColor: fewer than 1 items")]
    [InlineData("\"fade\": 0.8, \"easing\": \"smooth\"", "\"fade\": 0.8, \"easing\": \"constant\"", "$.changes[5].visible[0].easing: \"constant\" is not one of")]
    [InlineData("\"mix\": \"srgb\"", "\"mix\": \"paint\"", "$.changes[5].tint[1].mix: \"paint\" is not one of")]
    [InlineData("{ \"t\": 0.4, \"value\": 4 }", "{ \"t\": 0.4, \"value\": -4 }", "$.changes[3].energy[0].value: below 0")]
    [InlineData("\"value\": [0, 25, -4]", "\"value\": [0, 25]", "$.changes[5].offset[1].value: fewer than 3 items")]
    [InlineData("{ \"t\": 1.2, \"value\": 0.05 }", "{ \"t\": 1.2, \"value\": 0.05, \"mix\": \"srgb\" }", "$.changes[6].worldStrength[1]: 'mix' is not a known property")]
    public void WhatTheFormatForbidsOfAChange_DoesNotConform(string original, string changed, string expected)
    {
        Assert.Contains(original, FullJson);

        var problems = JsonSchemaSubset.Validate(ScenePlanSchema.Text, FullJson.Replace(original, changed));

        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
    }

    // ---- the player's module ----

    [Fact]
    public void ThePlayer_HasTheModule_AfterTheOnesThatMakeWhatItChanges_AndIsANewerPlayer()
    {
        Assert.Contains(PlayerFiles.JobFiles, f => f.Path == "thide_player/modules/changes.py");
        var order = ScenePlan.PlayerFeatures.ToList();
        Assert.True(order.IndexOf("look") < order.IndexOf(ScenePlan.ChangesFeature));
        Assert.True(order.IndexOf(ScenePlan.TexturedMaterialsFeature) < order.IndexOf(ScenePlan.ChangesFeature));
        Assert.True(order.IndexOf(ScenePlan.LabelsFeature) < order.IndexOf(ScenePlan.ChangesFeature));
        Assert.True(order.IndexOf(ScenePlan.ChangesFeature) < order.IndexOf(ScenePlan.BlendFileFeature));
        Assert.Equal("1.8", PlayerFiles.Version);
        Assert.Equal(1, PlayerFiles.PlanVersion);
    }
}
