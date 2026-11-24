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
    public void SectionsOfOtherModules_AreAllowed_AndTheReservedOneIsAnArray()
    {
        string withCustom = FullPlan.TrimEnd().TrimEnd('}') + ",\n  \"marker_cube\": { \"points\": [] },\n  \"annotations\": []\n}\n";
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
    [InlineData("\"degrees\": 30", "\"angle\": 30", "$.models[1].rotation: 'degrees' is missing")]
    [InlineData("\"axis\": [0, 0, 1]", "\"axis\": \"Z\"", "$.models[1].rotation.axis: expected array, found String")]
    [InlineData("\"scale\": [1, 1, 2]", "\"scale\": [1, 1, 0]", "$.models[1].scale[2]: not above 0")]
    [InlineData("\"scale\": [1, 1, 2]", "\"scale\": 2", "$.models[1].scale: expected array, found Number")]
    [InlineData("\"interpolation\": \"bezier\"", "\"interpolation\": \"spline\"", "$.camera.interpolation: \"spline\" is not one of")]
    [InlineData("\"target\": [0, 0, 0],", "\"target\": [0, 0],", "$.camera.track[0].target: fewer than 3 items")]
    [InlineData("\"orientation\": [1, 0, 0, 0]", "\"orientation\": [1, 0, 0]", "$.camera.track[0].orientation: fewer than 4 items")]
    [InlineData("\"orientation\": [1, 0, 0, 0]", "\"orientation\": [1, 0, 0, 0, 0]", "$.camera.track[0].orientation: more than 4 items")]
    [InlineData("\"orientation\": [1, 0, 0, 0]", "\"orientation\": [2, 0, 0, 0]", "$.camera.track[0].orientation[0]: above 1")]
    [InlineData("\"engine\": \"eevee\"", "\"engine\": \"povray\"", "$.look.engine: \"povray\" is not one of")]
    [InlineData("\"roughness\": 0.5", "\"roughness\": 1.5", "$.look.materials[2].roughness: above 1")]
    [InlineData("\"color\": [0.2, 0.4, 0.6]", "\"colour\": [0.2, 0.4, 0.6]", "$.look.materials[2]: 'colour' is not a known property")]
    [InlineData("\"color\": [0.2, 0.4, 0.6]", "\"color\": [0.2, 0.4, 1.6]", "$.look.materials[2].color[2]: above 1")]
    [InlineData("\"kind\": \"headlamp\"", "\"kind\": \"spot\"", "$.look.lights[1].kind: \"spot\" is not one of")]
    [InlineData("\"baseName\": \"clip\"", "\"baseName\": \"\"", "$.output.baseName: shorter than 1 characters")]
    [InlineData("\"role\": \"station-label\"", "\"role\": \"Station Label\"", "$.labels.texts.items[0].role: 'Station Label' does not match")]
    [InlineData("\"text\": \"two\\nlines\", ", "", "$.labels.texts.items[1]: 'text' is missing")]
    [InlineData("\"size\": 0.5,", "\"size\": 0,", "$.labels.texts.items[1].size: not above 0")]
    [InlineData("\"follows\": \"here\"", "\"follows\": \"\"", "$.labels.texts.items[1].follows: shorter than 1 characters")]
    [InlineData("\"radius\": 0.3 }", "\"radius\": 0.3, \"shape\": \"cube\" }", "$.labels.markers.items[0]: 'shape' is not a known property")]
    [InlineData("\"period\": 0.56", "\"period\": 0", "$.labels.markers.pulse.period: not above 0")]
    [InlineData("\"factor\": 0.015,", "", "$.labels.markers.byDistance: 'factor' is missing")]
    [InlineData("\"strength\": 1.25", "\"strength\": -1", "$.labels.texts.strength: below 0")]
    [InlineData("\"baseName\": \"clip\"", "\"baseName\": \"clip\", \"render\": \"no\"", "$.output.render: expected boolean, found String")]
    [InlineData("\"path\": \"project/scene.blend\"", "\"path\": \"/tmp/scene.blend\"", "$.blend.path: '/tmp/scene.blend' does not match")]
    [InlineData("\"path\": \"project/scene.blend\"", "\"path\": \"project/scene.txt\"", "$.blend.path: 'project/scene.txt' does not match")]
    [InlineData("\"path\": \"project/scene.blend\"", "\"file\": \"project/scene.blend\"", "$.blend: 'path' is missing")]
    [InlineData("\"camera_track\"", "\"Camera-Track\"", "$.features[1]: 'Camera-Track' does not match")]
    public void WhatTheFormatForbids_DoesNotConform(string original, string changed, string expected)
    {
        Assert.Contains(original, FullPlan);

        var problems = Problems(FullPlan.Replace(original, changed));

        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryObjectButThePlan_IsClosed()
    {
        using var schema = JsonDocument.Parse(ScenePlanSchema.Text);
        var open = new List<string>();
        void Walk(JsonElement node, string path)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in node.EnumerateArray()) Walk(item, $"{path}[{i++}]");
                return;
            }
            if (node.ValueKind != JsonValueKind.Object) return;
            if (path.Length > 0 && node.TryGetProperty("properties", out _)
                && !(node.TryGetProperty("additionalProperties", out var closed) && closed.ValueKind == JsonValueKind.False))
                open.Add(path);
            foreach (var property in node.EnumerateObject()) Walk(property.Value, path + "/" + property.Name);
        }

        Walk(schema.RootElement, "");

        Assert.Empty(open);
        Assert.False(schema.RootElement.TryGetProperty("additionalProperties", out _));
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
