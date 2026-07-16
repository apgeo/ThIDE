// Preset (de)serialization tests (BA-B9 batch 1): JSON round-trip, camelCase/string enums,
// envelope version gating, ToRenderSpec grafting, and the malformed/too-new failure paths.

using Therion.Blender;
using Therion.Blender.Presets;

namespace Therion.Blender.Tests;

public class PresetSerializerTests
{
    private static RenderPreset SamplePreset() => new()
    {
        Name = "Peștera orbit",
        Description = "A gentle turntable",
        BuiltIn = false,
        Spec = new SceneSpec
        {
            Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 2 } },
            Engine = new EngineSpec { Samples = 96 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 1920, Height = 1080 },
        },
    };

    [Fact]
    public void Json_RoundTrips()
    {
        var preset = SamplePreset();
        var json = PresetSerializer.Write(preset);
        var back = PresetSerializer.Read(json);

        Assert.Equal(json, PresetSerializer.Write(back)); // list members ⇒ compare JSON
        Assert.Equal("Peștera orbit", back.Name);
        Assert.Equal(CameraTemplate.Orbit, back.Spec.Camera.Template);
        Assert.Equal(2, back.Spec.Camera.Orbit!.Revolutions);
    }

    [Fact]
    public void Json_UsesCamelCaseAndStringEnums_AndKeepsDiacritics()
    {
        var json = PresetSerializer.Write(SamplePreset());
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"name\": \"Peștera orbit\"", json); // diacritics not escaped
        Assert.Contains("\"template\": \"Orbit\"", json);
    }

    [Fact]
    public void ToRenderSpec_GraftsSourceAndOutputLocation()
    {
        var preset = SamplePreset();
        var source = new SourceSpec { PlyPath = "job/model.ply", SceneMetaPath = "job/scene-meta.json" };
        var spec = preset.ToRenderSpec(source, "renders", "my-cave");

        Assert.Equal("job/model.ply", spec.Source.PlyPath);
        Assert.Equal("renders", spec.Output.OutputDirectory);
        Assert.Equal("my-cave", spec.Output.BaseName);
        Assert.Equal(OutputKind.Video, spec.Output.Kind); // preset presentation preserved
        Assert.Empty(SceneSpecValidator.Validate(spec));   // now complete + valid
    }

    [Fact]
    public void Read_NewerVersion_FailsClearly()
    {
        var ex = Assert.Throws<PresetFormatException>(() => PresetSerializer.Read("""{ "version": 99, "name": "x", "spec": {} }"""));
        Assert.Contains("newer", ex.Message);
    }

    [Theory]
    [InlineData("""{ "name": "x", "spec": {} }""")]     // no version
    [InlineData("""{ "version": 0, "name": "x" }""")]   // below first schema
    [InlineData("""{ "version": 1, "spec": {} }""")]    // no name
    [InlineData("[]")]                                  // not an object
    [InlineData("nonsense")]                            // malformed
    public void Read_BadEnvelope_Throws(string json)
    {
        Assert.Throws<PresetFormatException>(() => PresetSerializer.Read(json));
    }
}
