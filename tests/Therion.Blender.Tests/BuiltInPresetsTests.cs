// Built-in gallery tests (BA-B9 batch 2): every shipped preset grafts onto a job to a
// valid spec, the five headline templates are distinct, and each preset round-trips
// through the serializer.

using Therion.Blender;
using Therion.Blender.Presets;

namespace Therion.Blender.Tests;

public class BuiltInPresetsTests
{
    private static readonly SourceSpec Source = new() { PlyPath = "job/model.ply", SceneMetaPath = "job/scene-meta.json" };

    [Fact]
    public void Gallery_HasTheFiveHeadlinePresets_AllBuiltIn()
    {
        var names = BuiltInPresets.All.Select(p => p.Name).ToList();
        Assert.Equal(5, names.Count);
        Assert.Equal(names, names.Distinct().ToList());
        Assert.All(BuiltInPresets.All, p => Assert.True(p.BuiltIn));
        Assert.Contains("Orbit showcase", names);
        Assert.Contains("Documentation stills", names);
    }

    [Fact]
    public void EveryPreset_GraftsToAValidSpec()
    {
        foreach (var preset in BuiltInPresets.All)
        {
            var spec = preset.ToRenderSpec(Source, "renders", "cave");
            var errors = SceneSpecValidator.Validate(spec);
            Assert.True(errors.Count == 0, $"{preset.Name}: {string.Join("; ", errors.Select(e => e.Path))}");
        }
    }

    [Fact]
    public void Presets_CoverTheDistinctCameraTemplates()
    {
        var templates = BuiltInPresets.All.Select(p => p.Spec.Camera.Template).ToList();
        Assert.Contains(CameraTemplate.Orbit, templates);
        Assert.Contains(CameraTemplate.Helix, templates);
        Assert.Contains(CameraTemplate.Flythrough, templates);
        Assert.Contains(CameraTemplate.StillSet, templates);
    }

    [Fact]
    public void DocumentationStills_IsAFrameSequenceStillSet()
    {
        var preset = BuiltInPresets.ByName("documentation stills"); // case-insensitive
        Assert.NotNull(preset);
        Assert.Equal(CameraTemplate.StillSet, preset!.Spec.Camera.Template);
        Assert.Equal(OutputKind.FrameSequence, preset.Spec.Output.Kind);
        var spec = preset.ToRenderSpec(Source, "out", "stills");
        Assert.Equal(4, SceneSpecValidator.FrameCount(spec)); // Top/Front/Left/IsoNE
    }

    [Fact]
    public void EveryPreset_RoundTripsThroughTheSerializer()
    {
        foreach (var preset in BuiltInPresets.All)
        {
            var json = PresetSerializer.Write(preset);
            var back = PresetSerializer.Read(json);
            Assert.Equal(json, PresetSerializer.Write(back));
            Assert.Equal(preset.Name, back.Name);
        }
    }

    [Fact]
    public void ByName_ReturnsNull_ForUnknown()
    {
        Assert.Null(BuiltInPresets.ByName("no such preset"));
    }
}
