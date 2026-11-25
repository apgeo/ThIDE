// The render service's exports in a real Blender, on an invented cave with invented images: the
// exported folder and the exported single script render, with nothing of ThIDE beside them, the
// frames a render of the same spec gives; and the export to look at builds, as Blender's window
// would be given it, the scene of the render. Opt-in like the other Blender tests
// (THIDE_BLENDER_SMOKE=1; THIDE_BLENDER_PATH picks the Blender).

using System.Diagnostics;
using Therion.Blender.Execution;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Blender.Tests.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests;

public class BlenderExportBlenderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-export-blender-" + Guid.NewGuid().ToString("N"));

    public BlenderExportBlenderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private RenderSource Cave()
    {
        string lox = Path.Combine(_root, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        return new RenderSource(new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile });
    }

    /// <summary>A short clip with what only the player draws: a title and a caption, a callout
    /// and a photo, a reveal, and an image the scene is lit by.</summary>
    private SceneSpec Scene() => BuiltInPresets.OrbitShowcase().Spec with
    {
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
        Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
        Lighting = new LightingSpec
        {
            Rig = LightingRig.HdriFile,
            HdriPath = TestImages.Write(Path.Combine(_root, "skies"), "evening sky.hdr", TestImages.RadianceHdr(16, 8, TestImages.Compass)),
            HdriRotation = 90,
        },
        Labels = new LabelsSpec
        {
            Overlays = new OverlaySpec
            {
                Title = "{cave}", Subtitle = "an invented survey", ScaleBar = true, NorthArrow = true,
                Captions = [new CaptionSpec { Text = "Sala Mare", Start = 0.25, End = 0.75 }],
            },
        },
        Annotations =
        [
            new AnnotationSpec { Kind = AnnotationKind.Callout, Station = "main0", Text = "Intrarea" },
            new AnnotationSpec
            {
                Id = "flag", Kind = AnnotationKind.Photo, Station = "main6", Size = 40, Offset = new CaveVector3(0, 0, 25),
                Image = TestImages.Write(Path.Combine(_root, "trip"), "the flag.png", TestImages.FlagPng()),
            },
        ],
        Reveal = new RevealSpec { By = RevealBy.Distance, From = "main0", End = 0.75 },
        Animation = new AnimationSpec { Fps = 8, DurationSeconds = 1 },
        Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 160, Height = 90, BaseName = "clip" },
    };

    private BlenderRenderService Service() => new(
        new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()), Path.Combine(_root, "jobs"),
        blenderOverride: () => Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH"));

    /// <summary>Starts Blender on an exported script the way its README says, in the folder it
    /// lies in, and returns the frames it wrote.</summary>
    private static async Task<List<string>> RenderExportedAsync(RenderExport export)
    {
        var (exitCode, log) = await BlenderTestProcess.RunAsync(
            SceneProbeRunner.Blender.Path, ["--python", export.ScriptPath], export.Directory, timeoutSeconds: 600);
        await File.WriteAllTextAsync(Path.Combine(export.Directory, "run.log"), log);
        Assert.True(exitCode == 0, $"The exported script failed (exit code {exitCode}): "
            + $"{PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log)}");
        Assert.Equal(["1"], PlayerRunner.Reported(log, "done"));
        return Directory.GetFiles(Path.Combine(export.Directory, "out"), "clip_*.png").Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>How far two pictures are apart: the mean difference of their channels, of 255.</summary>
    private static double Difference(string a, string b)
    {
        var (first, second) = (RenderedPicture.Read(a), RenderedPicture.Read(b));
        Assert.Equal((first.Width, first.Height), (second.Width, second.Height));
        long sum = 0;
        for (int i = 0; i < first.Rgba.Length; i++) sum += Math.Abs(first.Rgba[i] - second.Rgba[i]);
        return sum / (double)first.Rgba.Length;
    }

    [BlenderFact]
    public async Task AnExportedFolder_AndAnExportedScript_RenderTheFramesARenderOfTheSameSpecGives()
    {
        var spec = Scene();
        var rendered = await Service().RenderAsync(spec with { Output = spec.Output with { OutputDirectory = Path.Combine(_root, "renders") } }, Cave());
        Assert.True(rendered.Succeeded, $"{rendered.FailureKind}: {rendered.ErrorMessage}\n  log: {rendered.JobLogPath}");
        var frames = rendered.OutputPaths.Where(p => p.EndsWith(".png", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(8, frames.Count);

        // The folder: a plan, the player, the mesh and the images, started as its README says.
        var folder = await Service().ExportAsync(spec, Cave(), Path.Combine(_root, "folder"), new ExportOptions());
        var fromFolder = await RenderExportedAsync(folder);
        Assert.Equal(8, fromFolder.Count);
        Assert.False(Directory.Exists(Path.Combine(folder.Directory, "frames")), "the frames of the first stage were left behind");

        // One file, alone in a folder of its own.
        var one = await Service().ExportAsync(spec, Cave(), Path.Combine(_root, "one"), new ExportOptions { SingleFile = true });
        Assert.Equal(["render.py"], Directory.GetFileSystemEntries(one.Directory).Select(Path.GetFileName));
        var fromScript = await RenderExportedAsync(one);
        Assert.Equal(8, fromScript.Count);

        // The same scene as the render: frame for frame the same picture, the texts and the
        // photo in them.
        for (int f = 0; f < 8; f++)
        {
            Assert.True(Difference(frames[f], fromFolder[f]) < 0.5, $"frame {f + 1} of the exported folder is {Difference(frames[f], fromFolder[f]):F2} of 255 from the render's");
            Assert.True(Difference(frames[f], fromScript[f]) < 0.5, $"frame {f + 1} of the exported script is {Difference(frames[f], fromScript[f]):F2} of 255 from the render's");
        }
        var picture = RenderedPicture.Read(fromFolder[7]);
        Assert.True(picture.Of(new ColorRgb(1, 0, 0)).Count > 20 && picture.Of(new ColorRgb(0, 0, 1)).Count > 20,
            "the photo is not in the last frame of the exported folder's render");
    }

    [BlenderFact]
    public async Task AnExportToLookAt_BuildsTheSceneOfTheRender_WithoutRenderingIt()
    {
        var spec = Scene();
        string renders = Path.Combine(_root, "renders");

        var export = await Service().ExportAsync(
            spec with { Output = spec.Output with { OutputDirectory = renders } }, Cave(), Path.Combine(_root, "gui"), new ExportOptions { Interactive = true });

        // What Blender's window is started with, here without the window: the scene is built
        // and nothing is rendered.
        Assert.Equal(["--build-only"], export.ScriptArguments);
        var clock = Stopwatch.StartNew();
        var (exitCode, log) = await PlayerRunner.RunAsync(export.ScriptPath, [.. export.ScriptArguments]);
        Assert.True(exitCode == 0, $"Building the scene failed: {PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log)}");
        Assert.Equal(["1"], PlayerRunner.Reported(log, "built"));
        Assert.Empty(PlayerRunner.Reported(log, "done"));
        Assert.False(Directory.Exists(renders));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(120));

        // The scene itself: the cave, the camera of the render, the annotations and the world
        // of the image; a render started from the window would write where "Render" does.
        var report = await PlayerRunner.ProbeAsync(export.ScriptPath);
        Assert.Equal("Camera", report.Scene.Camera);
        Assert.Equal((1, 8), (report.Scene.FrameStart, report.Scene.FrameEnd));
        Assert.Equal([160, 90], report.Scene.Resolution);
        Assert.StartsWith(renders, report.Scene.OutputPath);
        Assert.Contains(report.Objects, o => o.Name == "CaveModel" && o.Polygons > 0);
        Assert.Contains(report.Objects, o => o.Role == "photo");
        Assert.Contains(report.Objects, o => o.Role == "callout" && o.Text == "Intrarea");
        // The texts are the second stage's: none of them is an object of the scene.
        Assert.True(export.TextsInSecondStage);
        Assert.DoesNotContain(report.Objects, o => o.Text is "Invented small cave" or "an invented survey" or "Sala Mare");
    }

    /// <summary>
    /// The export to look at, started as "Open in Blender" starts it: without <c>-b</c>, so in
    /// a window. Runs where a display of its own can be had for it (Linux with <c>xvfb-run</c>),
    /// never on the desktop of whoever runs the tests; a Blender that cannot draw on such a
    /// display (no OpenGL there) says so and the test passes without a verdict.
    /// </summary>
    [BlenderFact]
    public async Task AnExportToLookAt_OpenedInBlendersWindow_IsSeenThroughTheCameraOfTheRender()
    {
        if (!OperatingSystem.IsLinux() || !OnPath("xvfb-run")) return;
        var export = await Service().ExportAsync(Scene(), Cave(), Path.Combine(_root, "window"), new ExportOptions { Interactive = true });
        string views = Path.Combine(_root, "views.json");
        string script = Path.Combine(AppContext.BaseDirectory, "Player", "window_views.py");

        var psi = new ProcessStartInfo("xvfb-run")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = export.Directory,
        };
        string[] arguments =
        [
            "-a", "-s", "-screen 0 1280x800x24 -nolisten tcp", SceneProbeRunner.Blender.Path, "--factory-startup",
            "--python", export.ScriptPath, "--python", script, "--", .. export.ScriptArguments, "--views", views,
        ];
        foreach (string argument in arguments) psi.ArgumentList.Add(argument);
        using var blender = Process.Start(psi)!;
        var output = blender.StandardOutput.ReadToEndAsync();
        var errors = blender.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try { await blender.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            blender.Kill(entireProcessTree: true);
            Assert.Fail("Blender's window did not close in three minutes.");
        }
        string log = await output + await errors;
        if (!File.Exists(views))
        {
            // No window could be had: this display gives Blender nothing to draw with.
            Assert.DoesNotContain("THIDE:error=", log);
            Assert.DoesNotContain("Traceback", log);
            return;
        }

        using var report = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(views));
        Assert.False(report.RootElement.GetProperty("background").GetBoolean());
        Assert.Equal("Camera", report.RootElement.GetProperty("camera").GetString());
        Assert.True(report.RootElement.GetProperty("objects").GetInt32() > 3);
        var seen = report.RootElement.GetProperty("views").EnumerateArray().Select(v => v.GetString()).ToList();
        Assert.NotEmpty(seen);
        Assert.All(seen, view => Assert.Equal("CAMERA", view));
        Assert.Contains("THIDE:built=1", log);
    }

    private static bool OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(d => File.Exists(Path.Combine(d, tool)));
}
