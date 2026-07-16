// Generated-script ⇄ real-Blender smoke loop. Golden tests prove byte-stability and the
// opportunistic py_compile check proves syntax, but only a real Blender proves the emitted
// bpy calls still exist — Blender's Python API churns every minor release (4.2 → 5.x broke
// the NISHITA sky enum and gated FFMPEG behind image_settings.media_type). This fact runs a
// spec matrix covering every emitter branch through the locally installed Blender: scene
// build only for most cases (ScriptPurpose.Interactive, headless) plus tiny real renders
// for the output/collector paths.
//
// Opt-in (needs Blender >= 4.2 on the machine):
//   pwsh tools/blender-smoke.ps1          — or —
//   THIDE_BLENDER_SMOKE=1 dotnet test tests/Therion.Blender.Tests -m:1 \
//       --filter FullyQualifiedName~GeneratedScriptBlenderSmoke
// Optional: THIDE_BLENDER_PATH=<exe> to pin a specific Blender build.
//
// On failure the assert message lists each failing case with its Python exception line, and
// every case's render.py + run.log stays under %TEMP%/thide-blender-smoke/<case>/ with a
// summary in %TEMP%/thide-blender-smoke/report.md — built for an agent/dev fix loop:
// patch ScriptGenerator → rerun → read the report.

using System.Diagnostics;
using System.Text;
using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Execution;
using Therion.Blender.Parsing;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Xunit.Abstractions;

namespace Therion.Blender.Tests;

public class GeneratedScriptBlenderSmokeTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("THIDE_BLENDER_SMOKE") == "1";

    private const int CaseTimeoutSeconds = 240;

    private readonly ITestOutputHelper _output;

    public GeneratedScriptBlenderSmokeTests(ITestOutputHelper output) => _output = output;

    private sealed record SmokeCase(string Name, SceneSpec Spec, ScriptPurpose Purpose, ScriptAssets? Assets = null);

    private sealed record CaseResult(string Name, bool Passed, string Detail, string CaseDir);

    [Fact]
    public async Task GeneratedScripts_RunCleanly_InRealBlender()
    {
        if (!Enabled) return; // opt-in only (needs a local Blender)

        var locate = new BlenderLocator(new ProcessBlenderProbe())
            .Locate(Environment.GetEnvironmentVariable("THIDE_BLENDER_PATH"));
        Assert.True(locate.IsUsable, $"THIDE_BLENDER_SMOKE=1 but no usable Blender was located: {locate.Detail}");
        string blender = locate.Installation!.Path;

        string root = Path.Combine(Path.GetTempPath(), "thide-blender-smoke");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        // One synthetic model, converted once via the real pipeline (PLY + meta + framing) —
        // the same assets the app would hand the generator. A lead on the first station makes
        // the lead-marker emitters non-empty.
        string loxPath = Path.Combine(root, "model.lox");
        await File.WriteAllBytesAsync(loxPath, LoxWriter.Write(LoxReaderTests.BuildSyntheticModel()));
        var source = new ResolvedModelSource { Path = loxPath, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        string assetDir = Path.Combine(root, "assets");
        var probe = CaveConversionPipeline.ConvertResolvedFull(source, new ConversionOptions { OutputDirectory = assetDir });
        var leads = probe.Meta.Stations.Count > 0
            ? new[] { new SourceLead(probe.Meta.Stations[0].Name, "continuation", "smoke lead") }
            : [];
        var conversion = CaveConversionPipeline.ConvertResolvedFull(
            source, new ConversionOptions { OutputDirectory = assetDir, Leads = leads });
        var framing = CameraFraming.FromGeometry(conversion.Geometry);

        var results = new List<CaseResult>();
        foreach (var smokeCase in BuildMatrix(conversion))
        {
            string caseDir = Path.Combine(root, smokeCase.Name);
            Directory.CreateDirectory(caseDir);
            var spec = smokeCase.Spec with
            {
                Source = smokeCase.Spec.Source with
                {
                    PlyPath = conversion.Manifest.ModelPath,
                    SceneMetaPath = conversion.Manifest.SceneMetaPath,
                },
                Output = smokeCase.Spec.Output with { OutputDirectory = caseDir },
            };
            string script = ScriptGenerator.Generate(spec, smokeCase.Assets, framing, conversion.Meta, smokeCase.Purpose);
            string scriptPath = Path.Combine(caseDir, "render.py");
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(false));

            var (exitCode, log) = await RunBlenderAsync(blender, scriptPath, caseDir);
            await File.WriteAllTextAsync(Path.Combine(caseDir, "run.log"), log);
            results.Add(Evaluate(smokeCase, spec, exitCode, log, caseDir));
        }

        string report = WriteReport(root, blender, locate.Installation.Version.ToString(), results);
        _output.WriteLine(report);

        var failed = results.Where(r => !r.Passed).ToList();
        Assert.True(failed.Count == 0,
            $"{failed.Count}/{results.Count} generated scripts failed in Blender {locate.Installation.Version} " +
            $"(full report: {Path.Combine(root, "report.md")}):\n" +
            string.Join("\n", failed.Select(f => $"  {f.Name}: {f.Detail}\n    logs: {f.CaseDir}")));
    }

    /// <summary>Every emitter branch at least once: the five shipped presets (planners, rigs,
    /// materials, events) build interactively; targeted cases cover the enum values and modes
    /// no preset uses; three tiny renders exercise the output/FFMPEG/collector paths.</summary>
    private static IEnumerable<SmokeCase> BuildMatrix(ConversionResult conversion)
    {
        foreach (var preset in BuiltInPresets.All)
        {
            string slug = preset.Name.ToLowerInvariant().Replace(' ', '-');
            yield return new SmokeCase($"preset-{slug}", preset.Spec, ScriptPurpose.Interactive);
        }

        // Flat rock + missing-HDRI rig + static camera + every label/overlay/event surface on.
        yield return new SmokeCase("labels-overlays-hdri", new SceneSpec
        {
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat },
            Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "missing.hdr" },
            Engine = new EngineSpec { Gpu = GpuMode.CpuOnly, Samples = 1 },
            Labels = new LabelsSpec
            {
                Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Named },
                Components = new ComponentLabelSpec { Show = true, MinStationCount = 1 },
                Leads = new LeadMarkerSpec { Show = true, Pulse = true, ShowText = true },
                Overlays = new OverlaySpec { Title = "Smoke \"title\"", ScaleBar = true, NorthArrow = true, DepthLegend = true },
                Events =
                [
                    new VisibilityEvent { Target = VisibilityTarget.StationLabels, ShowFrame = 1, FadeSeconds = 0.5 },
                    new VisibilityEvent { Target = VisibilityTarget.Overlays, HideFrame = 2 },
                ],
            },
            Output = new OutputSpec { Kind = OutputKind.Still, Width = 64, Height = 64, BaseName = "smoke" },
        }, ScriptPurpose.Interactive);

        // Per-survey vertex colours + EEVEE + self-contained embedded assets.
        yield return new SmokeCase("persurvey-eevee-embedded", new SceneSpec
        {
            Source = new SourceSpec { SelfContained = true, EmbedMesh = true },
            Materials = new MaterialsSpec { Rock = RockMaterial.PerSurvey },
            Engine = new EngineSpec { Kind = RenderEngineKind.Eevee, Samples = 4 },
            Output = new OutputSpec { Kind = OutputKind.Still, Width = 64, Height = 64, BaseName = "smoke" },
        }, ScriptPurpose.Interactive, new ScriptAssets
        {
            SceneMetaJson = File.ReadAllText(conversion.Manifest.SceneMetaPath),
            PlyBytes = File.ReadAllBytes(conversion.Manifest.ModelPath),
        });

        var tinyCycles = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly };

        yield return new SmokeCase("render-still-png", new SceneSpec
        {
            Engine = tinyCycles,
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Output = new OutputSpec { Kind = OutputKind.Still, Width = 64, Height = 64, BaseName = "still" },
        }, ScriptPurpose.Render);

        // Two-frame orbit → the video/FFMPEG path (Blender 5.x gates it behind media_type).
        yield return new SmokeCase("render-video-mp4", new SceneSpec
        {
            Engine = tinyCycles,
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
            Animation = new AnimationSpec { Fps = 4, DurationSeconds = 0.5 },
            Output = new OutputSpec { Kind = OutputKind.Video, Container = VideoContainer.Mp4, Width = 64, Height = 64, BaseName = "clip" },
        }, ScriptPurpose.Render);

        yield return new SmokeCase("render-frame-sequence", new SceneSpec
        {
            Engine = tinyCycles,
            Materials = new MaterialsSpec { Rock = RockMaterial.Flat },
            Lighting = new LightingSpec { Rig = LightingRig.ThreePoint },
            Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } },
            Animation = new AnimationSpec { Fps = 4, DurationSeconds = 0.5 },
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 64, Height = 64, BaseName = "frames" },
        }, ScriptPurpose.Render);
    }

    private static CaseResult Evaluate(SmokeCase smokeCase, SceneSpec spec, int exitCode, string log, string caseDir)
    {
        var problems = new List<string>();
        if (exitCode != 0) problems.Add($"exit code {exitCode}");
        if (log.Contains("Traceback (most recent call last)", StringComparison.Ordinal))
            problems.Add("python traceback: " + ExceptionLine(log));
        if (smokeCase.Purpose == ScriptPurpose.Render)
        {
            if (!log.Contains("THIDE:done=1", StringComparison.Ordinal)) problems.Add("no THIDE:done=1");
            bool anyOutput = Directory.EnumerateFiles(caseDir)
                .Any(f => Path.GetFileName(f).StartsWith(spec.Output.BaseName, StringComparison.Ordinal)
                          && !f.EndsWith(".py", StringComparison.Ordinal) && !f.EndsWith(".log", StringComparison.Ordinal));
            if (!anyOutput) problems.Add("no output file written");
        }
        else if (!log.Contains("THIDE:phase=interactive", StringComparison.Ordinal))
        {
            problems.Add("scene build did not reach the interactive epilogue");
        }
        return new CaseResult(smokeCase.Name, problems.Count == 0,
            problems.Count == 0 ? "ok" : string.Join("; ", problems), caseDir);
    }

    /// <summary>The first meaningful Python exception line (e.g. "TypeError: …"), for compact reports.</summary>
    private static string ExceptionLine(string log)
    {
        string[] lines = log.Split('\n');
        int start = Array.FindIndex(lines, l => l.StartsWith("Traceback", StringComparison.Ordinal));
        if (start < 0) return "?";
        // The exception line is the first non-indented line after the frames of the (last)
        // traceback block; Blender prints its own noise after it, so stop at the first hit.
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Length > 0 && !char.IsWhiteSpace(lines[i][0]))
                return line;
        }
        return lines[start].TrimEnd();
    }

    private static async Task<(int ExitCode, string Log)> RunBlenderAsync(string blender, string scriptPath, string workDir)
    {
        var psi = new ProcessStartInfo(blender)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        };
        // Same argv the runner uses, plus --python-exit-code so an uncaught Python exception
        // fails the process instead of exiting 0.
        foreach (var arg in new[] { "-b", "--factory-startup", "--python-exit-code", "64", "--python", scriptPath })
            psi.ArgumentList.Add(arg);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        var log = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CaseTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            lock (log) log.AppendLine($"[smoke] TIMEOUT after {CaseTimeoutSeconds}s — killed.");
            return (-2, log.ToString());
        }
        return (process.ExitCode, log.ToString());
    }

    private static string WriteReport(string root, string blender, string version, List<CaseResult> results)
    {
        var report = new StringBuilder();
        report.AppendLine($"# Blender smoke report — {DateTime.Now:yyyy-MM-dd HH:mm}");
        report.AppendLine($"Blender {version} at `{blender}`");
        report.AppendLine();
        foreach (var r in results)
            report.AppendLine($"- {(r.Passed ? "PASS" : "FAIL")} `{r.Name}` — {r.Detail}");
        string text = report.ToString();
        File.WriteAllText(Path.Combine(root, "report.md"), text);
        return text;
    }
}
