// scene-plan.json: what is written reads back the same, stays readable, and is the same bytes on
// every platform and under every culture.
//
// Golden workflow: THIDE_UPDATE_GOLDENS=1 rewrites Goldens/plan-orbit.json, then inspect + commit.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class ScenePlanSerializerTests
{
    [Fact]
    public void APlanWithEveryField_ReadsBackTheSame()
    {
        string written = ScenePlanSerializer.Write(PlanFixtures.Full());

        var read = ScenePlanSerializer.Read(written);

        Assert.Equal(written, ScenePlanSerializer.Write(read));
        Assert.Equal(PlanFixtures.Full().Camera.Track, read.Camera.Track);
        Assert.Equal(PlanFixtures.Full().Models, read.Models);
        Assert.Equal(PlanFixtures.Full().Look.Lights, read.Look.Lights);
        Assert.Equal(PlanFixtures.Full().Frame, read.Frame);
    }

    [Fact]
    public void APlanBuiltFromAPreset_ReadsBackTheSame()
    {
        string written = ScenePlanSerializer.Write(PlanFixtures.PlanOf(PlanFixtures.Preset("Full flythrough")));

        Assert.Equal(written, ScenePlanSerializer.Write(ScenePlanSerializer.Read(written)));
    }

    [Fact]
    public void Words_AreCamelCase_VectorsAreArrays_AndAPoseTakesOneLine()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full());

        Assert.Contains("\"version\": 1,", json);
        Assert.Contains("\"features\": [", json);
        Assert.Contains("\"upAxis\": \"Z\"", json);
        Assert.Contains("\"origin\": [512000.5, 5120000.25, 1200]", json);
        Assert.Contains("\"frameCount\": 51", json);
        Assert.Contains("\"interpolation\": \"bezier\"", json);
        Assert.Contains("      { \"t\": 0, \"eye\": [10, -20, 5.5], \"target\": [0, 0, 0] },\n", json);
        Assert.Contains("      { \"t\": 1, \"eye\": [20, -10, 6], \"target\": [0, 0, 1], \"lens\": 50 },\n", json);
        Assert.Contains("\"engine\": \"eevee\"", json);
        Assert.Contains("\"device\": \"optix\"", json);
        Assert.Contains("\"kind\": \"heightRamp\"", json);
        Assert.Contains("{ \"at\": 1, \"color\": [1, 0.5, 0] }", json);
        Assert.Contains("\"kind\": \"headlamp\"", json);
        Assert.Contains("\"container\": \"webm\"", json);
    }

    [Fact]
    public void WhatIsNotSet_IsNotWritten()
    {
        string json = ScenePlanSerializer.Write(new ScenePlan());

        foreach (string absent in new[] { "\"name\"", "\"crs\"", "\"fStop\"", "\"world\"", "\"material\"", "null" })
            Assert.DoesNotContain(absent, json);
    }

    [Fact]
    public void Lines_EndInLf_AndTheFileEndsWithOne()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full());

        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("}\n", json);
    }

    [Fact]
    public void Writing_IsTheSameUnderRoRoCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        string invariant, romanian;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariant = ScenePlanSerializer.Write(PlanFixtures.Full());
            CultureInfo.CurrentCulture = new CultureInfo("ro-RO");
            romanian = ScenePlanSerializer.Write(PlanFixtures.Full());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        Assert.Equal(invariant, romanian);
        Assert.Contains("5.5", invariant);
    }

    [Fact]
    public void ASmallNumber_IsWrittenAsJsonAndReadsBack()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full());

        Assert.Contains("1E-07", json);
        using var parsed = JsonDocument.Parse(json); // an exponent is JSON too
        Assert.Equal(1e-7, ScenePlanSerializer.Read(json).Camera.Track[2].Target.Y);
    }

    [Fact]
    public void Names_KeepTheirLetters()
    {
        var plan = PlanFixtures.Full() with { Name = "Peștera \"Mare\" — nivelul 2 \\ 'sus'" };

        string json = ScenePlanSerializer.Write(plan);

        Assert.Contains("Peștera", json);
        Assert.Equal(plan.Name, ScenePlanSerializer.Read(json).Name);
    }

    // ---- sections this library does not know ----

    [Fact]
    public void OtherSections_AreKeptAsTheyWere()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full())
            .TrimEnd().TrimEnd('}') + ",\n  \"annotations\": [{ \"id\": \"a1\", \"text\": \"Sala Mare\" }],\n  \"marker_cube\": { \"points\": [[1, 2, 3]] }\n}\n";

        var plan = ScenePlanSerializer.Read(json);

        Assert.Equal(["annotations", "marker_cube"], plan.Sections!.Keys.Order());
        string rewritten = ScenePlanSerializer.Write(plan);
        using var doc = JsonDocument.Parse(rewritten);
        Assert.Equal("Sala Mare", doc.RootElement.GetProperty("annotations")[0].GetProperty("text").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("marker_cube").GetProperty("points")[0][2].GetInt32());
    }

    [Fact]
    public void AnUnknownKeyInsideAKnownSection_IsIgnored()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full()).Replace("\"fps\": 25,", "\"fps\": 25, \"speed\": 2,");

        Assert.Equal(25, ScenePlanSerializer.Read(json).Timeline.Fps);
    }

    // ---- refusals ----

    [Fact]
    public void ANewerPlan_IsRefusedWithAMessageThatSaysSo()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full()).Replace("\"version\": 1,", "\"version\": 2,");

        var ex = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(json));
        Assert.Contains("version 2", ex.Message);
        Assert.Contains("newer", ex.Message);
    }

    [Theory]
    [InlineData("[]", "must be an object")]
    [InlineData("{}", "no \"version\"")]
    [InlineData("{ \"version\": \"1\" }", "must be an integer")]
    [InlineData("{ \"version\": 0 }", "Invalid scene-plan version")]
    [InlineData("{ \"version\": 1, ", "Malformed")]
    [InlineData("{ \"version\": 1, \"look\": { \"engine\": \"povray\" } }", "Malformed")]
    [InlineData("{ \"version\": 1, \"look\": { \"engine\": 1 } }", "Malformed")]
    [InlineData("{ \"version\": 1, \"frame\": { \"origin\": [1, 2] } }", "Malformed")]
    [InlineData("{ \"version\": 1, \"camera\": { \"track\": [{ \"t\": 0, \"eye\": [1, 2, 3] }] } }", "Malformed")]
    public void BadJson_IsRefused(string json, string expected)
    {
        var ex = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Files_AreUtf8WithoutAByteOrderMark()
    {
        string path = Path.Combine(Path.GetTempPath(), $"thide-plan-{Guid.NewGuid():N}.json");
        try
        {
            ScenePlanSerializer.WriteFile(PlanFixtures.Full() with { Name = "Peștera" }, path);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'{', bytes[0]);
            Assert.Equal("Peștera", ScenePlanSerializer.ReadFile(path).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- golden ----

    [Fact]
    public void Golden_OrbitPlanMatchesByteExactly()
    {
        // The fixture of the camera-script goldens: a fixed box, so nothing depends on the host.
        var framing = new CameraFraming { LocalBounds = new BoundingBox(new CaveVector3(-40, -30, -20), new CaveVector3(60, 50, 20)) };
        var spec = new SceneSpec
        {
            Name = "Golden plan",
            Seed = 5,
            Source = new SourceSpec { PlyPath = "assets/model.ply" },
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 32, Gpu = GpuMode.CpuOnly },
            Camera = new CameraSpec
            {
                Template = CameraTemplate.Orbit, FocalLength = 35, AutoFramePadding = 1.2,
                Orbit = new OrbitParams { Revolutions = 0.5, ElevationDegrees = 20 },
            },
            Animation = new AnimationSpec { Fps = 8, DurationSeconds = 2 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 960, Height = 540, OutputDirectory = "out", BaseName = "golden-plan" },
        };
        string json = ScenePlanSerializer.Write(ScenePlanBuilder.FromSpec(spec, framing));
        string goldenPath = GoldenPath("plan-orbit.json");

        if (Environment.GetEnvironmentVariable("THIDE_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllText(goldenPath, json, new UTF8Encoding(false));
            return;
        }

        Assert.True(File.Exists(goldenPath),
            $"Golden file missing: {goldenPath}. Run once with THIDE_UPDATE_GOLDENS=1 to seed it.");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), json);
    }

    private static string GoldenPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string tests = Path.Combine(dir.FullName, "tests", "Therion.Blender.Tests");
            if (Directory.Exists(tests)) return Path.Combine(tests, "Goldens", fileName);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate tests/Therion.Blender.Tests above the test output directory.");
    }
}
