// A render bundle as its user gets it: written from an invented cave, unzipped into a fresh folder
// with nothing else, and rendered with its own run script. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1); the script is given THIDE_BLENDER_PATH's Blender through BLENDER, so
// a run per Blender version covers each. The case is kept under
// %TEMP%/thide-blender-probe/bundle-render/.

using System.Diagnostics;
using System.IO.Compression;
using Therion.Blender.Execution;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests.Player;

public class RenderBundleBlenderTests
{
    private const int TimeoutSeconds = 600;

    [BlenderFact]
    public async Task ABundle_UnzippedElsewhere_RendersWithItsRunScript_AndSoDoesItsDraft()
    {
        if (OperatingSystem.IsWindows()) return; // render.sh; render.cmd is not run by the tests
        string root = Path.Combine(SceneProbeRunner.Root, "bundle-render");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        // Written from an invented cave; the conversion's folder is gone before the bundle is used.
        string work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        string lox = Path.Combine(work, "cave.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        var spec = PlanFixtures.Preset("Full flythrough") with
        {
            Name = "Invented Small Cave",
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 0.4 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "clip" },
        };
        var content = RenderBundleWriter.FromModel(
            new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile },
            spec, work, draft: true);
        string zip = Path.Combine(root, "cave-bundle.zip");
        RenderBundleWriter.WriteZip(content, zip);
        Directory.Delete(work, recursive: true);

        string unpacked = Path.Combine(root, "unpacked");
        Unzip(zip, unpacked);
        // One folder, named after the zip.
        string folder = Assert.Single(Directory.GetFileSystemEntries(unpacked));
        Assert.Equal(Path.Combine(unpacked, "cave-bundle"), folder);

        var (code, log) = await RunScript(folder);
        Assert.True(code == 0, $"render.sh failed (exit code {code}): {Failure(log)}\n  logs: {root}");
        Assert.Equal(["1"], PlayerRunner.Reported(log, "done"));
        Assert.Equal(SceneProbeRunner.Blender.Version.ToString(), PlayerRunner.Reported(log, "blender").Single());
        var movie = await SceneProbeRunner.ReadMovieAsync(Path.Combine(folder, "out", "clip.mp4"));
        Assert.Equal([96, 64], movie.Size);
        Assert.Equal(4, movie.Frames);

        (code, log) = await RunScript(folder, "--draft");
        Assert.True(code == 0, $"render.sh --draft failed (exit code {code}): {Failure(log)}\n  logs: {root}");
        var draft = await SceneProbeRunner.ReadMovieAsync(Path.Combine(folder, "out", "clip-draft.mp4"));
        Assert.Equal([48, 32], draft.Size);
        Assert.Equal(4, draft.Frames);

        // The renders wrote into the bundle's folder and nowhere else.
        Assert.Equal(["out"], Directory.GetDirectories(folder).Select(Path.GetFileName).Where(d => d != PlayerFiles.PackageName));
    }

    [BlenderFact]
    public async Task ABundleWithTextsAndATerrain_RendersInTwoStages_AndItsMovieIsWrittenAgainAfterATextWasChanged()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine(SceneProbeRunner.Root, "bundle-texts");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        string lox = Path.Combine(work, "cave.lox");
        LoxWriter.WriteFile(InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small)), lox);
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
            Labels = new LabelsSpec
            {
                Overlays = new OverlaySpec
                {
                    Title = "Invented Cave", NorthArrow = true, EndCard = new EndCardSpec { Text = "The end", Seconds = 0.5 },
                    Captions = [new CaptionSpec { Text = "A caption", Start = 0, End = 0.3 }],
                },
            },
            Surface = new SurfaceSpec { Mode = SurfaceMode.Textured },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 0.4 },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 160, Height = 90, BaseName = "clip" },
        };
        var content = RenderBundleWriter.FromModel(
            new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, spec, work);
        Assert.Empty(content.NotCarried);
        string folder = Path.Combine(root, "bundle");
        RenderBundleWriter.WriteFolder(content, folder);
        Directory.Delete(work, recursive: true);
        Assert.True(File.Exists(Path.Combine(folder, "surface.png")));
        Assert.True(File.Exists(Path.Combine(folder, "out", "clip.srt")));

        // Both stages, the frames kept for what follows.
        var (code, log) = await RunScript(folder, "--keep-frames");
        Assert.True(code == 0, $"render.sh failed (exit code {code}): {Failure(log)}\n  logs: {root}");
        Assert.Equal(["scene", "import", "engine", "render", "compose"], PlayerRunner.Reported(log, "phase"));
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        string movie = Path.Combine(folder, "out", "clip.mp4");
        Assert.Equal(4 + 5, (await SceneProbeRunner.ReadMovieAsync(movie)).Frames);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(folder, "frames"), "frame_*.png").Length);
        byte[] first = await File.ReadAllBytesAsync(movie);

        // Another title, written into the plan by hand as the README says; then only the second stage.
        string planPath = Path.Combine(folder, "scene-plan.json");
        string plan = await File.ReadAllTextAsync(planPath);
        Assert.Contains("\"text\": \"Invented Cave\"", plan);
        await File.WriteAllTextAsync(planPath, plan.Replace("\"text\": \"Invented Cave\"", "\"text\": \"Peștera Inventată\""));
        (code, log) = await RunScript(folder, "--compose");
        Assert.True(code == 0, $"render.sh --compose failed (exit code {code}): {Failure(log)}\n  logs: {root}");
        Assert.Equal(["compose"], PlayerRunner.Reported(log, "phase"));
        Assert.NotEqual(first, await File.ReadAllBytesAsync(movie));
        Assert.Equal(9, (await SceneProbeRunner.ReadMovieAsync(movie)).Frames);
        Assert.False(Directory.Exists(Path.Combine(folder, "frames")), "composing without --keep-frames removes the frames");
    }

    [BlenderFact]
    public async Task ABundleOfASceneUnderAnHdriImage_RendersWithItsRunScript_WithTheImageItCarries()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine(SceneProbeRunner.Root, "bundle-hdri");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        string lox = Path.Combine(work, "cave.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        string sky = TestImages.Write(Path.Combine(root, "skies"), "invented sky.exr", TestImages.OpenExr(64, 32, TestImages.Compass));
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat, BaseColor = new ColorRgb(0.5, 0.5, 0.5) },
            Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = sky, HdriRotation = 45 },
            Labels = new LabelsSpec(),
            Animation = new AnimationSpec { Fps = 5, DurationSeconds = 0.4 },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 160, Height = 90, BaseName = "clip" },
        };
        var content = RenderBundleWriter.FromModel(
            new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, spec, work, draft: true);
        Assert.Empty(content.NotCarried);
        string zip = Path.Combine(root, "sky-bundle.zip");
        RenderBundleWriter.WriteZip(content, zip);
        // Nothing of where the bundle was made is left: not the conversion, not the image.
        Directory.Delete(work, recursive: true);
        Directory.Delete(Path.Combine(root, "skies"), recursive: true);

        string unpacked = Path.Combine(root, "unpacked");
        Unzip(zip, unpacked);
        string folder = Path.Combine(unpacked, "sky-bundle");
        Assert.True(File.Exists(Path.Combine(folder, "hdri", "invented_sky.exr")));

        var (code, log) = await RunScript(folder);
        Assert.True(code == 0, $"render.sh failed (exit code {code}): {Failure(log)}\n  logs: {root}");
        Assert.Empty(PlayerRunner.Reported(log, "warning"));
        // The sky above the cave is the image's: every pixel of the upper edge one of its colours.
        var top = RenderedPicture.Read(Path.Combine(folder, "out", "clip_0001.png")).Part(0, 1, 0, 0.15);
        int coloured = new[] { new ColorRgb(1, 0, 0), new ColorRgb(0, 1, 0), new ColorRgb(0, 0, 1), new ColorRgb(1, 1, 0) }.Sum(c => top.Of(c).Count);
        Assert.True(coloured > 0.9 * top.Width * top.Height, $"{coloured} of {top.Width * top.Height} pixels of the upper edge are of the image's colours");

        (code, log) = await RunScript(folder, "--draft");
        Assert.True(code == 0, $"render.sh --draft failed (exit code {code}): {Failure(log)}\n  logs: {root}");
        Assert.True(File.Exists(Path.Combine(folder, "out", "clip-draft_0001.png")));
    }

    /// <summary>Unpacks with the system's unzip when there is one, as a user would; with .NET's
    /// own reader otherwise. Either keeps render.sh executable.</summary>
    private static void Unzip(string zip, string folder)
    {
        Directory.CreateDirectory(folder);
        if (File.Exists("/usr/bin/unzip"))
        {
            using var unzip = Process.Start(new ProcessStartInfo("/usr/bin/unzip", ["-q", zip, "-d", folder]))!;
            unzip.WaitForExit();
            Assert.Equal(0, unzip.ExitCode);
        }
        else
        {
            ZipFile.ExtractToDirectory(zip, folder);
        }
    }

    /// <summary>Starts ./render.sh in its folder with this run's Blender in BLENDER.</summary>
    private static async Task<(int ExitCode, string Log)> RunScript(string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, "render.sh"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = folder,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["BLENDER"] = SceneProbeRunner.Blender.Path;
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return (BlenderTestProcess.TimedOut, await output + await error);
        }
        string log = await output + await error;
        await File.WriteAllTextAsync(Path.Combine(folder, "..", "..", arguments.Length == 0 ? "render.log" : "draft.log"), log);
        return (process.ExitCode, log);
    }

    private static string Failure(string log)
        => PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log);
}
