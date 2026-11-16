// Builds a scene in a real Blender and reads it back as a SceneReport: the generated script runs
// in its inspect mode (the scene is built, nothing is rendered), then scene_probe.py describes
// what is there. A test asserts on the report the way a UI test asserts on a window.
//
// Every case keeps its render.py, run.log and scene-report.json under
// %TEMP%/thide-blender-probe/<case>/, so a failing case can be rerun by hand:
//   blender -b --factory-startup --python render.py --python scene_probe.py -- --report r.json

using System.Globalization;
using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Execution;
using Therion.Blender.Parsing;
using Therion.Blender.Sources;

namespace Therion.Blender.Tests.SceneProbe;

/// <summary>An invented cave converted through the real pipeline: the files and metadata the
/// application would hand the script generator.</summary>
internal sealed record ProbeAssets(string Directory, ConversionResult Conversion, CameraFraming Framing);

internal sealed record ProbeOptions
{
    /// <summary>Frames to sample; when empty, <see cref="Samples"/> frames spread over the range.</summary>
    public IReadOnlyList<int> Frames { get; init; } = [];
    public int Samples { get; init; } = 5;

    /// <summary>Also measure each role's share of the pixels at this size.</summary>
    public (int Width, int Height)? Pixels { get; init; }

    /// <summary>Also write a contact sheet of the sampled frames (sheet.png in the case folder).</summary>
    public bool Sheet { get; init; }
    public (int Width, int Height) SheetCell { get; init; } = (320, 180);
}

internal static class SceneProbeRunner
{
    private const int TimeoutSeconds = 600;

    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "thide-blender-probe");

    private static string ProbeScript => Path.Combine(AppContext.BaseDirectory, "SceneProbe", "scene_probe.py");

    private static readonly Lazy<BlenderInstallation> Located = new(() =>
    {
        var locate = new BlenderLocator(new ProcessBlenderProbe())
            .Locate(Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH"));
        Assert.True(locate.IsUsable, $"No usable Blender was located: {locate.Detail}");
        return locate.Installation!;
    });

    public static BlenderInstallation Blender => Located.Value;

    /// <summary>Writes <paramref name="model"/> as a .lox file and converts it the way the
    /// application does. Stations flagged as continuations become leads.</summary>
    public static ProbeAssets Prepare(string name, CaveModel model)
    {
        string dir = Path.Combine(Root, "assets-" + name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        string loxPath = Path.Combine(dir, "model.lox");
        LoxWriter.WriteFile(model, loxPath);
        var source = new ResolvedModelSource { Path = loxPath, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        var conversion = CaveConversionPipeline.ConvertResolvedFull(
            source, new ConversionOptions { OutputDirectory = dir, Leads = InventedCave.Leads(model) });
        return new ProbeAssets(dir, conversion, CameraFraming.FromGeometry(conversion.Geometry));
    }

    /// <summary>Generates the script for <paramref name="spec"/>, builds the scene in Blender
    /// and returns what the probe reports.</summary>
    public static async Task<SceneReport> RunAsync(
        string caseName, ProbeAssets assets, SceneSpec spec, ProbeOptions? options = null)
    {
        options ??= new ProbeOptions();
        string caseDir = NewCaseDirectory(caseName);
        spec = WithPaths(spec, assets, caseDir);
        string scriptPath = Path.Combine(caseDir, "render.py");
        await File.WriteAllTextAsync(
            scriptPath,
            ScriptGenerator.Generate(spec, null, assets.Framing, assets.Conversion.Meta, ScriptPurpose.Interactive),
            new UTF8Encoding(false));

        string reportPath = Path.Combine(caseDir, "scene-report.json");
        var arguments = new List<string> { "--python", scriptPath, "--python", ProbeScript, "--", "--report", reportPath };
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
        return await RunProbeAsync(caseDir, arguments, reportPath);
    }

    /// <summary>Renders <paramref name="spec"/> for real (keep it tiny) and returns the folder
    /// the output landed in.</summary>
    public static async Task<string> RenderAsync(string caseName, ProbeAssets assets, SceneSpec spec)
    {
        string caseDir = NewCaseDirectory(caseName);
        spec = WithPaths(spec, assets, caseDir);
        string scriptPath = Path.Combine(caseDir, "render.py");
        await File.WriteAllTextAsync(
            scriptPath,
            ScriptGenerator.Generate(spec, null, assets.Framing, assets.Conversion.Meta, ScriptPurpose.Render),
            new UTF8Encoding(false));
        var (exitCode, log) = await BlenderTestProcess.RunAsync(
            Blender.Path, ["--python", scriptPath], caseDir, TimeoutSeconds);
        await File.WriteAllTextAsync(Path.Combine(caseDir, "run.log"), log);
        Assert.True(exitCode == 0 && log.Contains("THIDE:done=1", StringComparison.Ordinal),
            $"The render failed (exit code {exitCode}): {BlenderTestProcess.ExceptionLine(log)}\n  logs: {caseDir}");
        return caseDir;
    }

    /// <summary>Reads a rendered movie back through Blender: size, frame count, frame rate.</summary>
    public static async Task<ProbeMovie> ReadMovieAsync(string moviePath)
    {
        string caseDir = Path.GetDirectoryName(moviePath)!;
        string reportPath = Path.Combine(caseDir, "movie-report.json");
        var report = await RunProbeAsync(
            caseDir, ["--python", ProbeScript, "--", "--movie", moviePath, "--report", reportPath], reportPath);
        return report.Movie ?? throw new InvalidOperationException("The probe reported no movie.");
    }

    private static async Task<SceneReport> RunProbeAsync(string caseDir, IEnumerable<string> arguments, string reportPath)
    {
        var (exitCode, log) = await BlenderTestProcess.RunAsync(Blender.Path, arguments, caseDir, TimeoutSeconds);
        await File.WriteAllTextAsync(Path.Combine(caseDir, "probe.log"), log);
        Assert.True(exitCode == 0 && File.Exists(reportPath),
            $"The scene probe failed (exit code {exitCode}): {BlenderTestProcess.ExceptionLine(log)}\n  logs: {caseDir}");
        return SceneReport.Parse(await File.ReadAllTextAsync(reportPath));
    }

    private static string NewCaseDirectory(string caseName)
    {
        string caseDir = Path.Combine(Root, caseName);
        if (Directory.Exists(caseDir)) Directory.Delete(caseDir, recursive: true);
        Directory.CreateDirectory(caseDir);
        return caseDir;
    }

    private static SceneSpec WithPaths(SceneSpec spec, ProbeAssets assets, string caseDir) => spec with
    {
        Source = spec.Source with
        {
            PlyPath = assets.Conversion.Manifest.ModelPath,
            SceneMetaPath = assets.Conversion.Manifest.SceneMetaPath,
        },
        Output = spec.Output with { OutputDirectory = caseDir },
    };
}
