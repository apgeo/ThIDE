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
        Assert.Equal(PlanFixtures.Full().Labels!.Texts!.Items, read.Labels!.Texts!.Items);
        Assert.Equal(PlanFixtures.Full().Labels!.Markers!.Items, read.Labels.Markers!.Items);
        Assert.Equal(PlanFixtures.Full().Labels!.Markers!.Pulse, read.Labels.Markers.Pulse);
        Assert.Equal(PlanFixtures.Full().Labels!.Markers!.ByDistance, read.Labels.Markers.ByDistance);
        Assert.Equal(
            PlanFixtures.Full().TexturedMaterials!.Select(m => m with { Models = [] }),
            read.TexturedMaterials!.Select(m => m with { Models = [] }));
        Assert.Equal(["Terrain"], read.TexturedMaterials![0].Models);
        var texts = PlanFixtures.Full().Texts!;
        Assert.Equal((texts.Hold, texts.FromBlack, texts.ToBlack), (read.Texts!.Hold, read.Texts.FromBlack, read.Texts.ToBlack));
        Assert.Equal(texts.Items.Select(i => i with { Turn = null }), read.Texts.Items.Select(i => i with { Turn = null }));
        Assert.Equal(texts.Items[3].Turn, read.Texts.Items[3].Turn);
    }

    [Fact]
    public void AThingWrittenOverThePicture_TakesOneLine_AndSoDoesEachTurnOfAnArrow()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full());

        Assert.Contains(
            "  \"texts\": {\n" +
            "    \"hold\": 3,\n" +
            "    \"fromBlack\": 0.5,\n" +
            "    \"toBlack\": 1,\n" +
            "    \"items\": [\n" +
            "      { \"id\": \"title\", \"role\": \"title\", \"kind\": \"text\", \"at\": [0.5, 0.95], \"text\": \"Peștera \\\"de sus\\\"\", \"size\": 0.06, \"alignY\": \"top\", \"wrap\": 0.6, \"end\": 4.8, \"fadeOut\": 0.8 },\n" +
            "      { \"id\": \"caption.1\", \"role\": \"caption\", \"kind\": \"text\", \"at\": [0.5, 0.12], \"text\": \"two\\nlines\", \"size\": 0.042, \"alignX\": \"left\", \"alignY\": \"bottom\", \"color\": [1, 0.9, 0.6], \"opacity\": 0.9, \"backing\": true, \"start\": 0.4, \"end\": 1.6, \"fadeIn\": 0.2, \"fadeOut\": 0.2, \"slot\": \"caption\" },\n" +
            "      { \"id\": \"scale-bar\", \"role\": \"scale-bar\", \"kind\": \"box\", \"at\": [0.15, 0.054], \"width\": 0.2, \"height\": 0.008 },\n" +
            "      { \"id\": \"north-arrow\", \"role\": \"north-arrow\", \"kind\": \"arrow\", \"at\": [0.9, 0.85], \"text\": \"N\", \"size\": 0.1, \"turn\": [\n" +
            "        { \"t\": 0, \"degrees\": 30 },\n" +
            "        { \"t\": 1, \"degrees\": 395.5 },\n" +
            "        { \"t\": 2, \"degrees\": 380 }\n" +
            "      ] }\n" +
            "    ]\n" +
            "  },\n", json);

        // Times that are nothing are not written.
        string plain = ScenePlanSerializer.Write(PlanFixtures.Full() with { Texts = new PlanScreenTexts() });
        Assert.Contains("  \"texts\": {\n    \"items\": []\n  },\n", plain);
        Assert.False(ScenePlanSerializer.Read(plain).HasTexts);
    }

    [Fact]
    public void AMovieWithTexts_IsAsLongAsItsSceneAndTheTimeItsLastFrameIsHeld()
    {
        var plan = PlanFixtures.Full();   // 51 frames at 25 fps, held for 3 seconds

        Assert.True(plan.HasTexts);
        Assert.Equal(51 + 75, plan.OutputFrameCount);
        Assert.Equal(51, (plan with { Texts = null }).OutputFrameCount);
        Assert.Equal(1, (plan with { Output = plan.Output with { Kind = PlanOutputKind.Still } }).OutputFrameCount);
        Assert.True((plan with { Texts = new PlanScreenTexts { FromBlack = 1 } }).HasTexts);
    }

    [Fact]
    public void ALabel_TakesOneLine_WhateverItsTextSays()
    {
        string json = ScenePlanSerializer.Write(PlanFixtures.Full());

        Assert.Contains(
            "      \"items\": [\n" +
            "        { \"id\": \"stn:entrance\", \"role\": \"station-label\", \"text\": \"Intrarea \\\"de sus\\\"\", \"at\": [1.5, 2, 3], \"size\": 1.2 },\n" +
            "        { \"id\": \"note\", \"text\": \"two\\nlines\", \"at\": [-4, 0, 0.25], \"size\": 0.5, \"follows\": \"here\" }\n" +
            "      ]", json);
        Assert.Contains(
            "        { \"id\": \"lead:far.7\", \"role\": \"lead-marker\", \"at\": [40, -8, 2], \"radius\": 0.3 },\n" +
            "        { \"id\": \"here\", \"at\": [0, 0, 0], \"radius\": 1 }\n", json);
        Assert.Contains("\"pulse\": {\n        \"period\": 0.56,\n        \"scale\": 1.5\n      }", json);

        var hostile = PlanFixtures.Full() with
        {
            Labels = new PlanLabels { Texts = new PlanTexts { Items = [new PlanText(PlayerWriterTests.HostileName, PlayerWriterTests.HostileName, default, 1)] } },
        };
        var read = ScenePlanSerializer.Read(ScenePlanSerializer.Write(hostile)).Labels!.Texts!.Items[0];
        Assert.Equal((PlayerWriterTests.HostileName, PlayerWriterTests.HostileName), (read.Id, read.Text));
        Assert.Null(read.Role);
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
        Assert.Contains("\"rotation\": {\n        \"axis\": [0, 0, 1],\n        \"degrees\": 30\n      },", json);
        Assert.Contains("\"scale\": [1, 1, 2]", json);
        Assert.Contains("\"interpolation\": \"bezier\"", json);
        Assert.Contains("      { \"t\": 0, \"eye\": [10, -20, 5.5], \"target\": [0, 0, 0], \"orientation\": [1, 0, 0, 0] },\n", json);
        Assert.Contains("      { \"t\": 1, \"eye\": [20, -10, 6], \"target\": [0, 0, 1], \"lens\": 50, \"orientation\": [0.5, 0.5, -0.5, 0.5], \"clipStart\": 0.5, \"clipEnd\": 800 },\n", json);
        Assert.Contains("\"low\": -40,\n        \"high\": 12.5,\n        \"space\": \"model\"", json);
        Assert.Contains("\"engine\": \"eevee\"", json);
        Assert.Contains("\"device\": \"optix\"", json);
        Assert.Contains("\"kind\": \"heightRamp\"", json);
        Assert.Contains("{ \"at\": 1, \"color\": [1, 0.5, 0] }", json);
        Assert.Contains("\"kind\": \"headlamp\"", json);
        Assert.Contains("\"container\": \"webm\"", json);
    }

    [Fact]
    public void APoseWithoutAnOrientation_HasNone_AndOneWithAnOrientation_KeepsItsEveryDigit()
    {
        var turn = PlanQuaternion.FromView(new CaveVector3(1, 2, -3), new CaveVector3(0, 0, 1));
        var plan = PlanFixtures.Full();
        plan = plan with { Camera = plan.Camera with { Track = [plan.Camera.Track[0] with { Orientation = turn }] } };

        var read = ScenePlanSerializer.Read(ScenePlanSerializer.Write(plan));

        Assert.Equal(turn, read.Camera.Track[0].Orientation);
        var plain = PlanFixtures.Full() with
        {
            Camera = PlanFixtures.Full().Camera with { Track = [.. PlanFixtures.Full().Camera.Track.Select(k => k with { Orientation = null })] },
        };
        string json = ScenePlanSerializer.Write(plain);
        Assert.DoesNotContain("orientation", json);
        Assert.All(ScenePlanSerializer.Read(json).Camera.Track, k => Assert.Null(k.Orientation));
    }

    [Fact]
    public void WhatIsNotSet_IsNotWritten()
    {
        string json = ScenePlanSerializer.Write(new ScenePlan { Models = [new PlanModel { Id = "m", Path = "m.ply" }] });

        foreach (string absent in new[]
                 {
                     "\"name\"", "\"crs\"", "\"fStop\"", "\"world\"", "\"material\"", "\"translation\"", "\"rotation\"", "\"scale\"",
                     "\"render\"", "\"blend\"", "\"labels\"", "null",
                 })
            Assert.DoesNotContain(absent, json);
    }

    [Fact]
    public void AWorldOfAnImage_IsWrittenWithItsFileAndItsTurn_AndReadsBack()
    {
        ScenePlan With(PlanWorld world) => PlanFixtures.Full() with { Look = PlanFixtures.Full().Look with { World = world }, Changes = null };
        var world = new PlanWorld { Kind = PlanWorldKind.Image, Image = "hdri/evening_sky.hdr", Strength = 1.5, Rotation = 90 };

        string json = ScenePlanSerializer.Write(With(world));

        Assert.Contains(
            "    \"world\": {\n      \"kind\": \"image\",\n      \"strength\": 1.5,\n      \"image\": \"hdri/evening_sky.hdr\",\n      \"rotation\": 90\n    }",
            json);
        Assert.Equal(world, ScenePlanSerializer.Read(json).Look.World);
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, json));
        // A world that is not turned does not say so, and the other worlds are written as they were.
        Assert.DoesNotContain("rotation\": 9", ScenePlanSerializer.Write(With(world with { Rotation = null })));
        string sky = ScenePlanSerializer.Write(With(new PlanWorld { Kind = PlanWorldKind.Sky, Strength = 0.5 }));
        Assert.Contains("    \"world\": {\n      \"kind\": \"sky\",\n      \"strength\": 0.5\n    }", sky);
        // The schema has the rule of the validator: an HDR or an EXR file beside the plan.
        Assert.Contains(
            "$.look.world.image: 'hdri/evening_sky.png' does not match",
            Assert.Single(JsonSchemaSubset.Validate(ScenePlanSchema.Text, json.Replace("evening_sky.hdr", "evening_sky.png"))));
        Assert.Contains(
            "$.look.world.image: '/skies/evening_sky.hdr' does not match",
            Assert.Single(JsonSchemaSubset.Validate(ScenePlanSchema.Text, json.Replace("hdri/evening_sky.hdr", "/skies/evening_sky.hdr"))));
    }

    [Fact]
    public void AnOutputThatDoesNotRender_SaysSo_AndReadsBack()
    {
        var plan = PlanFixtures.Full() with { Output = PlanFixtures.Full().Output with { Render = false } };

        string json = ScenePlanSerializer.Write(plan);

        Assert.Contains("\"baseName\": \"clip\",\n    \"render\": false\n  },\n  \"blend\": {\n    \"path\": \"project/scene.blend\"\n  }", json);
        Assert.False(ScenePlanSerializer.Read(json).Output.Render);
        Assert.True(ScenePlanSerializer.Read(ScenePlanSerializer.Write(PlanFixtures.Full())).Output.Render);
        Assert.Equal(plan.Blend, ScenePlanSerializer.Read(json).Blend);
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
            .TrimEnd().TrimEnd('}') + ",\n  \"surveyors\": [{ \"id\": \"a1\", \"text\": \"Sala Mare\" }],\n  \"marker_cube\": { \"points\": [[1, 2, 3]] }\n}\n";

        var plan = ScenePlanSerializer.Read(json);

        Assert.Equal(["marker_cube", "surveyors"], plan.Sections!.Keys.Order());
        string rewritten = ScenePlanSerializer.Write(plan);
        using var doc = JsonDocument.Parse(rewritten);
        Assert.Equal("Sala Mare", doc.RootElement.GetProperty("surveyors")[0].GetProperty("text").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("marker_cube").GetProperty("points")[0][2].GetInt32());
    }

    [Theory]
    [InlineData("\"fps\": 25,", "\"fps\": 25, \"speed\": 2,", "timeline.speed")]
    [InlineData("\"material\": \"rock\"", "\"material\": \"rock\", \"tint\": [1, 0, 0]", "models[0].tint")]
    [InlineData("\"t\": 1,", "\"t\": 1, \"roll\": 5,", "camera.track[1].roll")]
    [InlineData("\"at\": 1, ", "\"at\": 1, \"alpha\": 1, ", "look.materials[0].stops[1].alpha")]
    [InlineData("\"radius\": 1 }", "\"radius\": 1, \"shape\": \"cube\" }", "labels.markers.items[1].shape")]
    [InlineData("\"period\": 0.56", "\"period\": 0.56, \"ease\": true", "labels.markers.pulse.ease")]
    [InlineData("\"baseName\": \"clip\"", "\"baseName\": \"clip\", \"codec\": \"av1\"", "output.codec")]
    [InlineData("\"backOpacity\": 0,", "\"backOpacity\": 0, \"emission\": 2,", "texturedMaterials[0].emission")]
    [InlineData("\"role\": \"terrain\",", "\"role\": \"terrain\", \"visible\": false,", "models[2].visible")]
    [InlineData("\"hold\": 3,", "\"hold\": 3, \"music\": \"song.ogg\",", "texts.music")]
    [InlineData("\"width\": 0.2,", "\"width\": 0.2, \"radius\": 3,", "texts.items[2].radius")]
    [InlineData("\"degrees\": 380 }", "\"degrees\": 380, \"ease\": 1 }", "texts.items[3].turn[2].ease")]
    public void AnUnknownKeyInsideAKnownSection_IsRefused_ByItsPath(string original, string changed, string path)
    {
        string full = ScenePlanSerializer.Write(PlanFixtures.Full());
        Assert.Contains(original, full);

        var ex = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(full.Replace(original, changed)));

        Assert.Equal($"{path} is not known to this build (scene plan version 1); a newer build may be needed.", ex.Message);
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
    [InlineData("{ \"version\": 1, \"camera\": { \"track\": [{ \"t\": 0, \"eye\": [1, 2, 3], \"target\": [0, 0, 0], \"orientation\": [1, 0, 0] }] } }", "Malformed")]
    [InlineData("{ \"version\": 1, \"camera\": { \"track\": [{ \"t\": 0, \"eye\": [1, 2, 3], \"target\": [0, 0, 0], \"orientation\": [1, 0, 0, 0, 0] }] } }", "Malformed")]
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
