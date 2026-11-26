// A film of several shots in a real Blender, on an invented cave: every shot's frames in a folder
// of its own, the film joined from them with a cut, a dissolve and a dip (the pictures at the
// joins are compared with the shots' own frames, as numbers), a film that goes on with the shot
// it stopped in, a film of one shot that has the frames of the scene it was, and the render
// service and its progress. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1;
// THIDE_BLENDER_PATH picks the Blender); each case keeps its job folder under
// %TEMP%/thide-blender-probe/player-<case>/.

using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests.Player;

public class PlayerMovieBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("movie-small", InventedCave.Build(InventedCaveSize.Small)));

    private static readonly ColorRgb Red = new(1, 0, 0), Green = new(0, 1, 0), Blue = new(0, 0, 1), Yellow = new(1, 1, 0);

    private static ShotSpec Shot(string name, double seconds, ColorRgb colour, TransitionSpec? begins = null, double startAngle = 0) => new()
    {
        Name = name, Seconds = seconds, In = begins,
        Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25, StartAngleDegrees = startAngle } },
        Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = colour },
    };

    /// <summary>Four shots at 8 fps, each a cave of one colour: 8 frames; 12 after a cut; 8 that
    /// start four frames early, over a dissolve; 8 after a dip to white of four frames. Frames
    /// 1–8, 9–20, 17–24 and 25–32 of the film.</summary>
    private static Storyboard FourShots(OutputKind kind = OutputKind.FrameSequence) => new()
    {
        Name = "Invented Cave: four shots",
        Fps = 8,
        Shots =
        [
            Shot("Red", 1, Red),
            Shot("Green", 1.5, Green, startAngle: 90),
            Shot("Blue", 1, Blue, new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 0.5 }, startAngle: 180),
            Shot("Yellow", 1, Yellow, new TransitionSpec { Kind = TransitionKind.Dip, Seconds = 0.5, Color = new ColorRgb(1, 1, 1) }, startAngle: 270),
        ],
        Output = new OutputSpec { Kind = kind, Width = 96, Height = 64, BaseName = "film", OutputDirectory = "." },
    };

    /// <summary>The film with the look of a draft at full size, under its own names: a frame
    /// takes a fraction of a second, and what joins the shots is the same.</summary>
    private static StoryboardPlan Quick(StoryboardPlan film) => film with
    {
        Segments = film.Segments.Select(s => ScenePlanBuilder.Draft(s, scale: 1.0) with { Output = s.Output }).ToList(),
    };

    private static (StoryboardPlan Film, PlayerJob Job) WriteJob(string caseName, Storyboard storyboard)
    {
        var assets = Small.Value;
        var film = Quick(StoryboardPlanner.Lower(
            storyboard, new SourceSpec { PlyPath = assets.Conversion.Manifest.ModelPath }, assets.Framing, assets.Conversion.Meta));
        var job = PlayerWriter.WriteJob(film, PlayerRunner.NewCase(caseName), PlayerRunner.MeshOf(assets));
        return (film, job);
    }

    private static async Task<string> Run(PlayerJob job, params string[] arguments)
    {
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, arguments);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return log;
    }

    private static RenderedPicture Film(PlayerJob job, int number) => RenderedPicture.Read(Path.Combine(job.Folder, $"film_{number:0000}.png"));

    private static string StagePath(PlayerJob job, string segment, int number) => Path.Combine(job.Folder, "frames", segment, $"frame_{number:0000}.png");

    private static RenderedPicture Stage(PlayerJob job, string segment, int number) => RenderedPicture.Read(StagePath(job, segment, number));

    /// <summary>The share of a picture's pixels that are not what they should be: more than
    /// <see cref="Tolerance"/> of 255 off in a colour channel. What they should be is
    /// <paramref name="from"/>, with <paramref name="other"/> (or a picture of one
    /// <paramref name="colour"/>) mixed in by <paramref name="share"/>.</summary>
    private static double Off(RenderedPicture picture, RenderedPicture from, RenderedPicture? other = null, double share = 0, byte[]? colour = null)
    {
        Assert.Equal((from.Width, from.Height), (picture.Width, picture.Height));
        int off = 0;
        for (int at = 0; at < picture.Rgba.Length; at += 4)
            for (int channel = 0; channel < 3; channel++)
            {
                double mixedIn = other is not null ? other.Rgba[at + channel] : colour is not null ? colour[channel] : from.Rgba[at + channel];
                if (Math.Abs(picture.Rgba[at + channel] - (from.Rgba[at + channel] * (1 - share) + mixedIn * share)) > Tolerance)
                {
                    off++;
                    break;
                }
            }
        return off / (double)(picture.Rgba.Length / 4);
    }

    private const double Tolerance = 12;

    /// <summary>The most of a picture that may be off and the picture still be the one it should
    /// be, and the least that is off when it is another.</summary>
    private const double Same = 0.003, Another = 0.01;

    // ---- joined ----

    [BlenderFact]
    public async Task AFilm_IsEveryShotsFrames_JoinedWithACut_ADissolveAndADip()
    {
        var (film, job) = WriteJob("movie-joined", FourShots());
        Assert.Equal(32, film.Movie.Timeline.FrameCount);

        // As a bundle's run script starts it: one Blender, with the movie's plan, which takes the
        // player through every segment and then through the second stage.
        string log = await Run(job, "--keep-frames");

        Assert.Equal(["1/4 Red", "2/4 Green", "3/4 Blue", "4/4 Yellow"], PlayerRunner.Reported(log, "segment"));
        Assert.Equal(["8", "12", "8", "8", "32"], PlayerRunner.Reported(log, "frames"));
        Assert.Equal("compose", PlayerRunner.Reported(log, "phase")[^1]);
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        Assert.Equal([Path.Combine(job.Folder, "frames")], PlayerRunner.Reported(log, "frames-kept"));
        foreach (var segment in film.Movie.Segments)
            Assert.Equal(segment.FrameCount, Directory.GetFiles(Path.Combine(job.Folder, "frames", segment.Frames), "frame_*.png").Length);
        Assert.Equal(32, Directory.GetFiles(job.Folder, "film_*.png").Length);

        // The shots are a cave of one colour each: what is compared is not one picture four times.
        Assert.True(Stage(job, "s01", 4).Of(Red).Count > 30, "the first shot's cave is not red");
        Assert.Equal(0, Stage(job, "s02", 4).Of(Red).Count);
        Assert.True(Stage(job, "s02", 4).Of(Green).Count > 30, "the second shot's cave is not green");
        Assert.True(Off(Stage(job, "s02", 10), Stage(job, "s03", 2)) > Another, "the two shots of the dissolve look the same");

        // Inside a shot, and across the cut, the film is the shot's own frames.
        Assert.InRange(Off(Film(job, 5), Stage(job, "s01", 5)), 0, Same);
        Assert.InRange(Off(Film(job, 8), Stage(job, "s01", 8)), 0, Same);
        Assert.InRange(Off(Film(job, 9), Stage(job, "s02", 1)), 0, Same);
        Assert.True(Off(Film(job, 9), Stage(job, "s01", 8)) > Another, "the frame after the cut is still the first shot's");
        Assert.InRange(Off(Film(job, 16), Stage(job, "s02", 8)), 0, Same);

        // The dissolve: frames 17 to 20 are the second shot's 9 to 12 and the third's 1 to 4,
        // mixed evenly, one fifth more of the third on each.
        for (int j = 1; j <= 4; j++)
        {
            double mixed = Off(Film(job, 16 + j), Stage(job, "s02", 8 + j), Stage(job, "s03", j), share: j / 5.0);
            Assert.True(mixed <= Same, $"frame {16 + j}: {mixed:P1} of it is not the mix of the two shots");
            Assert.True(Off(Film(job, 16 + j), Stage(job, "s02", 8 + j)) > Another, $"frame {16 + j} has nothing of the third shot");
            Assert.True(Off(Film(job, 16 + j), Stage(job, "s03", j)) > Another, $"frame {16 + j} has nothing of the second shot");
        }
        Assert.InRange(Off(Film(job, 21), Stage(job, "s03", 5)), 0, Same);

        // The dip: white comes over the third shot's last two frames and goes from the fourth's
        // first two; the frames at the cut are white alone.
        byte[] white = [255, 255, 255];
        Assert.InRange(Off(Film(job, 22), Stage(job, "s03", 6)), 0, Same);
        Assert.InRange(Off(Film(job, 23), Stage(job, "s03", 7), share: 0.5, colour: white), 0, Same);
        Assert.True(Off(Film(job, 23), Stage(job, "s03", 7)) > Another, "frame 23 has nothing of the dip's colour");
        Assert.InRange(Off(Film(job, 24), Stage(job, "s03", 8), share: 1, colour: white), 0, Same);
        Assert.InRange(Off(Film(job, 25), Stage(job, "s04", 1), share: 1, colour: white), 0, Same);
        Assert.InRange(Off(Film(job, 26), Stage(job, "s04", 2), share: 0.5, colour: white), 0, Same);
        Assert.InRange(Off(Film(job, 27), Stage(job, "s04", 3)), 0, Same);
        Assert.InRange(Off(Film(job, 32), Stage(job, "s04", 8)), 0, Same);
    }

    [BlenderFact]
    public async Task ADissolve_IntoTheSecondShot_IsTheSameMix_AndTheFilmIsShorterByIt()
    {
        // The shots lie on two channels of the video editor in turn: here the shot that begins
        // is the upper one, and comes; in the film of four it was the lower one, and the other went.
        var storyboard = FourShots() with
        {
            Shots = [Shot("Red", 1, Red), Shot("Blue", 1, Blue, new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 0.5 }, startAngle: 180)],
        };
        var (film, job) = WriteJob("movie-dissolve", storyboard);
        Assert.Equal(12, film.Movie.Timeline.FrameCount);

        await Run(job, "--keep-frames");

        Assert.Equal(12, Directory.GetFiles(job.Folder, "film_*.png").Length);
        Assert.InRange(Off(Film(job, 4), Stage(job, "s01", 4)), 0, Same);
        for (int j = 1; j <= 4; j++)
        {
            double mixed = Off(Film(job, 4 + j), Stage(job, "s01", 4 + j), Stage(job, "s02", j), share: j / 5.0);
            Assert.True(mixed <= Same, $"frame {4 + j}: {mixed:P1} of it is not the mix of the two shots");
            Assert.True(Off(Film(job, 4 + j), Stage(job, "s01", 4 + j)) > Another, $"frame {4 + j} has nothing of the second shot");
            Assert.True(Off(Film(job, 4 + j), Stage(job, "s02", j)) > Another, $"frame {4 + j} has nothing of the first shot");
        }
        Assert.InRange(Off(Film(job, 9), Stage(job, "s02", 5)), 0, Same);
        Assert.InRange(Off(Film(job, 12), Stage(job, "s02", 8)), 0, Same);
    }

    // ---- stopped, changed, and written again ----

    [BlenderFact]
    public async Task AFilmThatWasStopped_GoesOnInTheShotItStoppedIn_AndAChangedShot_IsRenderedAgainAlone()
    {
        var (film, job) = WriteJob("movie-resumed", FourShots());
        await Run(job, "--keep-frames");
        var written = Directory.GetFiles(Path.Combine(job.Folder, "frames"), "frame_*.png", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);

        // As if it had been stopped in the second shot: two of its frames are not there, and one
        // of them was cut short.
        File.Delete(StagePath(job, "s02", 11));
        File.WriteAllBytes(StagePath(job, "s02", 12), [1, 2, 3]);
        string log = await Run(job, "--keep-frames");

        Assert.Equal(["8/8", "10/12", "8/8", "8/8"], PlayerRunner.Reported(log, "resumed"));
        Assert.Equal(["11/12", "12/12"], PlayerRunner.Reported(log, "frame").Take(2));
        Assert.Equal(2 + 32, PlayerRunner.Reported(log, "frame").Count);
        Assert.All(written.Where(f => !f.Key.Contains("s02" + Path.DirectorySeparatorChar + "frame_001", StringComparison.Ordinal)),
            f => Assert.Equal(f.Value, File.GetLastWriteTimeUtc(f.Key)));

        // Another lens in the third shot: its frames are another scene's, the others' are not.
        string third = Path.Combine(job.Folder, film.Movie.Segments[2].Plan);
        ScenePlanSerializer.WriteFile(film.Segments[2] with { Camera = film.Segments[2].Camera with { Lens = 50 } }, third);

        // The second stage alone refuses them, and says which segment's they are.
        var (exitCode, refused) = await PlayerRunner.RunAsync(job.ScriptPath, "--stage", "compose", "--keep-frames");
        Assert.NotEqual(0, exitCode);
        string error = Assert.Single(PlayerRunner.Reported(refused, "error"));
        Assert.StartsWith("segments[2]: the frames in ", error);
        Assert.Contains("were rendered from another scene", error);

        // The whole film again renders the third shot and no other.
        log = await Run(job, "--keep-frames");
        Assert.Equal(["8/8", "12/12", "8/8"], PlayerRunner.Reported(log, "resumed"));
        Assert.Equal(8 + 32, PlayerRunner.Reported(log, "frame").Count);

        // A text of the film, and how two shots are joined, cost the second stage alone.
        var retitled = film.Movie with
        {
            Texts = new PlanScreenTexts { Items = [new PlanScreenItem { Id = "title", Text = "Invented Cave", At = new PlanPoint(0.5, 0.9), Size = 0.1 }] },
        };
        ScenePlanSerializer.WriteFile(retitled, job.PlanPath);
        var plain = Film(job, 5);
        log = await Run(job, "--stage", "compose", "--keep-frames");
        Assert.Equal(["compose"], PlayerRunner.Reported(log, "phase"));
        Assert.True(Off(Film(job, 5), plain) > Another, "the title is not over the film");
        Assert.InRange(Off(Film(job, 5).Part(0, 1, 0.3, 1), plain.Part(0, 1, 0.3, 1)), 0, Same);

        // Without --keep-frames the frames go when the film is written, folder and all.
        await Run(job, "--stage", "compose");
        Assert.False(Directory.Exists(Path.Combine(job.Folder, "frames")));
    }

    // ---- one shot is the scene it was ----

    [BlenderFact]
    public async Task AFilmOfOneShot_HasTheFramesOfTheSceneOfTheSameSpec()
    {
        var assets = Small.Value;
        var spec = PlanFixtures.Preset("Helix descent") with
        {
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented Cave" } },
            Animation = new AnimationSpec { Fps = 8, DurationSeconds = 0.5 },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 96, Height = 64, BaseName = "film" },
        };
        // The scene as it is rendered today, with its real look: its first stage.
        var (single, scene) = PlayerRunner.WriteJob("movie-one-scene", assets, spec with { Engine = spec.Engine with { Samples = 2, Denoise = false, Gpu = GpuMode.CpuOnly } });
        await Run(scene, "--stage", "frames");

        var storyboard = new Storyboard
        {
            Fps = 8,
            Defaults = new StoryboardDefaults
            {
                Engine = spec.Engine with { Samples = 2, Denoise = false, Gpu = GpuMode.CpuOnly }, Materials = spec.Materials,
                Lighting = spec.Lighting, Labels = spec.Labels,
            },
            Shots = [new ShotSpec { Seconds = 0.5, Camera = spec.Camera }],
            Output = spec.Output with { OutputDirectory = "." },
        };
        var film = StoryboardPlanner.Lower(
            storyboard, new SourceSpec { PlyPath = assets.Conversion.Manifest.ModelPath }, assets.Framing, assets.Conversion.Meta);
        var job = PlayerWriter.WriteJob(film, PlayerRunner.NewCase("movie-one-film"), PlayerRunner.MeshOf(assets));
        string log = await Run(job, "--keep-frames");

        Assert.Equal(4, single.Timeline.FrameCount);
        Assert.Equal(["1/1 s01"], PlayerRunner.Reported(log, "segment"));
        // The same frames, picture for picture, and the same note of what they were rendered from.
        for (int number = 1; number <= 4; number++)
        {
            var before = RenderedPicture.Read(Path.Combine(scene.Folder, "frames", $"frame_{number:0000}.png"));
            var now = Stage(job, "s01", number);
            Assert.Equal(before.Rgba, now.Rgba);
        }
        string Note(string folder) => System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "frames.json"))).RootElement.GetProperty("scene").GetString()!;
        Assert.Equal(Note(Path.Combine(scene.Folder, "frames")), Note(Path.Combine(job.Folder, "frames", "s01")));
        // And the film has the scene's title over them.
        Assert.True(Off(Film(job, 2).Part(0.2, 0.8, 0, 0.2), Stage(job, "s01", 2).Part(0.2, 0.8, 0, 0.2)) > Another, "the title is not over the film");
        Assert.InRange(Off(Film(job, 2).Part(0, 1, 0.3, 1), Stage(job, "s01", 2).Part(0, 1, 0.3, 1)), 0, Same);
    }

    // ---- the counter of a reveal ----

    private sealed record ShownText(string Name, string Text, double Opacity);

    private sealed record StripFrame(int Frame, List<ShownText> Texts);

    private sealed record StripsReport(int FrameEnd, List<StripFrame> Frames);

    [BlenderFact]
    public async Task TheCounterOfAFilm_CountsTheRevealOfItsShots_InTheFilmsTime()
    {
        // The cave appears over the first six frames of the first shot; the second holds it.
        var storyboard = new Storyboard
        {
            Fps = 8,
            Shots =
            [
                Shot("Appearing", 1, Red) with { Reveal = ShotReveal.Of(new RevealSpec { By = RevealBy.Distance, Start = 0, End = 0.75, CounterFormat = "{} m" }) },
                Shot("There", 1, Red, startAngle: 90) with { Reveal = ShotReveal.Hold },
            ],
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 96, Height = 64, BaseName = "film", OutputDirectory = "." },
        };
        var assets = RevealFixtures.Prepare(
            "movie-reveal", InventedCave.Build(InventedCaveSize.Small), StoryboardPlanner.ConversionSpec(storyboard), asLox: true);
        var film = Quick(StoryboardPlanner.Lower(
            storyboard, new SourceSpec { PlyPath = assets.Conversion.Manifest.ModelPath }, assets.Framing, assets.Conversion.Meta));
        var job = PlayerWriter.WriteJob(film, PlayerRunner.NewCase("movie-reveal"), PlayerRunner.MeshOf(assets));
        var counters = film.Movie.Texts!.Items.Where(i => i.Counter is not null).ToList();
        Assert.Equal(["s01.reveal-counter", "s02.reveal-counter"], counters.Select(c => c.Id));

        // The first stage alone: the frames of both shots, and no film yet.
        string log = await Run(job, "--stage", "frames");
        Assert.Equal(["8", "8"], PlayerRunner.Reported(log, "frames"));
        Assert.DoesNotContain("compose", PlayerRunner.Reported(log, "phase"));
        Assert.Empty(Directory.GetFiles(job.Folder, "film_*.png"));

        // The second stage: the strips of the video editor.
        string path = Path.Combine(job.Folder, "strips.json");
        var (exitCode, composed) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path,
            ["--python", job.ScriptPath, "--python", PlayerRunner.RevealStripsScript, "--", "--stage", "compose", "--keep-frames", "--strips", path,
             "--strip-frames", string.Join(",", Enumerable.Range(1, 16))],
            job.Folder, 600);
        await File.WriteAllTextAsync(Path.Combine(job.Folder, "strips.log"), composed);
        Assert.True(exitCode == 0 && File.Exists(path),
            $"No strips were reported (exit code {exitCode}): {PlayerRunner.Reported(composed, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(composed)}\n  logs: {job.Folder}");
        var strips = System.Text.Json.JsonSerializer.Deserialize<StripsReport>(
            await File.ReadAllTextAsync(path), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(16, strips.FrameEnd);

        // What the first shot's own plan keys its threshold with, frame by frame, is what the
        // film's counter says over that shot; over the second, what the first ended on.
        var keys = film.Segments[0].Changes!.Single(c => c.Reveal is not null).Reveal!;
        var (min, max) = (counters[0].Counter!.Min!.Value, counters[0].Counter!.Max!.Value);
        Assert.Equal((1, 7), (ScenePlanValidator.FrameAt(keys[0].T, 8), ScenePlanValidator.FrameAt(keys[1].T, 8)));
        var said = new List<string>();
        foreach (int frame in Enumerable.Range(1, 16))
        {
            double threshold = keys[0].Value + (keys[1].Value - keys[0].Value) * (Math.Min(frame, 7) - 1) / 6.0;
            string expected = Math.Clamp(Math.Floor(threshold + 1e-6), min, max).ToString("0") + " m";
            string counter = frame <= 8 ? "s01.reveal-counter." : "s02.reveal-counter.";
            var shown = Assert.Single(strips.Frames.Single(s => s.Frame == frame).Texts, t => t.Name.Contains("reveal-counter", StringComparison.Ordinal));
            Assert.StartsWith(counter, shown.Name);
            Assert.True(shown.Text == expected, $"frame {frame}: the counter says {shown.Text} when the threshold is {threshold:F3}");
            said.Add(shown.Text);
        }
        Assert.True(said.Take(7).Distinct().Count() >= 4, "the counter hardly counts: " + string.Join(", ", said));
        Assert.Single(said.Skip(6).Distinct());
    }

    // ---- the render service ----

    [BlenderFact]
    public async Task TheService_RendersEveryShot_ThenTheFilm_AndGoesOnWithWhatIsThere()
    {
        string root = PlayerRunner.NewCase("movie-service");
        string lox = Path.Combine(root, "small.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        var source = new RenderSource(new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
        var service = new BlenderRenderService(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), Path.Combine(root, "jobs"),
            blenderOverride: () => Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH"));
        string outDir = Path.Combine(root, "out"), frames = Path.Combine(root, "frames");
        var storyboard = FourShots(OutputKind.Video) with
        {
            Texts = new OverlaySpec
            {
                Title = "{cave}", EndCard = new EndCardSpec { Text = "The end", Seconds = 1 },
                Captions = [new CaptionSpec { Text = "Four colours", Start = 1, End = 2 }],
            },
        };
        storyboard = storyboard with { Output = storyboard.Output with { OutputDirectory = outDir } };
        var ticks = new List<RenderProgress>();

        // A draft of the whole film: every shot in Blender's solid view, at half the size.
        var result = await service.RenderStoryboardAsync(
            storyboard, source, new RenderOptions { Draft = true, FramesDirectory = frames, KeepFrames = true }, new SyncProgress<RenderProgress>(ticks.Add));

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  log: {result.JobLogPath}");
        Assert.Equal(["film-draft.mp4", "film-draft.srt", "film-draft.vtt"], result.OutputPaths.Select(Path.GetFileName));
        Assert.Equal(40, result.FrameCount);
        Assert.Equal(SceneBuilder.Player, result.Builder);
        Assert.Equal(frames, result.FramesDirectory);
        Assert.Equal(["s01", "s02", "s03", "s04"], Directory.GetDirectories(frames).Select(Path.GetFileName).Order());
        Assert.Equal(12, Directory.GetFiles(Path.Combine(frames, "s02"), "frame_*.png").Length);
        Assert.Contains(ticks, t => t.Message.StartsWith("Shot 2 of 4 (Green): Rendering frame 12 of 12", StringComparison.Ordinal) && t.Fraction == 20 / 36.0);
        Assert.Contains(ticks, t => t.Phase == RenderPhase.Composing && t.Frame == 40 && t.FrameCount == 40);
        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths[0]);
        Assert.Equal([48, 32], movie.Size);
        Assert.Equal(40, movie.Frames);
        Assert.Contains("Four colours", await File.ReadAllTextAsync(result.OutputPaths[1]));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "jobs")));

        // The same film again: nothing is rendered, the film is written from what is there.
        var rendered = Directory.GetFiles(frames, "frame_*.png", SearchOption.AllDirectories).ToDictionary(f => f, File.GetLastWriteTimeUtc);
        ticks.Clear();
        var again = await service.RenderStoryboardAsync(
            storyboard with { Texts = storyboard.Texts! with { Title = "Another name" } }, source,
            new RenderOptions { Draft = true, FramesDirectory = frames, KeepFrames = true }, new SyncProgress<RenderProgress>(ticks.Add));
        Assert.True(again.Succeeded, $"{again.FailureKind}: {again.ErrorMessage}\n  log: {again.JobLogPath}");
        Assert.DoesNotContain(ticks, t => t.Phase == RenderPhase.Rendering && t.Frame is not null);
        Assert.All(rendered, f => Assert.Equal(f.Value, File.GetLastWriteTimeUtc(f.Key)));

        // From the frames alone, with one shot changed: the second stage says whose frames are not these.
        var shots = storyboard.Shots.ToList();
        shots[1] = shots[1] with { Camera = shots[1].Camera! with { FocalLength = 50 } };
        var moved = await service.RenderStoryboardAsync(
            storyboard with { Shots = shots }, source, new RenderOptions { Draft = true, FramesDirectory = frames, ComposeOnly = true });
        Assert.Equal((false, RenderFailureKind.ScriptError), (moved.Succeeded, moved.FailureKind));
        Assert.Contains("segments[1]: the frames in ", moved.ErrorMessage);

        // A storyboard is the player's.
        var refused = await service.RenderStoryboardAsync(storyboard, source, new RenderOptions { Builder = SceneBuilder.Generator });
        Assert.Equal(RenderFailureKind.BuilderRefused, refused.FailureKind);
    }
}
