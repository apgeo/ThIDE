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

        string folder = Path.Combine(root, "unpacked");
        Unzip(zip, folder);

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
        await File.WriteAllTextAsync(Path.Combine(folder, "..", arguments.Length == 0 ? "render.log" : "draft.log"), log);
        return (process.ExitCode, log);
    }

    private static string Failure(string log)
        => PlayerRunner.Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log);
}
