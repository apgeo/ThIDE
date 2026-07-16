// SceneSpec model tests (BA-B5 batch 1): validation matrix — every rule has a failing
// case — plus JSON round-trip, camelCase/string-enum shape, version gating (too new /
// invalid / missing), and spec-hash stability/sensitivity.

using Therion.Blender;

namespace Therion.Blender.Tests;

public class SceneSpecTests
{
    /// <summary>A fully-valid baseline spec the failure cases mutate one field at a time.</summary>
    internal static SceneSpec ValidSpec() => new()
    {
        Name = "Peștera Test — orbit",
        Seed = 42,
        Source = new SourceSpec { PlyPath = "assets/model.ply", SceneMetaPath = "assets/scene-meta.json" },
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 64, Gpu = GpuMode.Auto },
        Camera = new CameraSpec { FocalLength = 35, AutoFramePadding = 1.2 },
        Animation = new AnimationSpec { Fps = 30, DurationSeconds = 4 },
        Output = new OutputSpec
        {
            Kind = OutputKind.Video,
            Container = VideoContainer.Mp4,
            Width = 1920,
            Height = 1080,
            OutputDirectory = "out",
            BaseName = "cave-render",
        },
    };

    [Fact]
    public void ValidSpec_HasNoErrors()
    {
        Assert.Empty(SceneSpecValidator.Validate(ValidSpec()));
        Assert.Equal(120, SceneSpecValidator.FrameCount(ValidSpec())); // 30 fps × 4 s
    }

    public static TheoryData<string, SceneSpec> InvalidSpecs()
    {
        var v = ValidSpec();
        return new TheoryData<string, SceneSpec>
        {
            { "version", v with { Version = 999 } },
            { "source.plyPath", v with { Source = v.Source with { PlyPath = " " } } },
            { "source.embedMesh", v with { Source = v.Source with { EmbedMesh = true, SelfContained = false } } },
            { "engine.samples", v with { Engine = v.Engine with { Samples = 0 } } },
            { "engine.samples", v with { Engine = v.Engine with { Samples = 999_999 } } },
            { "camera.focalLength", v with { Camera = v.Camera with { FocalLength = 0 } } },
            { "camera.autoFramePadding", v with { Camera = v.Camera with { AutoFramePadding = 0.1 } } },
            { "animation.fps", v with { Animation = v.Animation with { Fps = 0 } } },
            { "animation.fps", v with { Animation = v.Animation with { Fps = 999 } } },
            { "animation.durationSeconds", v with { Animation = v.Animation with { DurationSeconds = 0 } } },
            { "animation.durationSeconds", v with { Animation = v.Animation with { DurationSeconds = 1e9 } } },
            { "animation.durationSeconds", v with { Animation = v.Animation with { Fps = 1, DurationSeconds = 0.2 } } }, // rounds to 0 frames
            { "output.width", v with { Output = v.Output with { Width = 8 } } },
            { "output.width", v with { Output = v.Output with { Width = 1921 } } },   // odd for video
            { "output.height", v with { Output = v.Output with { Height = 1081 } } }, // odd for video
            { "output.outputDirectory", v with { Output = v.Output with { OutputDirectory = "" } } },
            { "output.baseName", v with { Output = v.Output with { BaseName = "" } } },
            { "output.baseName", v with { Output = v.Output with { BaseName = "a/b" } } },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidSpecs))]
    public void EachRule_HasAFailingCase(string expectedPath, SceneSpec spec)
    {
        var errors = SceneSpecValidator.Validate(spec);
        Assert.Contains(errors, e => e.Path == expectedPath);
    }

    [Fact]
    public void OddResolution_IsFineForStills()
    {
        var spec = ValidSpec() with
        {
            Output = ValidSpec().Output with { Kind = OutputKind.Still, Width = 1921, Height = 1081 },
        };
        Assert.Empty(SceneSpecValidator.Validate(spec));
        Assert.Equal(1, SceneSpecValidator.FrameCount(spec));
    }

    [Fact]
    public void Json_RoundTripsExactly()
    {
        var spec = ValidSpec() with
        {
            Source = ValidSpec().Source with { SelfContained = true, EmbedMesh = true },
            Engine = ValidSpec().Engine with { Kind = RenderEngineKind.Eevee, Gpu = GpuMode.CpuOnly },
            CreatedBy = "ThIDE tests",
        };

        var json = SceneSpecSerializer.Write(spec);
        var back = SceneSpecSerializer.Read(json);

        // Re-serialize to compare: record equality is reference-based for the list members
        // that later batches added (labels events, camera viewpoints), so JSON is the
        // fidelity check. Scalars still get a direct value assert below.
        Assert.Equal(json, SceneSpecSerializer.Write(back));
        Assert.Equal(spec.Source, back.Source);
        Assert.Equal(spec.Engine, back.Engine);
    }

    [Fact]
    public void Json_UsesCamelCaseKeysAndStringEnums()
    {
        var json = SceneSpecSerializer.Write(ValidSpec());
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"plyPath\"", json);
        Assert.Contains("\"kind\": \"Cycles\"", json);
        Assert.Contains("\"gpu\": \"Auto\"", json);
        Assert.Contains("Peștera", json); // diacritics not \u-escaped
    }

    [Fact]
    public void Json_EnumsReadCaseInsensitively_AndUnknownFieldsAreIgnored()
    {
        var json = """
            { "version": 1, "seed": 7, "futureField": true,
              "engine": { "kind": "eevee", "gpu": "cpuOnly" },
              "source": { "plyPath": "m.ply" } }
            """;
        var spec = SceneSpecSerializer.Read(json);
        Assert.Equal(RenderEngineKind.Eevee, spec.Engine.Kind);
        Assert.Equal(GpuMode.CpuOnly, spec.Engine.Gpu);
        Assert.Equal(7, spec.Seed);
        Assert.Equal(128, spec.Engine.Samples); // absent field → default
    }

    [Fact]
    public void Json_NewerVersion_FailsWithClearMessage()
    {
        var ex = Assert.Throws<SceneSpecFormatException>(
            () => SceneSpecSerializer.Read("""{ "version": 99 }"""));
        Assert.Contains("newer", ex.Message);
    }

    [Theory]
    [InlineData("""{ "seed": 1 }""")]              // version missing
    [InlineData("""{ "version": 0 }""")]           // below the first schema
    [InlineData("""{ "version": "one" }""")]       // wrong type
    [InlineData("[]")]                             // not an object
    [InlineData("not json at all")]                // malformed
    public void Json_BadVersionOrShape_Throws(string json)
    {
        Assert.Throws<SceneSpecFormatException>(() => SceneSpecSerializer.Read(json));
    }

    [Fact]
    public void SpecHash_IsStableAndSensitive()
    {
        var a1 = SceneSpecSerializer.ComputeHash(ValidSpec());
        var a2 = SceneSpecSerializer.ComputeHash(ValidSpec());
        var b = SceneSpecSerializer.ComputeHash(ValidSpec() with { Seed = 43 });

        Assert.Equal(a1, a2);                    // deterministic
        Assert.NotEqual(a1, b);                  // any change moves the hash
        Assert.Equal(64, a1.Length);             // sha-256 hex
        Assert.Equal(a1, a1.ToLowerInvariant()); // lowercase
    }
}
