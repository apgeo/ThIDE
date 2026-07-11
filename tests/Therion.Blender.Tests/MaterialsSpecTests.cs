// Materials/lighting spec tests (BA-B7 batch 1): the validation matrix + JSON round-trip
// of the new Materials/Lighting sub-objects. The emitter output is covered by the golden
// tests; this file guards the spec surface only.

using Therion.Blender;

namespace Therion.Blender.Tests;

public class MaterialsSpecTests
{
    private static SceneSpec Base() => SceneSpecTests.ValidSpec();

    [Fact]
    public void Defaults_AreValid()
    {
        var spec = Base();
        Assert.Empty(SceneSpecValidator.Validate(spec));
        Assert.Equal(RockMaterial.DepthGradient, spec.Materials.Rock);
        Assert.Equal(LightingRig.SunSky, spec.Lighting.Rig);
    }

    [Fact]
    public void HdriRig_NeedsAPath()
    {
        var noPath = Base() with { Lighting = new LightingSpec { Rig = LightingRig.HdriFile } };
        Assert.Contains(SceneSpecValidator.Validate(noPath), e => e.Path == "lighting.hdriPath");

        var withPath = Base() with { Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "/hdris/cave.exr" } };
        Assert.Empty(SceneSpecValidator.Validate(withPath));
    }

    public static TheoryData<string, SceneSpec> InvalidSpecs()
    {
        var b = Base();
        return new TheoryData<string, SceneSpec>
        {
            { "materials.roughness", b with { Materials = b.Materials with { Roughness = 1.5 } } },
            { "materials.proceduralScale", b with { Materials = b.Materials with { ProceduralScale = 0 } } },
            { "materials.bumpStrength", b with { Materials = b.Materials with { BumpStrength = 99 } } },
            { "materials.baseColor", b with { Materials = b.Materials with { BaseColor = new ColorRgb(1.2, 0, 0) } } },
            { "lighting.strength", b with { Lighting = b.Lighting with { Strength = -1 } } },
            { "lighting.strength", b with { Lighting = b.Lighting with { Strength = 1000 } } },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidSpecs))]
    public void EachRule_HasAFailingCase(string expectedPath, SceneSpec spec)
    {
        Assert.Contains(SceneSpecValidator.Validate(spec), e => e.Path == expectedPath);
    }

    [Fact]
    public void Json_RoundTripsMaterialsAndLighting()
    {
        var spec = Base() with
        {
            Materials = new MaterialsSpec
            {
                Rock = RockMaterial.Procedural, BaseColor = new ColorRgb(0.3, 0.25, 0.2),
                Roughness = 0.7, ProceduralScale = 12.5, BumpStrength = 0.4,
            },
            Lighting = new LightingSpec { Rig = LightingRig.HdriFile, Strength = 2.5, HdriPath = "/hdris/cave.exr" },
        };

        var json = SceneSpecSerializer.Write(spec);
        var back = SceneSpecSerializer.Read(json);
        Assert.Equal(json, SceneSpecSerializer.Write(back));
        Assert.Equal(RockMaterial.Procedural, back.Materials.Rock);
        Assert.Equal(new ColorRgb(0.3, 0.25, 0.2), back.Materials.BaseColor);
        Assert.Equal(LightingRig.HdriFile, back.Lighting.Rig);
        Assert.Equal("/hdris/cave.exr", back.Lighting.HdriPath);
    }

    [Fact]
    public void Json_UsesStringEnums_ForMaterialsAndLighting()
    {
        var json = SceneSpecSerializer.Write(Base());
        Assert.Contains("\"rock\": \"DepthGradient\"", json);
        Assert.Contains("\"rig\": \"SunSky\"", json);
    }
}
