// The shipped JSON schema and the serializer describe the same format: what the library writes
// conforms, and what the schema forbids is what the validator and the player refuse.

using System.Text.Json;
using Therion.Blender.Plan;

namespace Therion.Blender.Tests.Player;

public class ScenePlanSchemaTests
{
    private static IReadOnlyList<string> Problems(string plan) => JsonSchemaSubset.Validate(ScenePlanSchema.Text, plan);

    private static string FullPlan => ScenePlanSerializer.Write(PlanFixtures.Full());

    [Fact]
    public void TheSchema_IsShipped_AndIsForThisVersion()
    {
        using var schema = JsonDocument.Parse(ScenePlanSchema.Text);

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(
            BlenderModule.ScenePlanSchemaVersion,
            schema.RootElement.GetProperty("properties").GetProperty("version").GetProperty("const").GetInt32());
        Assert.DoesNotContain('\r', ScenePlanSchema.Text);
    }

    [Fact]
    public void APlanWithEveryField_Conforms()
        => Assert.Empty(Problems(FullPlan));

    [Fact]
    public void ADefaultPlan_LacksOnlyAPose()
        => Assert.Equal(["$.camera.track: fewer than 1 items"], Problems(ScenePlanSerializer.Write(new ScenePlan())));

    [Fact]
    public void SectionsOfOtherModules_AreAllowed_AndTheReservedOnesAreArrays()
    {
        string withCustom = FullPlan.TrimEnd().TrimEnd('}') + ",\n  \"marker_cube\": { \"points\": [] },\n  \"texts\": []\n}\n";
        string reservedMisused = FullPlan.TrimEnd().TrimEnd('}') + ",\n  \"annotations\": {}\n}\n";

        Assert.Empty(Problems(withCustom));
        Assert.Equal(["$.annotations: expected array, found Object"], Problems(reservedMisused));
    }

    [Theory]
    [InlineData("\"version\": 1,", "\"version\": 2,", "$.version: must be 1")]
    [InlineData("\"units\": \"metres\"", "\"units\": \"feet\"", "$.frame.units: must be \"metres\"")]
    [InlineData("\"upAxis\": \"Z\"", "\"upAxis\": \"Y\"", "$.frame.upAxis: must be \"Z\"")]
    [InlineData("\"fps\": 25", "\"fps\": 0", "$.timeline.fps: below 1")]
    [InlineData("\"fps\": 25", "\"fps\": 29.97", "$.timeline.fps: expected integer, found Number")]
    [InlineData("\"path\": \"model.ply\"", "\"path\": \"/tmp/model.ply\"", "$.models[0].path: '/tmp/model.ply' does not match")]
    [InlineData("\"path\": \"model.ply\"", "\"path\": \"../model.ply\"", "$.models[0].path: '../model.ply' does not match")]
    [InlineData("\"path\": \"model.ply\"", "\"path\": \"C:/model.ply\"", "$.models[0].path: 'C:/model.ply' does not match")]
    [InlineData("\"path\": \"model.ply\"", "\"path\": \"model.obj\"", "$.models[0].path: 'model.obj' does not match")]
    [InlineData("\"interpolation\": \"bezier\"", "\"interpolation\": \"spline\"", "$.camera.interpolation: \"spline\" is not one of")]
    [InlineData("\"target\": [0, 0, 0] }", "\"target\": [0, 0] }", "$.camera.track[0].target: fewer than 3 items")]
    [InlineData("\"engine\": \"eevee\"", "\"engine\": \"workbench\"", "$.look.engine: \"workbench\" is not one of")]
    [InlineData("\"roughness\": 0.5", "\"roughness\": 1.5", "$.look.materials[2].roughness: above 1")]
    [InlineData("\"color\": [0.2, 0.4, 0.6]", "\"colour\": [0.2, 0.4, 0.6]", "$.look.materials[2]: 'colour' is not a known property")]
    [InlineData("\"color\": [0.2, 0.4, 0.6]", "\"color\": [0.2, 0.4, 1.6]", "$.look.materials[2].color[2]: above 1")]
    [InlineData("\"kind\": \"headlamp\"", "\"kind\": \"spot\"", "$.look.lights[1].kind: \"spot\" is not one of")]
    [InlineData("\"baseName\": \"clip\"", "\"baseName\": \"\"", "$.output.baseName: shorter than 1 characters")]
    [InlineData("\"camera_track\"", "\"Camera-Track\"", "$.features[1]: 'Camera-Track' does not match")]
    public void WhatTheFormatForbids_DoesNotConform(string original, string changed, string expected)
    {
        Assert.Contains(original, FullPlan);

        var problems = Problems(FullPlan.Replace(original, changed));

        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingRequiredSection_DoesNotConform()
        => Assert.Equal(
            ["$: 'features' is missing", "$: 'frame' is missing", "$: 'timeline' is missing"],
            Problems("{ \"version\": 1 }"));

    [Fact]
    public void ThePlayersModules_AreTheOnesTheSchemaNames()
    {
        using var schema = JsonDocument.Parse(ScenePlanSchema.Text);
        string described = schema.RootElement.GetProperty("properties").GetProperty("features").GetProperty("description").GetString()!;

        Assert.EndsWith(string.Join(", ", ScenePlan.PlayerFeatures) + ".", described);
    }
}
