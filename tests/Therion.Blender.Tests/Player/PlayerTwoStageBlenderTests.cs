// A render in two stages, in a real Blender, on invented caves: the scene as frames, then the
// frames with the plan's texts over them. The frames are kept and continued when they belong to
// the scene, the texts are where the plan puts them on every frame, and changing a text changes
// nothing else. Opt-in like the other Blender tests (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH
// picks the Blender). Pictures are compared as numbers; each case keeps its job folder under
// %TEMP%/thide-blender-probe/player-<case>/.

using System.Diagnostics;
using Therion.Blender.Execution;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;

namespace Therion.Blender.Tests.Player;

public class PlayerTwoStageBlenderTests
{
    private static readonly Lazy<ProbeAssets> Small = new(() =>
        SceneProbeRunner.Prepare("stages-small", InventedCave.Build(InventedCaveSize.Small)));

    private static readonly Lazy<ProbeAssets> Medium = new(() =>
        SceneProbeRunner.Prepare("stages-medium", InventedCave.Build(InventedCaveSize.Medium)));

    /// <summary>The title's part of the picture, with room for its outline and shadow.</summary>
    private static readonly Region TitleRegion = new(0.1, 0.8, 0.9, 1.0);

    private static SceneSpec Orbit(OverlaySpec overlays, int frames = 8, OutputKind kind = OutputKind.FrameSequence, int width = 320, int height = 180)
        => PlanFixtures.Preset("Orbit showcase") with
        {
            Labels = new LabelsSpec { Overlays = overlays },
            Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
            Animation = new AnimationSpec { Fps = 8, DurationSeconds = frames / 8.0 },
            Output = new OutputSpec { Kind = kind, Width = width, Height = height, BaseName = "clip" },
        };

    /// <summary>The plan with the look of a draft at full size: a frame takes a fraction of a
    /// second, and what is written over it is the same.</summary>
    private static ScenePlan Quick(ScenePlan plan) => ScenePlanBuilder.Draft(plan, scale: 1.0) with { Output = plan.Output };

    private static string Frame(PlayerJob job, int number) => Path.Combine(job.Folder, $"clip_{number:0000}.png");

    private static string StageFrame(PlayerJob job, int number) => Path.Combine(job.Folder, "frames", $"frame_{number:0000}.png");

    private static async Task<string> Run(PlayerJob job, params string[] arguments)
    {
        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, arguments);
        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {job.Folder}");
        return log;
    }

    // ---- two stages ----

    [BlenderFact]
    public async Task AMovieWithTexts_IsRenderedInTwoStages_AndIsAsLongAsItsSceneAndItsEndCard()
    {
        var spec = Orbit(
            new OverlaySpec { Title = "Invented Cave", EndCard = new EndCardSpec { Text = "The end", Seconds = 1 } },
            kind: OutputKind.Video, width: 96, height: 64);
        var (plan, job) = PlayerRunner.WriteJob("stages-movie", Small.Value, spec, Quick);
        Assert.Equal(16, plan.OutputFrameCount);

        // As the application runs it: one Blender, which starts the second stage itself.
        var ticks = new List<RenderProgress>();
        var result = await new BlenderRunner(new RealBlenderProcessLauncher()).RunAsync(
            SceneProbeRunner.Blender,
            new RenderJob(job.ScriptPath, job.Folder, plan.OutputFrameCount, spec.Output with { OutputDirectory = job.Folder }),
            new SyncProgress<RenderProgress>(ticks.Add));

        Assert.True(result.Succeeded, $"{result.FailureKind}: {result.ErrorMessage}\n  logs: {job.Folder}");
        Assert.Equal([Path.Combine(job.Folder, "clip.mp4")], result.OutputPaths);
        Assert.Equal(16, result.FrameCount);
        Assert.Empty(result.Warnings);
        string log = await File.ReadAllTextAsync(result.JobLogPath!);
        Assert.Equal(["scene", "import", "engine", "render", "compose"], PlayerRunner.Reported(log, "phase"));
        Assert.Equal(["8", "16"], PlayerRunner.Reported(log, "frames"));
        Assert.Equal(["1"], PlayerRunner.Reported(log, "done"));
        Assert.Contains(ticks, t => t.Phase == RenderPhase.Composing && t.Frame == 16);
        Assert.False(Directory.Exists(Path.Combine(job.Folder, "frames")), "the frames are removed when the movie is written");

        var movie = await SceneProbeRunner.ReadMovieAsync(result.OutputPaths[0]);
        Assert.Equal([96, 64], movie.Size);
        Assert.Equal(16, movie.Frames);
        Assert.Equal(8.0, movie.Fps, 3);
    }

    [BlenderFact]
    public async Task APlanWithoutTexts_TakesOneStage_UnlessTheFramesAreAskedFor()
    {
        var spec = Orbit(new OverlaySpec(), frames: 4, kind: OutputKind.Video, width: 96, height: 64);
        var (_, job) = PlayerRunner.WriteJob("stages-none", Small.Value, spec, Quick);

        string log = await Run(job);
        Assert.DoesNotContain("compose", PlayerRunner.Reported(log, "phase"));
        Assert.False(Directory.Exists(Path.Combine(job.Folder, "frames")));
        Assert.True(File.Exists(Path.Combine(job.Folder, "clip.mp4")));

        File.Delete(Path.Combine(job.Folder, "clip.mp4"));
        log = await Run(job, "--keep-frames");
        Assert.Contains("compose", PlayerRunner.Reported(log, "phase"));
        Assert.Equal([Path.Combine(job.Folder, "frames")], PlayerRunner.Reported(log, "frames-kept"));
        Assert.Equal(4, Directory.GetFiles(Path.Combine(job.Folder, "frames"), "frame_*.png").Length);
        var movie = await SceneProbeRunner.ReadMovieAsync(Path.Combine(job.Folder, "clip.mp4"));
        Assert.Equal(4, movie.Frames);
    }

    [BlenderFact]
    public async Task TheSecondStage_LeavesThePictureAsTheFirstRenderedIt_OutsideTheTexts()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" }, frames: 3);
        // The real look for once: the frames must come through the second stage's colours unchanged.
        var (_, job) = PlayerRunner.WriteJob("stages-colours", Small.Value, spec with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 4, Denoise = false, Gpu = GpuMode.CpuOnly },
        });

        await Run(job, "--keep-frames");

        for (int frame = 1; frame <= 3; frame++)
        {
            var rendered = Picture.Read(StageFrame(job, frame));
            var written = Picture.Read(Frame(job, frame));
            Assert.Equal((320, 180), (written.Width, written.Height));
            Assert.Equal(0, written.Differing(rendered, TitleRegion, inside: false, tolerance: 1));
            Assert.True(written.Differing(rendered, TitleRegion, inside: true) > 150, $"frame {frame} has no title");
        }
    }

    [BlenderFact]
    public async Task AStillWithTexts_IsItsOneFrameWithTheTextsOverIt()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave", Credits = "the invented team" }, kind: OutputKind.Still);
        var (plan, job) = PlayerRunner.WriteJob("stages-still", Small.Value, spec, Quick);
        Assert.Equal(1, plan.OutputFrameCount);

        string log = await Run(job, "--keep-frames");

        Assert.Equal(["1", "1"], PlayerRunner.Reported(log, "frames"));
        Assert.Single(Directory.GetFiles(Path.Combine(job.Folder, "frames"), "frame_*.png"));
        var still = Picture.Read(Path.Combine(job.Folder, "clip.png"));
        var frame = Picture.Read(StageFrame(job, 1));
        Assert.True(still.Differing(frame, TitleRegion, inside: true) > 150, "the still has no title");
        Assert.True(still.Differing(frame, new Region(0.5, 0.0, 1.0, 0.12), inside: true) > 40, "the still has no credits");
    }

    // ---- the frames are kept, and continued ----

    [BlenderFact]
    public async Task AFirstStageThatWasStopped_GoesOnWhereItStopped()
    {
        const int Frames = 40;
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" }, Frames) with
        {
            // Slow enough to be stopped half way: a real render of the medium cave.
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 24, Denoise = false, Gpu = GpuMode.CpuOnly },
        };
        var (_, job) = PlayerRunner.WriteJob("stages-stopped", Medium.Value, spec);
        string frames = Path.Combine(job.Folder, "frames");

        using (var blender = StartBlender(job, "--stage", "frames"))
        {
            var clock = Stopwatch.StartNew();
            while (!blender.HasExited && clock.Elapsed < TimeSpan.FromMinutes(5)
                   && (!Directory.Exists(frames) || Directory.GetFiles(frames, "frame_*.png").Length < 4))
                await Task.Delay(20);
            Assert.False(blender.HasExited, "the render ended before it could be stopped; give it more frames");
            blender.Kill(entireProcessTree: true);
            await blender.WaitForExitAsync();
        }
        int left = Directory.GetFiles(frames, "frame_*.png").Length;
        Assert.InRange(left, 4, Frames - 1);
        Assert.True(File.Exists(Path.Combine(frames, "frames.json")), "the frames have no note of their scene");

        string log = await Run(job, "--stage", "frames");

        // The frames that were whole are kept; the one that was being written is rendered again.
        string resumed = Assert.Single(PlayerRunner.Reported(log, "resumed"));
        Assert.EndsWith("/" + Frames, resumed);
        int kept = int.Parse(resumed.Split('/')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(kept, left - 1, left);
        Assert.Equal(Frames - kept, PlayerRunner.Reported(log, "frame").Count);
        Assert.Equal(Enumerable.Range(1, Frames).Select(n => $"frame_{n:0000}.png"), Directory.GetFiles(frames, "frame_*.png").Select(Path.GetFileName).Order());
        Assert.All(Directory.GetFiles(frames, "frame_*.png"), f => Assert.Equal((320, 180), Size(f)));

        // And the second stage makes the whole sequence of them.
        log = await Run(job, "--stage", "compose");
        Assert.Equal(["1"], PlayerRunner.Reported(log, "done"));
        Assert.Equal(Frames, Directory.GetFiles(job.Folder, "clip_*.png").Length);
        Assert.False(Directory.Exists(frames));
    }

    [BlenderFact]
    public async Task AFrameThatIsEmptyOrCutShort_IsRenderedAgain_AndTheOthersAreNot()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" });
        var (_, job) = PlayerRunner.WriteJob("stages-unfinished", Small.Value, spec, Quick);
        await Run(job, "--stage", "frames");
        var written = Enumerable.Range(1, 8).ToDictionary(n => n, n => File.GetLastWriteTimeUtc(StageFrame(job, n)));

        // What a render that is stopped leaves: the frame it was at, empty or half written.
        File.WriteAllBytes(StageFrame(job, 3), []);
        byte[] whole = File.ReadAllBytes(StageFrame(job, 5));
        File.WriteAllBytes(StageFrame(job, 5), whole[..(whole.Length / 2)]);
        File.Delete(StageFrame(job, 7));

        string log = await Run(job, "--stage", "frames");

        Assert.Equal(["5/8"], PlayerRunner.Reported(log, "resumed"));
        Assert.Equal(["3/8", "5/8", "7/8"], PlayerRunner.Reported(log, "frame"));
        Assert.Equal(whole.Length, new FileInfo(StageFrame(job, 5)).Length);
        Assert.All(new[] { 1, 2, 4, 6, 8 }, n => Assert.Equal(written[n], File.GetLastWriteTimeUtc(StageFrame(job, n))));
    }

    [BlenderFact]
    public async Task AChangedText_KeepsTheFrames_AndAChangedSceneDoesNot()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" });
        var (plan, job) = PlayerRunner.WriteJob("stages-changed", Small.Value, spec, Quick);
        await Run(job, "--stage", "frames");

        // Another title, a caption, another file name: nothing of the scene.
        var retitled = plan with
        {
            Texts = PlayerRunner.PlanFor(Small.Value, Orbit(new OverlaySpec
            {
                Title = "Another Name", Captions = [new CaptionSpec { Text = "a caption", Start = 0, End = 0.5 }],
            })).Texts,
            Output = plan.Output with { BaseName = "other" },
            Name = "renamed",
        };
        PlayerWriter.WriteJob(retitled, job.Folder);
        string log = await Run(job, "--stage", "frames");
        Assert.Equal(["8/8"], PlayerRunner.Reported(log, "resumed"));
        Assert.Empty(PlayerRunner.Reported(log, "frame"));

        // A plan that had no texts and gets its first keeps its frames too.
        PlayerWriter.WriteJob(plan with { Texts = null, Features = plan.Features.Where(f => f != ScenePlan.TextsFeature).ToList() }, job.Folder);
        log = await Run(job, "--stage", "frames");
        Assert.Equal(["8/8"], PlayerRunner.Reported(log, "resumed"));

        // Another lens is another scene: the second stage refuses the frames, the first renders them all again.
        PlayerWriter.WriteJob(plan with { Camera = plan.Camera with { Lens = plan.Camera.Lens + 10 } }, job.Folder);
        var (exitCode, refused) = await PlayerRunner.RunAsync(job.ScriptPath, "--stage", "compose");
        Assert.Equal(64, exitCode);
        Assert.Contains("were rendered from another scene", Assert.Single(PlayerRunner.Reported(refused, "error")));
        Assert.Equal(8, Directory.GetFiles(Path.Combine(job.Folder, "frames"), "frame_*.png").Length);
        log = await Run(job, "--stage", "frames");
        Assert.Empty(PlayerRunner.Reported(log, "resumed"));
        Assert.Equal(8, PlayerRunner.Reported(log, "frame").Count);
    }

    [BlenderFact]
    public async Task TheSecondStage_RefusesToStartWithoutItsFrames_AndSaysWhichAreMissing()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" });
        var (_, job) = PlayerRunner.WriteJob("stages-missing", Small.Value, spec, Quick);

        var (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--stage", "compose");
        Assert.Equal(64, exitCode);
        string error = Assert.Single(PlayerRunner.Reported(log, "error"));
        Assert.Contains("there are no frames in " + Path.Combine(job.Folder, "frames"), error);
        Assert.Contains("without --stage compose", error);

        await Run(job, "--stage", "frames");
        File.Delete(StageFrame(job, 6));
        File.Delete(StageFrame(job, 8));
        (exitCode, log) = await PlayerRunner.RunAsync(job.ScriptPath, "--stage", "compose");
        Assert.Equal(64, exitCode);
        Assert.Contains("2 of 8 frames are not in", Assert.Single(PlayerRunner.Reported(log, "error")));
        Assert.Contains("frame_0006.png", log);
        Assert.Empty(Directory.GetFiles(job.Folder, "clip*"));
    }

    [BlenderFact]
    public async Task TheFramesFolder_CanBeNamed_AndFramesFromElsewhereAreTakenAsTheyAre()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" }, frames: 3);
        var (_, job) = PlayerRunner.WriteJob("stages-folder", Small.Value, spec, Quick);
        string elsewhere = Path.Combine(job.Folder, "kept here");

        await Run(job, "--stage", "frames", "--frames", elsewhere);
        Assert.Equal(3, Directory.GetFiles(elsewhere, "frame_*.png").Length);
        Assert.False(Directory.Exists(Path.Combine(job.Folder, "frames")));

        // Frames without their note, as another renderer would leave them, are not questioned.
        File.Delete(Path.Combine(elsewhere, "frames.json"));
        string log = await Run(job, "--stage", "compose", "--frames", elsewhere, "--keep-frames");
        Assert.Equal([elsewhere], PlayerRunner.Reported(log, "frames-kept"));
        Assert.Equal(3, Directory.GetFiles(job.Folder, "clip_*.png").Length);
        Assert.Equal(3, Directory.GetFiles(elsewhere, "frame_*.png").Length);
    }

    // ---- the texts ----

    [BlenderFact]
    public async Task ANewTitle_ChangesTheTitleAndNothingElse_AndNoFrameIsRenderedAgain()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave", ScaleBar = true });
        var (plan, job) = PlayerRunner.WriteJob("stages-retitle", Small.Value, spec, Quick);
        await Run(job, "--keep-frames");
        var before = Enumerable.Range(1, 8).Select(n => Picture.Read(Frame(job, n))).ToList();
        var rendered = Enumerable.Range(1, 8).ToDictionary(n => n, n => File.GetLastWriteTimeUtc(StageFrame(job, n)));

        var texts = PlayerRunner.PlanFor(Small.Value, Orbit(new OverlaySpec { Title = "A Longer, Other Name", ScaleBar = true })).Texts;
        PlayerWriter.WriteJob(plan with { Texts = texts }, job.Folder);
        string log = await Run(job, "--stage", "compose", "--keep-frames");

        Assert.Equal(["compose"], PlayerRunner.Reported(log, "phase"));
        Assert.All(Enumerable.Range(1, 8), n =>
        {
            var after = Picture.Read(Frame(job, n));
            Assert.Equal(0, after.Differing(before[n - 1], TitleRegion, inside: false));
            Assert.True(after.Differing(before[n - 1], TitleRegion, inside: true) > 100, $"frame {n}: the title did not change");
            Assert.Equal(rendered[n], File.GetLastWriteTimeUtc(StageFrame(job, n)));
        });
    }

    [BlenderFact]
    public async Task TheNorthArrow_TurnsWithTheCamera_ItsLetterAtItsTip()
    {
        const int Frames = 9;
        var spec = Orbit(new OverlaySpec { NorthArrow = true }, Frames);
        var (plan, job) = PlayerRunner.WriteJob("stages-north", Small.Value, spec, Quick);
        var arrow = plan.Texts!.Items.Single(i => i.Role == ScreenTextPlanner.NorthArrowRole);
        var headings = ScreenTextPlanner.Headings(Emit.CameraPlanner.PlanPoses(
            PlanFixtures.WithPaths(spec), Small.Value.Framing), Frames);
        Assert.Equal(90, Math.Abs(headings[^1] - headings[0]), 0);

        await Run(job, "--keep-frames");

        // Around the arrow, the bright pixels are the arrow and its letter. The letter is beyond
        // the tip, so their middle lies from the arrow's own middle towards where it points.
        var around = new Region(arrow.At.X - 0.14, arrow.At.Y - 0.2, arrow.At.X + 0.14, arrow.At.Y + 0.2);
        var pointing = new List<double>();
        for (int frame = 1; frame <= Frames; frame++)
        {
            var written = Picture.Read(Frame(job, frame));
            var bare = Picture.Read(StageFrame(job, frame));
            var (x, y, count) = written.MiddleOfTheDifference(bare, around);
            Assert.True(count > 60, $"frame {frame}: no arrow ({count} pixels)");
            double dx = x - arrow.At.X * written.Width, dy = (1 - arrow.At.Y) * written.Height - y;
            pointing.Add(Math.Atan2(-dx, dy) * 180 / Math.PI);   // counter-clockwise from up, as the plan's turns
        }
        for (int frame = 1; frame <= Frames; frame++)
        {
            double off = Math.IEEERemainder(pointing[frame - 1] - headings[frame - 1], 360);
            Assert.True(Math.Abs(off) < 20, $"frame {frame}: the arrow points {pointing[frame - 1]:F0}°, the camera heads {headings[frame - 1]:F0}°");
        }
        Assert.True(Math.Abs(Math.IEEERemainder(pointing[^1] - pointing[0], 360)) > 60, "the arrow did not turn");
    }

    [BlenderFact]
    public async Task TheHeadingsWorkedOutForTheNorthArrow_AreTheOnesBlendersCameraHas_AtEveryFrame()
    {
        // The north arrow is turned by headings worked out before Blender starts. They have to be
        // the camera's own: here Blender is asked where its camera looks, frame by frame.
        const int Frames = 60;
        var viewpoints = new CameraSpec
        {
            Template = CameraTemplate.Viewpoints,
            Viewpoints = new ViewpointParams
            {
                Viewpoints =
                [
                    new CameraViewpoint { Position = new Therion.Cave3D.CaveVector3(-60, -40, 20), Target = new Therion.Cave3D.CaveVector3(0, 0, 0) },
                    new CameraViewpoint { Position = new Therion.Cave3D.CaveVector3(50, -60, 30), Target = new Therion.Cave3D.CaveVector3(10, 5, -5) },
                    new CameraViewpoint { Position = new Therion.Cave3D.CaveVector3(120, 40, 45), Target = new Therion.Cave3D.CaveVector3(20, 10, -10) },
                    new CameraViewpoint { Position = new Therion.Cave3D.CaveVector3(60, 90, 10), Target = new Therion.Cave3D.CaveVector3(0, 10, 0) },
                ],
            },
        };
        var cases = new (string Name, SceneSpec Spec, double Tolerance)[]
        {
            ("orbit", PlanFixtures.Preset("Orbit showcase"), 0.05),
            ("helix", PlanFixtures.Preset("Helix descent"), 0.05),
            ("flythrough", PlanFixtures.Preset("Full flythrough"), 0.05),
            // Blender eases through the poses on curves of its own making, which the planner
            // works out as well: level at the ends and at every turning point, smooth between.
            ("eased", PlanFixtures.Preset("Orbit showcase") with { Camera = viewpoints }, 0.05),
        };
        foreach (var (name, preset, tolerance) in cases)
        {
            var spec = preset with
            {
                Labels = new LabelsSpec(),
                Animation = new AnimationSpec { Fps = 10, DurationSeconds = Frames / 10.0 },
                Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180 },
            };
            var headings = ScreenTextPlanner.Headings(
                Emit.CameraPlanner.PlanPoses(PlanFixtures.WithPaths(spec), Medium.Value.Framing), Frames);
            var (_, job) = PlayerRunner.WriteJob("stages-headings-" + name, Medium.Value, spec);

            var report = await PlayerRunner.ProbeAsync(job.ScriptPath, new ProbeOptions { Frames = Enumerable.Range(1, Frames).ToList() });

            double worst = 0;
            foreach (var frame in report.Frames)
            {
                var forward = frame.Camera!.Forward;
                double seen = Math.Atan2(forward[0], forward[1]) * 180 / Math.PI;
                worst = Math.Max(worst, Math.Abs(Math.IEEERemainder(seen - headings[frame.Frame - 1], 360)));
            }
            Assert.True(worst <= tolerance, $"{name}: the heading is off by up to {worst:F3}° (allowed {tolerance}°)");
        }
    }

    [BlenderFact]
    public async Task RomanianLetters_AreDrawnWithTheirMarks()
    {
        // One frame for each text: a letter with its mark, then the letter without.
        string[] letters = ["ș", "s", "ț", "t", "ă", "a", "â", "a", "î", "ı", "Ș", "S", "Ț", "T", "", "s"];
        var spec = Orbit(new OverlaySpec { Title = "x" }, frames: letters.Length);
        var (_, job) = PlayerRunner.WriteJob("stages-letters", Small.Value, spec, plan => Quick(plan) with
        {
            Texts = new PlanScreenTexts
            {
                Items = letters.Select((letter, i) => new PlanScreenItem
                {
                    Id = "letter." + i, Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.5, 0.5), Text = letter, Size = 0.6,
                    Start = i == 0 ? null : i / 8.0, End = (i + 1) / 8.0,
                }).ToList(),
            },
        });

        await Run(job, "--keep-frames");

        var ink = Enumerable.Range(1, letters.Length)
            .Select(n => Picture.Read(Frame(job, n)).Differing(Picture.Read(StageFrame(job, n)), Region.All, inside: true))
            .ToList();
        for (int pair = 0; pair < 14; pair += 2)
        {
            Assert.True(ink[pair + 1] > 300, $"'{letters[pair + 1]}' is not drawn ({ink[pair + 1]} pixels)");
            Assert.True(ink[pair] > ink[pair + 1] * 1.03 && ink[pair] < ink[pair + 1] * 2.5,
                $"'{letters[pair]}' covers {ink[pair]} pixels and '{letters[pair + 1]}' {ink[pair + 1]}: the mark is not drawn");
        }
        // A character the font does not have looks like none of them.
        Assert.NotEqual(ink[0], ink[14]);
    }

    [BlenderFact]
    public async Task APlanWrittenByHand_FadesOutOfBlackAndBackIntoIt_ShowsItsTitleCardForItsTime_AndOneCaptionAtATime()
    {
        const int Frames = 16;   // two seconds at 8 fps
        var spec = Orbit(new OverlaySpec { Title = "x" }, Frames);
        var caption = new PlanScreenItem
        {
            Id = "c1", Role = "caption", Slot = "caption", Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.5, 0.12),
            AlignY = PlanAlignY.Bottom, Text = "MMMMMMMMMMMMMMMMMMMM", Size = 0.08, Backing = true, End = 1.25,
        };
        var (_, job) = PlayerRunner.WriteJob("stages-by-hand", Small.Value, spec, plan => Quick(plan) with
        {
            Texts = new PlanScreenTexts
            {
                FromBlack = 0.5, ToBlack = 0.5,
                Items =
                [
                    new PlanScreenItem
                    {
                        Id = "card", Role = "title", Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.5, 0.9), Text = "TITLE CARD", Size = 0.1,
                        End = 1.0, FadeOut = 0.25,
                    },
                    new PlanScreenItem { Id = "corner", Kind = PlanScreenItemKind.Text, At = new PlanPoint(0.05, 0.6), AlignX = PlanAlignX.Left, Text = "always", Size = 0.06 },
                    caption,
                    // Starts while the first is showing, and is much shorter: the first one goes.
                    caption with { Id = "c2", Text = "MM", Start = 0.75, End = 1.25 },
                ],
            },
        });

        await Run(job, "--keep-frames");

        var written = Enumerable.Range(1, Frames).Select(n => Picture.Read(Frame(job, n))).ToList();
        var bare = Enumerable.Range(1, Frames).Select(n => Picture.Read(StageFrame(job, n))).ToList();
        int Ink(int frame, Region region) => written[frame - 1].Differing(bare[frame - 1], region, inside: true);

        // Out of black over four frames, into black over the last four.
        Assert.True(written[0].Mean() < 1.0, $"the first frame is not black: {written[0].Mean():F1}");
        Assert.True(written[1].Mean() < written[2].Mean() && written[2].Mean() < written[3].Mean(), "the picture does not come out of black evenly");
        Assert.Equal(0, written[7].Differing(bare[7], new Region(0, 0.3, 1, 0.5), inside: true));
        Assert.True(written[^1].Mean() < 1.0, $"the last frame is not black: {written[^1].Mean():F1}");
        Assert.True(written[^3].Mean() > written[^2].Mean(), "the picture does not go to black evenly");

        // The title card is there for its second and gone after it; the corner line stays.
        var card = new Region(0.1, 0.78, 0.9, 1.0);
        var corner = new Region(0.0, 0.5, 0.5, 0.7);
        Assert.True(Ink(6, card) > 200, "no title card in its time");
        Assert.True(Ink(8, card) < Ink(6, card), "the title card does not fade");
        Assert.Equal(0, Ink(10, card));
        Assert.True(Ink(6, corner) > 50 && Ink(12, corner) > 50, "the corner line is not there throughout");

        // The long caption shows alone until the short one starts (frame 7), then only the short
        // one, and after their time (frame 11) neither.
        var captions = new Region(0.0, 0.0, 1.0, 0.3);
        Assert.True(Ink(5, captions) > Ink(9, captions) * 2, $"the first caption is still there: {Ink(5, captions)} then {Ink(9, captions)} pixels");
        Assert.True(Ink(9, captions) > 40, $"the second caption is not there: {Ink(9, captions)} pixels");
        Assert.Equal(0, Ink(12, captions));
    }

    // ---- the known defect of titles in the scene, on this path ----

    [BlenderFact]
    public async Task Flythrough_TitleIsNeverHiddenByTheWalls()
    {
        // Inside a narrow passage the walls come between the camera and anything that floats in
        // front of it. This title is not in the scene: it is written over the finished frames.
        const int Frames = 80;
        var spec = PlanFixtures.Preset("Full flythrough") with
        {
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = Frames / 10.0 },
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented Cave" } },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 480, Height = 270, BaseName = "clip" },
        };
        Assert.Empty(ScenePlanBuilder.NotCarried(spec));
        var (_, job) = PlayerRunner.WriteJob("stages-flythrough-title", Medium.Value, spec, Quick);

        await Run(job, "--keep-frames");

        // The title's letters: what is white in the first frame and was not before the title was
        // written over it. They are as white on every other frame, whatever the walls do there.
        var first = Picture.Read(Frame(job, 1));
        var letters = first.White(TitleRegion).Except(Picture.Read(StageFrame(job, 1)).White(TitleRegion)).ToList();
        Assert.True(letters.Count > 100, $"the title covers only {letters.Count} pixels");
        var hidden = new List<string>();
        for (int n = 1; n <= Frames; n++)
        {
            int there = Picture.Read(Frame(job, n)).White(TitleRegion).Intersect(letters).Count();
            if (there < letters.Count) hidden.Add($"{n} ({there} of {letters.Count} pixels)");
        }
        Assert.True(hidden.Count == 0, $"the title is hidden at {hidden.Count} of {Frames} frames: {string.Join(", ", hidden)}");
        // And nothing else of the picture is touched.
        Assert.All(Enumerable.Range(1, Frames).Where(n => n % 10 == 0), n =>
            Assert.Equal(0, Picture.Read(Frame(job, n)).Differing(Picture.Read(StageFrame(job, n)), TitleRegion, inside: false)));
    }

    // ---- one file ----

    [BlenderFact]
    public async Task ASingleFile_WithTexts_RendersInTwoStagesToo()
    {
        var spec = Orbit(new OverlaySpec { Title = "Invented Cave" }, frames: 3);
        var plan = Quick(PlayerRunner.PlanFor(Small.Value, spec));
        string dir = PlayerRunner.NewCase("stages-single-file");
        string script = Path.Combine(dir, "everything.py");
        string mesh = Small.Value.Conversion.Manifest.ModelPath;
        await File.WriteAllTextAsync(script, PlayerWriter.SingleFile(plan, new Dictionary<string, byte[]>
        {
            [Path.GetFileName(mesh)] = await File.ReadAllBytesAsync(mesh),
        }));

        var (exitCode, log) = await PlayerRunner.RunAsync(script);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {PlayerRunner.Reported(log, "error").FirstOrDefault()}\n  logs: {dir}");
        Assert.Contains("compose", PlayerRunner.Reported(log, "phase"));
        Assert.Equal(3, Directory.GetFiles(dir, "clip_*.png").Length);
        Assert.False(Directory.Exists(Path.Combine(dir, "frames")));
    }

    // ---- helpers ----

    private static Process StartBlender(PlayerJob job, params string[] arguments)
    {
        var start = new ProcessStartInfo(SceneProbeRunner.Blender.Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = job.Folder,
        };
        foreach (string argument in (string[])["-b", "--factory-startup", "--python-exit-code", "64", "--python", job.ScriptPath, "--", .. arguments])
            start.ArgumentList.Add(argument);
        var process = Process.Start(start)!;
        // Nothing reads what it says; let it say it.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static (int, int) Size(string png)
    {
        var (width, height, _) = Png.Decode(File.ReadAllBytes(png));
        return (width, height);
    }

    /// <summary>A part of the picture, in the plan's measure: from the left edge and from the
    /// bottom edge, each 0 to 1.</summary>
    private readonly record struct Region(double Left, double Bottom, double Right, double Top)
    {
        public static readonly Region All = new(0, 0, 1, 1);

        public bool Holds(int x, int y, int width, int height)
        {
            double fromLeft = (x + 0.5) / width, fromBottom = 1 - (y + 0.5) / height;
            return fromLeft >= Left && fromLeft <= Right && fromBottom >= Bottom && fromBottom <= Top;
        }
    }

    private sealed record Picture(int Width, int Height, byte[] Rgba)
    {
        public static Picture Read(string path)
        {
            var (width, height, rgba) = Png.Decode(File.ReadAllBytes(path));
            return new Picture(width, height, rgba);
        }

        /// <summary>The pixels that differ from <paramref name="other"/> by more than
        /// <paramref name="tolerance"/> in a channel, inside the region or outside it.</summary>
        public int Differing(Picture other, Region region, bool inside, int tolerance = 2)
        {
            Assert.Equal((other.Width, other.Height), (Width, Height));
            int count = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (region.Holds(x, y, Width, Height) == inside && Differs(other, x, y, tolerance))
                        count++;
            return count;
        }

        /// <summary>The middle of the pixels of a region that differ from
        /// <paramref name="other"/>, in pixels from the left and from the top, and how many they are.</summary>
        public (double X, double Y, int Count) MiddleOfTheDifference(Picture other, Region region)
        {
            double sumX = 0, sumY = 0;
            int count = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (region.Holds(x, y, Width, Height) && Differs(other, x, y, 40))
                    {
                        sumX += x + 0.5;
                        sumY += y + 0.5;
                        count++;
                    }
            return count == 0 ? (0, 0, 0) : (sumX / count, sumY / count, count);
        }

        /// <summary>The pixels of a region that are white, each as its place in the picture.</summary>
        public IEnumerable<int> White(Region region)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int p = (y * Width + x) * 4;
                    if (region.Holds(x, y, Width, Height) && Rgba[p] > 225 && Rgba[p + 1] > 225 && Rgba[p + 2] > 225)
                        yield return y * Width + x;
                }
        }

        public double Mean()
        {
            long sum = 0;
            for (int p = 0; p < Width * Height; p++) sum += Rgba[p * 4] + Rgba[p * 4 + 1] + Rgba[p * 4 + 2];
            return sum / (3.0 * Width * Height);
        }

        private bool Differs(Picture other, int x, int y, int tolerance)
        {
            int p = (y * Width + x) * 4;
            return Math.Abs(Rgba[p] - other.Rgba[p]) > tolerance
                   || Math.Abs(Rgba[p + 1] - other.Rgba[p + 1]) > tolerance
                   || Math.Abs(Rgba[p + 2] - other.Rgba[p + 2]) > tolerance;
        }
    }
}
