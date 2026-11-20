// Plays scene plans in a real Blender for the tests: build the scene and read it back with the
// scene probe, or run the script as it is and return its log.
//
// Every case keeps its job folder (plan, player, meshes, run.log, scene-report.json) under
// %TEMP%/thide-blender-probe/player-<case>/, so a failing case can be rerun by hand:
//   blender -b --factory-startup --python render.py -- --build-only

using System.Globalization;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;

namespace Therion.Blender.Tests.Player;

internal static class PlayerRunner
{
    private const int TimeoutSeconds = 600;

    private static string ProbeScript => Path.Combine(AppContext.BaseDirectory, "SceneProbe", "scene_probe.py");

    /// <summary>A folder of feature modules that are not part of the player.</summary>
    public static string ExtraModules => Path.Combine(AppContext.BaseDirectory, "Player", "ExtraModules");

    public static string TwoPartGlbScript => Path.Combine(AppContext.BaseDirectory, "Player", "two_part_glb.py");

    public static string DraftColoursScript => Path.Combine(AppContext.BaseDirectory, "Player", "draft_colours.py");

    /// <summary>A new, empty folder for one case.</summary>
    public static string NewCase(string name)
    {
        string dir = Path.Combine(SceneProbeRunner.Root, "player-" + name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The plan of <paramref name="spec"/> for a converted invented cave, written as a
    /// job folder with the cave's mesh. The render writes into the job folder.</summary>
    public static (ScenePlan Plan, PlayerJob Job) WriteJob(string caseName, ProbeAssets assets, SceneSpec spec, Func<ScenePlan, ScenePlan>? change = null)
    {
        var plan = PlanFor(assets, spec);
        if (change is not null) plan = change(plan);
        var job = PlayerWriter.WriteJob(plan, NewCase(caseName), FilesOf(assets, plan));
        return (plan, job);
    }

    /// <summary>The spec with the files of the converted cave: its mesh, and its terrain and
    /// surface image when it was converted with them. The render writes beside the plan.</summary>
    public static SceneSpec SpecFor(ProbeAssets assets, SceneSpec spec) => spec with
    {
        Source = spec.Source with
        {
            PlyPath = assets.Conversion.Manifest.ModelPath,
            SurfacePlyPath = assets.Conversion.Manifest.SurfacePath,
            SurfaceImagePath = assets.Conversion.Manifest.SurfaceImagePath,
        },
        Output = spec.Output with { OutputDirectory = "." },
    };

    public static ScenePlan PlanFor(ProbeAssets assets, SceneSpec spec)
        => ScenePlanBuilder.FromSpec(SpecFor(assets, spec), assets.Framing, assets.Conversion.Meta);

    public static Dictionary<string, string> MeshOf(ProbeAssets assets)
        => new() { [Path.GetFileName(assets.Conversion.Manifest.ModelPath)] = assets.Conversion.Manifest.ModelPath };

    /// <summary>The files a plan of the converted cave names, as the conversion wrote them.</summary>
    public static Dictionary<string, string> FilesOf(ProbeAssets assets, ScenePlan plan)
        => PlayerWriter.FilesOf(plan).ToDictionary(f => f.Path, f => Path.Combine(assets.Directory, f.Path));

    /// <summary>Builds the scene of <paramref name="scriptPath"/> without rendering it and
    /// returns what the probe reports.</summary>
    public static async Task<SceneReport> ProbeAsync(string scriptPath, ProbeOptions? options = null, params string[] playerArguments)
    {
        string caseDir = Path.GetDirectoryName(scriptPath)!;
        string reportPath = Path.Combine(caseDir, "scene-report.json");
        var arguments = new List<string> { "--python", scriptPath, "--python", ProbeScript, "--", "--build-only", "--report", reportPath };
        arguments.AddRange(playerArguments);
        return await ProbeAsync(caseDir, arguments, reportPath, "run.log", options ?? new ProbeOptions());
    }

    /// <summary>Opens a scene that was saved as a Blender file and returns what the probe
    /// reports about it.</summary>
    public static async Task<SceneReport> ProbeBlendAsync(string blendPath, ProbeOptions? options = null)
    {
        string caseDir = Path.GetDirectoryName(blendPath)!;
        string reportPath = Path.Combine(caseDir, "blend-report.json");
        var arguments = new List<string> { blendPath, "--python", ProbeScript, "--", "--report", reportPath };
        return await ProbeAsync(caseDir, arguments, reportPath, "blend-probe.log", options ?? new ProbeOptions());
    }

    private static async Task<SceneReport> ProbeAsync(
        string caseDir, List<string> arguments, string reportPath, string logName, ProbeOptions options)
    {
        if (options.Frames.Count > 0)
        {
            arguments.Add("--frames");
            arguments.Add(string.Join(",", options.Frames.Select(f => f.ToString(CultureInfo.InvariantCulture))));
        }
        else
        {
            arguments.Add("--samples");
            arguments.Add(options.Samples.ToString(CultureInfo.InvariantCulture));
        }
        if (options.Pixels is { } pixels)
        {
            arguments.Add("--pixels");
            arguments.Add($"{pixels.Width}x{pixels.Height}");
        }
        if (options.Sheet)
        {
            arguments.Add("--sheet");
            arguments.Add(Path.Combine(caseDir, "sheet.png"));
            arguments.Add("--sheet-cell");
            arguments.Add($"{options.SheetCell.Width}x{options.SheetCell.Height}");
        }

        var (exitCode, log) = await BlenderTestProcess.RunAsync(SceneProbeRunner.Blender.Path, arguments, caseDir, TimeoutSeconds);
        await File.WriteAllTextAsync(Path.Combine(caseDir, logName), log);
        Assert.True(exitCode == 0 && File.Exists(reportPath),
            $"Building the scene failed (exit code {exitCode}): {Failure(log)}\n  logs: {caseDir}");
        return SceneReport.Parse(await File.ReadAllTextAsync(reportPath));
    }

    /// <summary>Runs <paramref name="scriptPath"/> as it is — a render, unless the arguments say
    /// otherwise — and returns Blender's exit code and output.</summary>
    public static async Task<(int ExitCode, string Log)> RunAsync(string scriptPath, params string[] playerArguments)
    {
        string caseDir = Path.GetDirectoryName(scriptPath)!;
        var arguments = new List<string> { "--python", scriptPath };
        if (playerArguments.Length > 0)
        {
            arguments.Add("--");
            arguments.AddRange(playerArguments);
        }
        var (exitCode, log) = await BlenderTestProcess.RunAsync(SceneProbeRunner.Blender.Path, arguments, caseDir, TimeoutSeconds);
        await File.WriteAllTextAsync(Path.Combine(caseDir, "run.log"), log);
        return (exitCode, log);
    }

    /// <summary>The values of the THIDE: lines with this key, in order.</summary>
    public static IReadOnlyList<string> Reported(string log, string key)
        => log.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("THIDE:" + key + "=", StringComparison.Ordinal))
            .Select(line => line[("THIDE:" + key + "=").Length..])
            .ToList();

    private static string Failure(string log)
        => Reported(log, "error").FirstOrDefault() ?? BlenderTestProcess.ExceptionLine(log);
}
