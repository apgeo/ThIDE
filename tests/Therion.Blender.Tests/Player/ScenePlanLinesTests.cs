// A plan's lines, the space of a ramp over height and the clip range of a camera pose, without
// Blender: how they are written and read, and what the validator and the schema refuse. In
// Blender they are in PlayerLinesBlenderTests.

using Therion.Blender.Plan;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class ScenePlanLinesTests
{
    private static ScenePlan Full => PlanFixtures.Full();

    private static string FullJson => ScenePlanSerializer.Write(Full);

    private static IEnumerable<string> Paths(ScenePlan plan) => ScenePlanValidator.Validate(plan).Select(e => e.Path);

    private static ScenePlan WithLine(Func<PlanLine, PlanLine> change) => Full with { Lines = [change(Full.Lines![0])] };

    private static readonly IReadOnlyList<CaveVector3> TwoPoints = [new CaveVector3(0, 0, 0), new CaveVector3(1, 2, 3)];

    // ---- lines ----

    [Fact]
    public void ALine_IsWrittenAfterTheModels_WithAStripToALine_AndReadsBackTheSame()
    {
        Assert.Contains(
            "  \"lines\": [\n" +
            "    {\n" +
            "      \"id\": \"Legs\",\n" +
            "      \"role\": \"survey-lines\",\n" +
            "      \"width\": 0.6,\n" +
            "      \"material\": \"baked\",\n" +
            "      \"translation\": [5, 0, -2],\n" +
            "      \"rotation\": {\n" +
            "        \"axis\": [0, 0, 1],\n" +
            "        \"degrees\": -15\n" +
            "      },\n" +
            "      \"scale\": [1, 1, 2],\n" +
            "      \"strips\": [\n" +
            "        { \"points\": [[0, 0, 0], [10, 0, -3], [10, 8, -3.5]], \"colors\": [[1, 0, 0], [0, 1, 0], [0, 0, 1]] },\n" +
            "        { \"points\": [[10, 0, -3], [14, -2, -9]], \"colors\": [[0, 1, 0], [1, 1, 0]] }\n" +
            "      ]\n" +
            "    }\n" +
            "  ],\n" +
            "  \"camera\": {\n", FullJson);

        var read = ScenePlanSerializer.Read(FullJson).Lines!;
        var line = Assert.Single(read);
        Assert.Equal(Full.Lines![0] with { Strips = [] }, line with { Strips = [] });
        Assert.Equal(2, line.Strips.Count);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(Full.Lines[0].Strips[i].Points, line.Strips[i].Points);
            Assert.Equal(Full.Lines[0].Strips[i].Colors, line.Strips[i].Colors);
        }
        Assert.Equal(FullJson, ScenePlanSerializer.Write(ScenePlanSerializer.Read(FullJson)));

        // A line in the colour of its material has no colours; a plan without lines has no section.
        string plain = ScenePlanSerializer.Write(WithLine(l => l with { Strips = [new PlanLineStrip(TwoPoints)] }));
        Assert.Contains("        { \"points\": [[0, 0, 0], [1, 2, 3]] }\n", plain);
        Assert.Null(ScenePlanSerializer.Read(plain).Lines![0].Strips[0].Colors);
        Assert.DoesNotContain("\"lines\"", ScenePlanSerializer.Write(Full with { Lines = null, Changes = null, Features = Full.Features.Where(f => f != "lines" && f != "changes").ToList() }));
    }

    [Fact]
    public void ALine_NeedsItsModule_AnIdOfItsOwn_AWidth_AndStripsOfTwoPointsOrMore()
    {
        Assert.Empty(Paths(Full));
        Assert.Equal(["lines"], Paths(Full with { Features = Full.Features.Where(f => f != ScenePlan.LinesFeature).ToList() }));
        var order = ScenePlan.PlayerFeatures.ToList();
        Assert.True(order.IndexOf("models") < order.IndexOf(ScenePlan.LinesFeature) && order.IndexOf(ScenePlan.LinesFeature) < order.IndexOf("look"));
        // The look gives a line its material, so the lines are built before it.
        Assert.Equal(["features"], Paths(Full with { Features = [.. Full.Features.Where(f => f != "lines" && f != "output"), "lines", "output"] }));

        // (The change of the fixture that named the line now names nothing.)
        Assert.Equal(["lines[0].id", "changes[5].target.id"], Paths(WithLine(l => l with { Id = " " })));
        // A line is a model to everything after it: the two share their ids.
        Assert.Equal("lines[0].id", Paths(WithLine(l => l with { Id = "CaveModel" })).First());
        Assert.Equal("lines[1].id", Paths(Full with { Lines = [Full.Lines![0], Full.Lines[0]] }).First());
        Assert.Equal(["lines[0].role"], Paths(WithLine(l => l with { Role = "Survey Lines" })));
        Assert.Equal(["lines[0].width"], Paths(WithLine(l => l with { Width = 0 })));
        Assert.Equal(["lines[0].width"], Paths(WithLine(l => l with { Width = double.NaN })));
        Assert.Equal(["lines[0].material"], Paths(WithLine(l => l with { Material = "no-such" })));
        Assert.Equal(["lines[0].scale"], Paths(WithLine(l => l with { Scale = new CaveVector3(1, 0, 1) })));
        Assert.Equal(["lines[0].rotation.axis"], Paths(WithLine(l => l with { Rotation = new PlanRotation { Axis = new CaveVector3(0, 0, 0), Degrees = 5 } })));
        Assert.Equal(["lines[0].translation"], Paths(WithLine(l => l with { Translation = new CaveVector3(double.PositiveInfinity, 0, 0) })));

        Assert.Equal(["lines[0].strips"], Paths(WithLine(l => l with { Strips = [] })));
        Assert.Equal(["lines[0].strips[0].points"], Paths(WithLine(l => l with { Strips = [new PlanLineStrip([new CaveVector3(0, 0, 0)])] })));
        Assert.Equal(["lines[0].strips[0].points"], Paths(WithLine(l => l with { Strips = [new PlanLineStrip([TwoPoints[0], new CaveVector3(0, double.NaN, 0)])] })));
        Assert.Equal(["lines[0].strips[0].colors"], Paths(WithLine(l => l with { Strips = [new PlanLineStrip(TwoPoints, [new ColorRgb(1, 0, 0)])] })));
        Assert.Equal(
            ["lines[0].strips[0].colors"],
            Paths(WithLine(l => l with { Strips = [new PlanLineStrip(TwoPoints, [new ColorRgb(1, 0, 0), new ColorRgb(0, 2, 0)])] })));
        // Either every strip has colours or none has.
        Assert.Equal(["lines[0].strips"], Paths(WithLine(l => l with { Strips = [l.Strips[0], new PlanLineStrip(TwoPoints)] })));
        // A line without a material is built, and has nothing a change could make see-through.
        Assert.Equal(["changes[5].visible", "changes[5].tint"], Paths(WithLine(l => l with { Material = null })));
    }

    [Theory]
    [InlineData("\"width\": 0.6,", "\"width\": 0,", "$.lines[0].width: not above 0")]
    [InlineData("\"width\": 0.6,", "", "$.lines[0]: 'width' is missing")]
    [InlineData("{ \"points\": [[10, 0, -3], [14, -2, -9]], \"colors\": [[0, 1, 0], [1, 1, 0]] }", "{ \"points\": [[10, 0, -3]] }", "$.lines[0].strips[1].points: fewer than 2 items")]
    [InlineData("\"colors\": [[0, 1, 0], [1, 1, 0]]", "\"colors\": [[0, 1, 0], [1, 1, 2]]", "$.lines[0].strips[1].colors[1][2]: above 1")]
    [InlineData("\"colors\": [[0, 1, 0], [1, 1, 0]]", "\"colours\": [[0, 1, 0], [1, 1, 0]]", "$.lines[0].strips[1]: 'colours' is not a known property")]
    [InlineData("\"role\": \"survey-lines\",", "\"role\": \"Survey Lines\",", "$.lines[0].role: 'Survey Lines' does not match")]
    [InlineData("\"space\": \"model\"", "\"space\": \"world\"", "$.look.materials[0].space: \"world\" is not one of")]
    [InlineData("\"clipStart\": 0.5,", "\"clipStart\": 0,", "$.camera.track[1].clipStart: not above 0")]
    [InlineData("\"clipEnd\": 800", "\"clipFar\": 800", "$.camera.track[1]: 'clipFar' is not a known property")]
    public void WhatTheFormatForbidsOfALineARampOrAPose_DoesNotConform(string original, string changed, string expected)
    {
        Assert.Contains(original, FullJson);

        var problems = JsonSchemaSubset.Validate(ScenePlanSchema.Text, FullJson.Replace(original, changed));

        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("\"width\": 0.6,", "\"width\": 0.6,\n      \"sides\": 8,", "lines[0].sides")]
    [InlineData("\"colors\": [[0, 1, 0], [1, 1, 0]]", "\"colors\": [[0, 1, 0], [1, 1, 0]], \"widths\": [1, 2]", "lines[0].strips[1].widths")]
    [InlineData("\"clipEnd\": 800", "\"clipEnd\": 800, \"fStop\": 2", "camera.track[1].fStop")]
    [InlineData("\"space\": \"model\"", "\"space\": \"model\",\n        \"axis\": \"z\"", "look.materials[0].axis")]
    public void AKeyThisBuildDoesNotKnow_IsRefusedByItsPath(string original, string changed, string path)
    {
        Assert.Contains(original, FullJson);

        var refused = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(FullJson.Replace(original, changed)));

        Assert.StartsWith(path + " is not known to this build", refused.Message);
    }

    // ---- the space of a ramp ----

    [Fact]
    public void OnlyAHeightRamp_HasASpace_WrittenWhenItIsSaid()
    {
        var look = Full.Look;
        ScenePlan With(PlanMaterial material) => Full with { Look = look with { Materials = [material, .. look.Materials.Skip(1)] } };

        Assert.Empty(Paths(With(look.Materials[0] with { Space = PlanRampSpace.Scene })));
        Assert.Empty(Paths(With(look.Materials[0] with { Space = null })));
        Assert.Equal(["look.materials[0].space"], Paths(With(look.Materials[0] with { Space = (PlanRampSpace)5 })));
        Assert.Equal(["look.materials[2].space"], Paths(Full with
        {
            Look = look with { Materials = [.. look.Materials.Take(2), look.Materials[2] with { Space = PlanRampSpace.Model }, look.Materials[3]] },
        }));

        Assert.Contains("\"space\": \"scene\"", ScenePlanSerializer.Write(With(look.Materials[0] with { Space = PlanRampSpace.Scene })));
        Assert.DoesNotContain("\"space\"", ScenePlanSerializer.Write(With(look.Materials[0] with { Space = null })));
        Assert.Equal(PlanRampSpace.Model, ScenePlanSerializer.Read(FullJson).Look.Materials[0].Space);
    }

    // ---- the clip range of a pose ----

    [Fact]
    public void APose_MayDrawWithinAClipRangeOfItsOwn_ThatIsARange()
    {
        var camera = Full.Camera;   // draws from 0.05 to 5000
        ScenePlan With(double? start, double? end) => Full with
        {
            Camera = camera with { Track = [camera.Track[0], camera.Track[1] with { ClipStart = start, ClipEnd = end }, camera.Track[2]] },
        };

        Assert.Empty(Paths(With(0.5, 800)));
        Assert.Empty(Paths(With(2, null)));
        Assert.Empty(Paths(With(null, 9000)));
        Assert.Equal(["camera.track[1].clipStart"], Paths(With(0, 800)));
        Assert.Equal(["camera.track[1].clipStart"], Paths(With(double.NaN, 800)));
        Assert.Equal(["camera.track[1].clipEnd"], Paths(With(10, 5)));
        Assert.Equal(["camera.track[1].clipEnd"], Paths(With(null, 0.01)));
        Assert.Equal(["camera.track[1].clipEnd"], Paths(With(1, double.PositiveInfinity)));
        // Beyond the far limit the camera gives a pose that has none of its own.
        Assert.Equal(["camera.track[1].clipStart"], Paths(With(6000, null)));

        var read = ScenePlanSerializer.Read(FullJson).Camera.Track;
        Assert.Equal((null, null), (read[0].ClipStart, read[0].ClipEnd));
        Assert.Equal((0.5, 800.0), (read[1].ClipStart, read[1].ClipEnd));
        Assert.Contains("\"clipStart\": 2 }", ScenePlanSerializer.Write(With(2, null)));
    }
}
