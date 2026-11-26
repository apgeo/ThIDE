// `therion-cli blender <model> --storyboard`: a film of several shots from a storyboard file, on
// an invented cave: written as a bundle, one shot of it alone, and what the options refuse.
// Rendering needs a real Blender and runs only with THIDE_BLENDER_SMOKE=1 (THIDE_BLENDER_PATH
// picks the Blender).

using System.IO.Compression;
using Therion.Blender;
using Therion.Blender.Plan;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderStoryboardVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-film-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;
    private readonly string _storyboard;

    /// <summary>Three shots at 8 fps: one second; a second and a half after a dissolve of half a
    /// second; one second after a dip. 8, 12 and 8 frames: 24 of film.</summary>
    private static Storyboard ThreeShots => new()
    {
        Name = "{cave}: three shots",
        Fps = 8,
        Defaults = new StoryboardDefaults { Engine = new EngineSpec { Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly } },
        Texts = new OverlaySpec { Title = "{cave}", Captions = [new CaptionSpec { Text = "Three shots", Start = 0.5, End = 1.5 }] },
        Shots =
        [
            new ShotSpec { Name = "Outside", Seconds = 1, Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.1 } } },
            new ShotSpec { Name = "Down", Seconds = 1.5, Preset = "Helix descent", In = new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 0.5 } },
            new ShotSpec { Name = "Still", Seconds = 1, In = new TransitionSpec { Kind = TransitionKind.Dip, Seconds = 0.5 }, Texts = new OverlaySpec { Corner = "Way out" } },
        ],
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "film" },
    };

    public BlenderStoryboardVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), _lox);
        _storyboard = Write("film.storyboard.json", ThreeShots);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string Write(string name, Storyboard storyboard)
    {
        string path = Path.Combine(_dir, name);
        StoryboardSerializer.WriteFile(storyboard, path);
        return path;
    }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(args, so, se);
        return (code, so.ToString(), se.ToString());
    }

    private static Dictionary<string, string> Texts(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        string top = Path.GetFileNameWithoutExtension(zip) + "/";
        return archive.Entries.Where(e => !e.FullName.EndsWith(".ply", StringComparison.Ordinal)).ToDictionary(e => e.FullName[top.Length..], e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        });
    }

    [Fact]
    public async Task AStoryboard_IsWrittenAsABundle_WithThePlanOfEveryShot_AndOfTheMovie()
    {
        string zip = Path.Combine(_dir, "film-bundle.zip");
        var (code, stdout, stderr) = await Run("blender", _lox, "--storyboard", _storyboard, "--bundle", zip, "--draft", "--readme-language", "ro");

        Assert.True(code == 0, stderr);
        Assert.Equal(Path.GetFullPath(zip), stdout.Trim());
        Assert.Contains("wrote a render bundle with a draft", stderr);
        var files = Texts(zip);
        foreach (string name in new[]
                 {
                     "scene-plan.json", "scene-plan.s01.json", "scene-plan.s02.json", "scene-plan.s03.json", "scene-plan.draft.json",
                     "scene-plan.s02.draft.json", "render.sh", "render.cmd", "README.txt", "thide_player/modules/segments.py", "out/film.srt",
                 })
            Assert.Contains(name, files.Keys);

        Assert.True(ScenePlanSerializer.IsMovie(files["scene-plan.json"]));
        var movie = ScenePlanSerializer.ReadMovie(files["scene-plan.json"]);
        Assert.Equal("Invented Small Cave: three shots", movie.Name);
        Assert.Equal(24, movie.Timeline.FrameCount);
        Assert.Equal([0, 0.5, 2], movie.Segments.Select(s => s.Start));
        Assert.Equal("out", movie.Output.Directory);
        Assert.Contains(movie.Texts!.Items, i => i.Id == "title" && i.Text == "Invented Small Cave");
        Assert.Contains(movie.Texts.Items, i => i.Id == "s03.corner" && i.Text == "Way out");
        // The second shot is the preset's move, a second and a half of it.
        var down = ScenePlanSerializer.Read(files["scene-plan.s02.json"]);
        Assert.Equal(12, down.Timeline.FrameCount);
        Assert.Equal("Down", down.Name);
        Assert.Null(down.Texts);
        Assert.Contains("UN FILM DIN MAI MULTE SECVENȚE", files["README.txt"]);
        Assert.Contains("Three shots", files["out/film.srt"]);
    }

    [Fact]
    public async Task OneShot_IsASceneLikeAnyOther_UnderTheFilmsNameAndItsOwnNumber()
    {
        string folder = Path.Combine(_dir, "shot-bundle");
        var (code, _, stderr) = await Run("blender", _lox, "--storyboard", _storyboard, "--shot", "3", "--bundle", folder, "--title", "Only the last");

        Assert.True(code == 0, stderr);
        string json = File.ReadAllText(Path.Combine(folder, "scene-plan.json"));
        Assert.False(ScenePlanSerializer.IsMovie(json));
        var plan = ScenePlanSerializer.Read(json);
        Assert.Equal("Still", plan.Name);
        Assert.Equal(8, plan.Timeline.FrameCount);
        Assert.Equal("film.s03", plan.Output.BaseName);
        // The shot's own texts, and what the command line adds, as for a preset; none of the film's.
        Assert.Equal(["corner", "title"], plan.Texts!.Items.Select(i => i.Id).Order());
        Assert.Contains(plan.Texts.Items, i => i.Text == "Only the last");
        Assert.False(File.Exists(Path.Combine(folder, "scene-plan.s01.json")));
    }

    [Fact]
    public async Task APhoto_IsNamedRelativeToTheStoryboardsFile_AndTravelsWithTheBundle()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "trip"));
        File.WriteAllBytes(Path.Combine(_dir, "trip", "flag.png"), BlenderBundleVerbTests.OnePixelPng);
        var shots = ThreeShots.Shots.ToList();
        shots[1] = shots[1] with { Annotations = [new AnnotationSpec { Id = "flag", Kind = AnnotationKind.Photo, Station = "main6", Image = "trip/flag.png", Size = 10 }] };
        string storyboard = Write("photo.storyboard.json", ThreeShots with { Shots = shots });
        string folder = Path.Combine(_dir, "photo-bundle");

        var (code, _, stderr) = await Run("blender", _lox, "--storyboard", storyboard, "--bundle", folder);

        Assert.True(code == 0, stderr);
        Assert.True(File.Exists(Path.Combine(folder, "photos", "flag.png")));
        Assert.Contains("photos/flag.png", File.ReadAllText(Path.Combine(folder, "scene-plan.s02.json")));
        Assert.DoesNotContain("photos/flag.png", File.ReadAllText(Path.Combine(folder, "scene-plan.s01.json")));
    }

    [Theory]
    [InlineData("--shot", "2")]                                    // no storyboard
    [InlineData("--storyboard", "{film}", "--preset", "Orbit showcase")]
    [InlineData("--storyboard", "{film}", "--spec", "spec.json")]
    [InlineData("--storyboard", "{film}", "--shot", "4")]          // three shots
    [InlineData("--storyboard", "{film}", "--shot", "first")]
    [InlineData("--storyboard", "{film}", "--shot")]
    [InlineData("--storyboard", "{film}", "--title", "A title")]   // the storyboard says it
    [InlineData("--storyboard", "{film}", "--reveal", "distance")]
    [InlineData("--storyboard", "{film}", "--export", "there")]
    [InlineData("--storyboard", "{film}", "--builder", "generator")]
    [InlineData("--storyboard", "{film}", "--render")]             // without a bundle
    [InlineData("--storyboard", "{film}", "--bundle", "b.zip", "--keep-frames")]
    [InlineData("--storyboard", "{film}", "--bundle", "b.zip", "--render", "--render-plan", "draft")]
    [InlineData("--storyboard", "nowhere.json")]
    public async Task WhatDoesNotGoWithAStoryboard_IsAUsageError(params string[] options)
    {
        var (code, stdout, stderr) = await Run(["blender", _lox, .. options.Select(o => o == "{film}" ? _storyboard : o)]);

        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.StartsWith("error: ", stderr);
        Assert.False(File.Exists("b.zip"));
    }

    [Fact]
    public async Task ABadStoryboard_IsNamedByWhatIsWrongWithIt()
    {
        string unknown = Path.Combine(_dir, "unknown.json");
        File.WriteAllText(unknown, File.ReadAllText(_storyboard).Replace("\"fps\": 8", "\"fps\": 8, \"speed\": 2"));
        var (code, _, stderr) = await Run("blender", _lox, "--storyboard", unknown, "--bundle", Path.Combine(_dir, "b.zip"));
        Assert.Equal(2, code);
        Assert.Contains("bad storyboard", stderr);
        Assert.Contains("speed", stderr);

        var shots = ThreeShots.Shots.ToList();
        shots[1] = shots[1] with { In = new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 5 }, Preset = "No such preset" };
        string invalid = Write("invalid.storyboard.json", ThreeShots with { Shots = shots });
        (code, _, stderr) = await Run("blender", _lox, "--storyboard", invalid, "--bundle", Path.Combine(_dir, "b.zip"));
        Assert.Equal(2, code);
        Assert.Contains("invalid storyboard", stderr);
        Assert.Contains("shots[1].preset: Unknown preset 'No such preset'.", stderr);

        // One shot alone is not held to how the shots are joined.
        shots[1] = shots[1] with { Preset = null };
        invalid = Write("invalid.storyboard.json", ThreeShots with { Shots = shots });
        (code, _, stderr) = await Run("blender", _lox, "--storyboard", invalid, "--bundle", Path.Combine(_dir, "b.zip"));
        Assert.Equal(2, code);
        Assert.Contains("shots[1].in.seconds", stderr);
        (code, _, stderr) = await Run("blender", _lox, "--storyboard", invalid, "--shot", "1", "--bundle", Path.Combine(_dir, "one"));
        Assert.True(code == 0, stderr);
    }

    [BlenderSmokeFact]
    public async Task AStoryboard_IsRendered_AsADraftOfTheWholeFilm_AndFromItsBundle()
    {
        var blender = Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH") is { Length: > 0 } path ? new[] { "--blender", path } : [];
        string outDir = Path.Combine(_dir, "renders");

        // The service's way: every shot's frames in a folder of its own beside the outputs, then the film.
        var (code, stdout, stderr) = await Run(["blender", _lox, "--storyboard", _storyboard, "--draft", "--keep-frames", "--out", outDir, .. blender]);
        Assert.True(code == 0, stderr);
        var outputs = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["film-draft.mp4", "film-draft.srt", "film-draft.vtt"], outputs.Select(Path.GetFileName));
        Assert.All(outputs, o => Assert.True(File.Exists(o), o));
        Assert.Contains("rendered a draft of a film of 3 shot(s), 24 frame(s)", stderr);
        Assert.Contains("Shot 2 of 3 (Down): Rendering frame 12 of 12", stderr);
        string frames = Path.Combine(outDir, "film-draft-frames");
        Assert.Contains($"the frames are in {frames}", stderr);
        Assert.Equal(["s01", "s02", "s03"], Directory.GetDirectories(frames).Select(Path.GetFileName).Order());

        // The same command again renders no frame, and writes the film from those that are there.
        (code, _, stderr) = await Run(["blender", _lox, "--storyboard", _storyboard, "--draft", "--keep-frames", "--out", outDir, .. blender]);
        Assert.True(code == 0, stderr);
        Assert.DoesNotContain("Rendering frame", stderr);
        Assert.Contains("Composing frame 24 of 24", stderr);

        // The bundle's way: one Blender with the movie's plan, as its run scripts start it.
        string zip = Path.Combine(_dir, "film.zip");
        (code, stdout, stderr) = await Run(["blender", _lox, "--storyboard", _storyboard, "--bundle", zip, "--draft", "--render", .. blender]);
        Assert.True(code == 0, stderr);
        outputs = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal([Path.GetFullPath(zip), Path.Combine(_dir, "film-draft.mp4")], outputs);
        Assert.True(new FileInfo(outputs[1]).Length > 0);
        Assert.Contains("rendered scene-plan.draft.json: 24 frame(s)", stderr);
    }
}
