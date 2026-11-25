// What somebody who writes plans without a SceneSpec takes from this library: texts laid out for
// a plan that is there already, the north arrow of a camera that is turned by its poses, an end
// card over the scene, the look this library gives a cave, the modules a plan's sections need,
// and a bundle whose files are not on disk and whose README is theirs in part.

using System.IO.Compression;
using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class PlanProducerTests
{
    /// <summary>A plan made by hand, as another program makes one: one mesh, a camera that flies
    /// north and turns east, four seconds at 10 fps.</summary>
    private static ScenePlan ByHand(params PlanCameraKey[] track) => new()
    {
        Generator = "another program",
        Name = "Flight",
        Timeline = new PlanTimeline { Fps = 10, FrameCount = 41 },
        Models = [new PlanModel { Id = "walls", Path = "walls.ply" }],
        Camera = new PlanCamera
        {
            Lens = 36, ClipStart = 0.1, ClipEnd = 1000,
            Track = track.Length > 0 ? track :
            [
                new PlanCameraKey(0, new CaveVector3(0, 0, 10), new CaveVector3(0, 100, 10)),
                new PlanCameraKey(4, new CaveVector3(0, 40, 10), new CaveVector3(100, 40, 10)),
            ],
        },
        Look = new PlanLook { Width = 640, Height = 360 },
        Output = new PlanOutput { Kind = PlanOutputKind.Video, Directory = "out", BaseName = "flight" },
    };

    private static PlanScreenItem Item(PlanScreenTexts texts, string id) => texts.Items.Single(i => i.Id == id);

    // ---- texts for a plan that is there ----

    [Fact]
    public void Overlays_AreLaidOutForAPlan_AsTheyAreForASpec()
    {
        var overlays = new OverlaySpec
        {
            Title = "Flight", Subtitle = "an invented cave", TitleSeconds = 1.5, Credits = "the team", NorthArrow = true, ScaleBar = true,
            Captions = [new CaptionSpec { Text = "the way in", Start = 1, End = 2.5 }],
            EndCard = new EndCardSpec { Text = "The end", Seconds = 2 },
            FadeFromBlack = 0.5,
        };
        var texts = ScreenTextPlanner.Plan(overlays, ByHand())!;

        Assert.Equal(
            ["scale-bar", "scale-bar-text", "north-arrow", "credits", "title", "subtitle", "caption.1", "end-card", "end-card-text"],
            texts.Items.Select(i => i.Id));
        Assert.Equal((2, 0.5, 0), (texts.Hold, texts.FromBlack, texts.ToBlack));
        // The clip is 41 frames at 10 fps: the card comes at 4.1 seconds.
        Assert.Equal(4.1, Item(texts, "end-card").Start);
        // A camera that stands 100 m from what it looks at: the picture is 100 m wide there, and
        // a quarter of it holds 20 m.
        var standing = ScreenTextPlanner.Plan(
            new OverlaySpec { ScaleBar = true }, ByHand(new PlanCameraKey(0, new CaveVector3(0, 0, 10), new CaveVector3(0, 100, 10))))!;
        Assert.Equal("20 m", Item(standing, "scale-bar-text").Text);
        Assert.Equal(0.2, Item(standing, "scale-bar").Width);
        // North to begin with, east at the end, turning on the way.
        var turns = Item(texts, "north-arrow").Turn!;
        Assert.Equal((new PlanTurn(0, 0), new PlanTurn(4, 90)), (turns[0], turns[^1]));
        Assert.All(turns.Zip(turns.Skip(1)), pair => Assert.True(pair.Second.Degrees > pair.First.Degrees));

        // Into the plan, with the module that draws them at its place.
        var plan = ScenePlanBuilder.WithTexts(ByHand(), texts);
        Assert.Equal(["models", "camera_track", "look", "texts", "output"], plan.Features);
        Assert.Empty(ScenePlanValidator.Validate(plan));
        Assert.Equal(41 + 20, plan.OutputFrameCount);

        Assert.Null(ScreenTextPlanner.Plan(new OverlaySpec(), ByHand()));
        // A still has what is there on its one frame.
        var still = ScreenTextPlanner.Plan(overlays, ByHand() with { Output = new PlanOutput { Kind = PlanOutputKind.Still } })!;
        Assert.Equal((0, 0), (still.Hold, still.FromBlack));
        Assert.DoesNotContain(still.Items, i => i.Role is "caption" or "end-card");
    }

    [Fact]
    public void TheNorthArrow_OfACameraTurnedByItsPoses_FollowsThem_StraightDownToo()
    {
        static PlanQuaternion Looking(double headingDegrees, double pitchDegrees)
        {
            double h = headingDegrees * Math.PI / 180, p = pitchDegrees * Math.PI / 180;
            var forward = new CaveVector3(Math.Sin(h) * Math.Cos(p), Math.Cos(h) * Math.Cos(p), Math.Sin(p));
            // Straight down, the top of the picture is the way ahead.
            var up = Math.Abs(pitchDegrees) > 89.999 ? new CaveVector3(Math.Sin(h), Math.Cos(h), 0) : new CaveVector3(0, 0, 1);
            return PlanQuaternion.FromView(forward, up);
        }
        var here = new CaveVector3(0, 0, 50);
        // A pose a frame, as a flyby is written: straight down throughout, turning from north-east
        // through east to south (45 to 180 degrees), which a target under the camera cannot say.
        var track = Enumerable.Range(0, 41)
            .Select(i => new PlanCameraKey(i / 10.0, here, new CaveVector3(0, 0, 0)) { Orientation = Looking(45 + 135 * i / 40.0, -90) })
            .ToArray();
        var plan = ByHand(track);
        Assert.Empty(ScenePlanValidator.Validate(plan));

        var headings = ScreenTextPlanner.Headings(plan.Camera, 10, 41);
        Assert.Equal(41, headings.Count);
        for (int i = 0; i < 41; i++) Assert.Equal(45 + 135 * i / 40.0, headings[i], 6);
        var arrow = Item(ScreenTextPlanner.Plan(new OverlaySpec { NorthArrow = true }, plan)!, "north-arrow");
        Assert.Equal([new PlanTurn(0, 45), new PlanTurn(4, 180)], arrow.Turn);

        // Level and tilted poses go by the way they look; on past north the angle goes on past 360.
        var round = Enumerable.Range(0, 41)
            .Select(i => new PlanCameraKey(i / 10.0, here, new CaveVector3(0, 0, 0)) { Orientation = Looking(300 + 3 * i, -30) })
            .ToArray();
        headings = ScreenTextPlanner.Headings(ByHand(round).Camera, 10, 41);
        Assert.Equal(-60, headings[0], 6);
        Assert.Equal(60, headings[40], 6);
        Assert.All(Enumerable.Range(1, 40), i => Assert.Equal(3, headings[i] - headings[i - 1], 6));

        // Between two poses a second apart the turn moves as the player moves it: half-way at
        // the frame in the middle.
        var two = ByHand(
            new PlanCameraKey(0, here, new CaveVector3(0, 0, 0)) { Orientation = Looking(0, -20) },
            new PlanCameraKey(1, here, new CaveVector3(0, 0, 0)) { Orientation = Looking(40, -20) });
        headings = ScreenTextPlanner.Headings(two.Camera, 10, 41);
        Assert.Equal(0, headings[0], 6);
        Assert.Equal(20, headings[5], 1);
        Assert.Equal(40, headings[10], 6);
        Assert.Equal(40, headings[40], 6);

        // Without orientations the camera looks at its targets, as a planned camera does.
        headings = ScreenTextPlanner.Headings(ByHand().Camera, 10, 41);
        Assert.Equal(0, headings[0], 6);
        Assert.Equal(90, headings[40], 6);
        // The turns of any headings, one a frame, are there for whoever has them from elsewhere.
        Assert.Equal([new PlanTurn(0, 10), new PlanTurn(0.2, 30), new PlanTurn(0.3, 30.5)], ScreenTextPlanner.Turns([10, 20, 30, 30.5], 10));
    }

    // ---- the end card ----

    [Fact]
    public void AnEndCard_MayLetThePictureThrough_AndComeOverTheEndOfTheScene()
    {
        PlanScreenTexts Texts(EndCardSpec card) => ScreenTextPlanner.Plan(new OverlaySpec { EndCard = card }, ByHand())!;
        var card = new EndCardSpec { Text = "The end", Subtitle = "an invented cave", Seconds = 2 };

        // As it was: a black card after the scene, for which the movie is longer.
        var black = Texts(card);
        Assert.Equal(2, black.Hold);
        Assert.Equal((4.1, null), (Item(black, "end-card").Start, Item(black, "end-card").Opacity));
        Assert.False(Item(black, "end-card-text").Backing);

        // The last frame, dimmed, under the text.
        var dimmed = Texts(card with { Backdrop = 0.6 });
        Assert.Equal((2, 0.6), (dimmed.Hold, Item(dimmed, "end-card").Opacity));
        // The last frame as it is: no card, and the text on a dark box of its own.
        var bare = Texts(card with { Backdrop = 0 });
        Assert.Equal(2, bare.Hold);
        Assert.Equal(["end-card-text", "end-card-subtitle"], bare.Items.Select(i => i.Id));
        Assert.All(bare.Items, i => Assert.True(i.Backing && i.Start == 4.1));

        // Over the last two seconds of the scene, which goes on under it: the movie is no longer.
        var over = Texts(card with { Backdrop = 0, OverScene = true });
        Assert.Equal(0, over.Hold);
        Assert.All(over.Items, i => Assert.Equal((2.1, 0.5), (i.Start, i.FadeIn)));
        Assert.Equal(41, ScenePlanBuilder.WithTexts(ByHand(), over).OutputFrameCount);
        // A card longer than the scene is there from the first frame.
        Assert.All(Texts(card with { Seconds = 30, OverScene = true }).Items, i => Assert.Null(i.Start));

        // A spec says them too, and is the same JSON as before when it does not.
        var spec = new SceneSpec { Labels = new LabelsSpec { Overlays = new OverlaySpec { EndCard = card } } };
        string plain = SceneSpecSerializer.Write(spec);
        Assert.DoesNotContain("backdrop", plain);
        Assert.DoesNotContain("overScene", plain);
        var changed = spec with { Labels = new LabelsSpec { Overlays = new OverlaySpec { EndCard = card with { Backdrop = 0.4, OverScene = true } } } };
        string written = SceneSpecSerializer.Write(changed);
        Assert.Contains("\"backdrop\": 0.4", written);
        Assert.Contains("\"overScene\": true", written);
        Assert.Equal(changed.Labels.Overlays.EndCard, SceneSpecSerializer.Read(written).Labels.Overlays.EndCard);
        Assert.Contains(
            SceneSpecValidator.Validate(PlanFixtures.WithPaths(changed with { Labels = new LabelsSpec { Overlays = new OverlaySpec { EndCard = card with { Backdrop = 1.5 } } } })),
            e => e.Path == "labels.overlays.endCard.backdrop");
        // The script generator draws no end card, with or without these.
        Assert.Contains("labels.overlays.endCard", SceneBuilders.NotDrawnByTheGenerator(changed));
        Assert.Empty(ScenePlanBuilder.NotCarried(changed));
    }

    // ---- the look of a cave, and the modules of a plan ----

    [Fact]
    public void TheRockAndTheLights_OfThisLibrary_AreThereForAPlanMadeByHand()
    {
        var bounds = new BoundingBox(new CaveVector3(-40, -30, -20), new CaveVector3(60, 50, 20));
        var spec = PlanFixtures.WithPaths(new SceneSpec
        {
            Materials = new MaterialsSpec { Rock = RockMaterial.DepthGradient },
            Lighting = new LightingSpec { Rig = LightingRig.SunSky, Strength = 2 },
        });
        var lowered = ScenePlanBuilder.FromSpec(spec, new CameraFraming { LocalBounds = bounds });

        // What a spec is lowered to, without a spec to lower.
        var rock = ScenePlanBuilder.Material(spec.Materials, bounds);
        Assert.Equal(lowered.Look.Materials[0] with { Stops = null }, rock with { Stops = null });
        Assert.Equal(lowered.Look.Materials[0].Stops, rock.Stops);
        Assert.Equal((PlanMaterialKind.HeightRamp, -20.0, 20.0), (rock.Kind, rock.Low, rock.High));
        Assert.Equal("walls", ScenePlanBuilder.Material(new MaterialsSpec(), bounds, "walls").Id);
        Assert.Equal(PlanMaterialKind.NoiseRamp, ScenePlanBuilder.Material(new MaterialsSpec { Rock = RockMaterial.Procedural }, bounds).Kind);

        var (lights, world) = ScenePlanBuilder.Lighting(spec.Lighting);
        Assert.Equal(lowered.Look.Lights, lights);
        Assert.Equal(lowered.Look.World, world);
        Assert.Equal(("Sun", PlanLightKind.Sun, 6.0), (lights[0].Id, lights[0].Kind, lights[0].Energy));
        var (lamp, none) = ScenePlanBuilder.Lighting(new LightingSpec { Rig = LightingRig.Headlamp });
        Assert.Equal(("Headlamp", PlanLightKind.Headlamp), (lamp[0].Id, lamp[0].Kind));
        Assert.Null(none);

        // A lamp, a sun and the rock, in a plan made by hand, with a sun that goes out.
        var plan = ByHand() with
        {
            Models = [new PlanModel { Id = "walls", Path = "walls.ply", Material = rock.Id }],
            Look = new PlanLook { Width = 640, Height = 360, Materials = [rock], Lights = [.. lamp, .. lights], World = world },
            Changes = [new PlanChange { Target = new PlanChangeTarget { Id = "Sun" }, Energy = [new PlanNumberKey(0, 6), new PlanNumberKey(2, 0)] }],
        };
        Assert.Equal(["changes"], ScenePlanValidator.Validate(plan).Select(e => e.Path));
        plan = ScenePlanBuilder.WithNeededFeatures(plan);
        Assert.Equal(["models", "camera_track", "look", "changes", "output"], plan.Features);
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Fact]
    public void TheModulesAPlansSectionsNeed_AreListedAtTheirPlaces_AndNoneIsTakenOff()
    {
        var plan = ByHand() with
        {
            Features = ["models", "camera_track", "marker_cube", "look", "output"],
            Lines = [new PlanLine { Id = "legs", Strips = [new PlanLineStrip([new CaveVector3(0, 0, 0), new CaveVector3(1, 0, 0)])] }],
            Labels = new PlanLabels(),
            Changes = [],
            Texts = new PlanScreenTexts { Hold = 1 },
            Blend = new PlanBlendFile(),
        };

        var listed = ScenePlanBuilder.WithNeededFeatures(plan);

        // An empty list of changes needs no module; a module from elsewhere stays where it is.
        Assert.Equal(["models", "lines", "camera_track", "marker_cube", "look", "labels", "texts", "blend_file", "output"], listed.Features);
        Assert.Same(listed, ScenePlanBuilder.WithNeededFeatures(listed));
        Assert.Equal(listed.Features, ScenePlanBuilder.WithNeededFeatures(listed with { Lines = null, Texts = null }).Features);
    }

    // ---- a bundle ----

    private static Dictionary<string, byte[]> Zipped(RenderBundleContent content, string name = "flight")
    {
        using var stream = new MemoryStream();
        RenderBundleWriter.WriteZip(content, stream, name);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName[(name.Length + 1)..], e =>
        {
            using var entry = e.Open();
            using var copy = new MemoryStream();
            entry.CopyTo(copy);
            return copy.ToArray();
        });
    }

    [Fact]
    public void ABundlesFiles_MayComeFromStreams_WithNoFileOnDisk()
    {
        byte[] mesh = Encoding.ASCII.GetBytes("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n");
        int opened = 0;
        var content = new RenderBundleContent
        {
            Plan = ByHand(),
            Streams = new Dictionary<string, Func<Stream>>
            {
                ["walls.ply"] = () =>
                {
                    opened++;
                    return new MemoryStream(mesh, writable: false);
                },
            },
        };

        var entries = Zipped(content);
        Assert.Equal(mesh, entries["walls.ply"]);
        Assert.Equal(1, opened);
        Assert.Contains("  walls.ply ", Encoding.UTF8.GetString(entries["README.txt"]));
        Assert.Contains("a model's mesh", Encoding.UTF8.GetString(entries["README.txt"]));
        // The same content gives the same bytes again, and is opened once more for it.
        Assert.Equal(entries["walls.ply"], Zipped(content)["walls.ply"]);
        Assert.Equal(2, opened);

        // A model's file is among the files or the streams, once.
        Assert.Contains("walls.ply", Assert.Throws<ArgumentException>(() => Zipped(content with { Streams = new Dictionary<string, Func<Stream>>() })).Message);
        string onDisk = Path.GetTempFileName();
        try
        {
            var twice = content with { Files = new Dictionary<string, string> { ["walls.ply"] = onDisk } };
            Assert.Contains("given twice", Assert.Throws<ArgumentException>(() => Zipped(twice)).Message);
        }
        finally
        {
            File.Delete(onDisk);
        }
        var outside = content with { Streams = new Dictionary<string, Func<Stream>>(content.Streams) { ["../walls.ply"] = content.Streams["walls.ply"] } };
        Assert.Contains("not a path inside the bundle", Assert.Throws<ArgumentException>(() => Zipped(outside)).Message);
        var own = content with { Streams = new Dictionary<string, Func<Stream>>(content.Streams) { ["render.py"] = content.Streams["walls.ply"] } };
        Assert.Contains("a file of the bundle itself", Assert.Throws<ArgumentException>(() => Zipped(own)).Message);
    }

    private static RenderBundleContent Flight => new()
    {
        Plan = ByHand(),
        Streams = new Dictionary<string, Func<Stream>> { ["walls.ply"] = () => new MemoryStream([1, 2, 3]) },
    };

    [Fact]
    public void TheReadme_IsInRomanianWhenAsked_AndInEnglishOtherwise()
    {
        string english = RenderBundleWriter.Readme(Flight);
        Assert.Equal(["en", "ro"], RenderBundleWriter.ReadmeLanguages);
        Assert.Contains("HOW TO RENDER", english);
        Assert.Contains("out/flight.mp4, a video of 41 frames at 10 fps (4.1 s), 640x360 pixels, Cycles.", english);
        Assert.Equal(english, RenderBundleWriter.Readme(Flight with { Language = "en-GB" }));
        // A language the README is not written in gives the English one.
        Assert.Equal(english, RenderBundleWriter.Readme(Flight with { Language = "fr" }));

        var content = Flight with
        {
            Language = "ro",
            Draft = ScenePlanBuilder.Draft(ByHand()),
            Plan = ScenePlanBuilder.WithTexts(ByHand(), new PlanScreenTexts { Hold = 1 }),
            NotCarried = ["lighting.hdriPath", "ceva al meu"],
            Captions = [new CaptionSpec { Text = "intrarea", Start = 0, End = 1 }],
        };
        string romanian = RenderBundleWriter.Readme(content);
        Assert.Equal(romanian, RenderBundleWriter.Readme(content with { Language = "ro-RO" }));
        Assert.StartsWith("Flight\n======\n\nO scenă pentru Blender:", romanian);
        foreach (string heading in new[] { "CUM SE RANDEAZĂ", "DOUĂ ETAPE", "CE SCRIE", "FIȘIERELE", "CE LIPSEȘTE DIN ACEASTĂ SCENĂ" })
            Assert.Contains("\n" + heading + "\n", romanian);
        Assert.Contains("out/flight.mp4, un video de 51 cadre la 10 fps (5.1 s), 640x360 pixeli, Cycles.", romanian);
        Assert.Contains("Previzualizarea: out/flight-draft.mp4, un video de 41 cadre", romanian);
        Assert.Contains("Workbench (vizualizarea solidă a Blender)", romanian);
        Assert.Contains("out/flight.srt și flight.vtt există deja", romanian);
        Assert.Contains("  walls.ply ", romanian);
        Assert.Contains("rețeaua de poligoane a unui model", romanian);
        Assert.Contains("  - iluminarea cu o imagine HDRI (scena are în schimb un fundal simplu, slab luminat) (lighting.hdriPath)", romanian);
        Assert.Contains("  - ceva al meu", romanian);
        // The commands are the same in every language, and no line is English.
        Assert.Contains("  Linux, macOS:   sh render.sh --draft", romanian);
        foreach (string word in new[] { "HOW TO", "The draft", "this file", "pixels", " a video of", "Needs Blender", "start Blender", "Every value" })
            Assert.DoesNotContain(word, romanian);
        Assert.DoesNotContain('\r', romanian);
        // The same sections, line for line.
        string same = RenderBundleWriter.Readme(content with { Language = "en" });
        Assert.Equal(same.Split('\n').Count(l => l.StartsWith("  ", StringComparison.Ordinal)), romanian.Split('\n').Count(l => l.StartsWith("  ", StringComparison.Ordinal)));

        Assert.Equal(romanian, Encoding.UTF8.GetString(Zipped(content)["README.txt"]));
    }

    [Fact]
    public void WhoeverMakesABundle_GivesItsReadmeAnOpeningAndSectionsOfTheirOwn()
    {
        var content = Flight with
        {
            ReadmeIntro = "A flight through an invented cave, written by another program.\r\nRender it with Blender.\n",
            ReadmeSections =
            [
                new RenderBundleReadmeSection("WHERE THIS CAME FROM", "The flyby \"Flight\", exported on a Tuesday.\n\nAsk the team."),
                new RenderBundleReadmeSection("DESPRE", "Zborul printr-o peșteră inventată."),
            ],
            NotCarried = ["the photographs along the way"],
        };

        string readme = RenderBundleWriter.Readme(content);

        Assert.StartsWith(
            "Flight\n======\n\nA flight through an invented cave, written by another program.\nRender it with Blender.\n\nNeeds Blender 4.2 or newer", readme);
        Assert.DoesNotContain("Nothing in\nthis folder needs ThIDE", readme);
        Assert.Contains(
            "repository: https://github.com/apgeo/ThIDE\n\nWHERE THIS CAME FROM\n\nThe flyby \"Flight\", exported on a Tuesday.\n\nAsk the team.\n\n" +
            "DESPRE\n\nZborul printr-o peșteră inventată.\n\nNOT IN THIS SCENE\n", readme);
        Assert.EndsWith("  - the photographs along the way\n", readme);
        Assert.DoesNotContain('\r', readme);
        // Without them the README is the library's own, to the byte.
        Assert.Contains("A scene for Blender: a cave model, a camera move and a look, ready to render. Nothing in\nthis folder needs ThIDE.\n", RenderBundleWriter.Readme(Flight));
    }
}
