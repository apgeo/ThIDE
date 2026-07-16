// Emitter-core tests (BA-B5): golden-file comparisons for canonical specs (byte-exact,
// also under ro-RO culture — R-08), determinism, THIDE protocol/structure asserts,
// hostile-input escaping (R-12), self-contained embedding, validation gating, and an
// opportunistic `python -m py_compile` syntax check when a Python is on PATH.
//
// Golden workflow: set THIDE_UPDATE_GOLDENS=1 and run to (re)write Goldens/*.py, then
// inspect the diff and commit. Normal runs compare byte-exactly.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Therion.Blender;
using Therion.Blender.Emit;

namespace Therion.Blender.Tests;

public class ScriptGeneratorTests
{
    // ---- canonical specs (fixed fake paths — goldens must never depend on the host) ----

    private static SceneSpec VideoSpec() => new()
    {
        Name = "Golden orbit video",
        CreatedBy = "ThIDE golden tests",
        Seed = 7,
        Source = new SourceSpec { PlyPath = "assets/model.ply", SceneMetaPath = "assets/scene-meta.json" },
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 96, Denoise = true, Gpu = GpuMode.Auto },
        Camera = new CameraSpec { FocalLength = 28, AutoFramePadding = 1.25 },
        Animation = new AnimationSpec { Fps = 24, DurationSeconds = 5 },
        Output = new OutputSpec
        {
            Kind = OutputKind.Video, Container = VideoContainer.Mp4,
            Width = 1280, Height = 720, OutputDirectory = "out", BaseName = "golden-video",
        },
    };

    private static SceneSpec StillSpec() => new()
    {
        Name = "Golden still — Peștera \"Test\"", // diacritics + quotes exercise escaping
        Seed = 3,
        Source = new SourceSpec { PlyPath = @"C:\caves\model.ply" },
        Engine = new EngineSpec
        {
            Kind = RenderEngineKind.Eevee, Samples = 32,
            TransparentBackground = true, Gpu = GpuMode.CpuOnly, // ignored for EEVEE
        },
        Output = new OutputSpec
        {
            Kind = OutputKind.Still, Width = 801, Height = 601, // odd is legal for stills
            OutputDirectory = "out/stills", BaseName = "golden-still",
        },
    };

    private static (SceneSpec Spec, ScriptAssets Assets) SelfContainedSpec()
    {
        var spec = new SceneSpec
        {
            Name = "Golden self-contained",
            Seed = 11,
            Source = new SourceSpec
            {
                PlyPath = "assets/model.ply", SceneMetaPath = "assets/scene-meta.json",
                SelfContained = true, EmbedMesh = true,
            },
            Engine = new EngineSpec { Gpu = GpuMode.Cuda, Samples = 16 },
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 1 },
            Output = new OutputSpec
            {
                Kind = OutputKind.FrameSequence, Width = 640, Height = 480,
                OutputDirectory = "out", BaseName = "golden-frames",
            },
        };
        var assets = new ScriptAssets
        {
            // Diacritics + a JSON-escaped quote: both must survive into the literal.
            SceneMetaJson = """{"version":1,"stations":[{"name":"Peștera \"T\""}]}""",
            PlyBytes = [0x70, 0x6C, 0x79, 0x0A, 0x00, 0xFF], // tiny fixed bytes
        };
        return (spec, assets);
    }

    // ---- goldens ----

    [Theory]
    [InlineData("video")]
    [InlineData("still")]
    [InlineData("self-contained")]
    [InlineData("interactive")]
    public void Golden_ScriptsMatchByteExactly(string which)
    {
        var script = GenerateByName(which);
        var goldenPath = GoldenPath($"{which}.py");

        if (Environment.GetEnvironmentVariable("THIDE_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, script, new UTF8Encoding(false));
            return; // updated — inspect the diff and commit
        }

        Assert.True(File.Exists(goldenPath),
            $"Golden file missing: {goldenPath}. Run once with THIDE_UPDATE_GOLDENS=1 to seed it.");
        Assert.Equal(File.ReadAllText(goldenPath).ReplaceLineEndings("\n"), script);
    }

    [Fact]
    public void Generation_IsByteIdentical_UnderRoRoCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        string invariant, romanian;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariant = ScriptGenerator.Generate(VideoSpec());
            CultureInfo.CurrentCulture = new CultureInfo("ro-RO");
            romanian = ScriptGenerator.Generate(VideoSpec());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        Assert.Equal(invariant, romanian);
        Assert.Contains("cam_data.lens = 28", invariant);        // a real float made it through
        Assert.DoesNotContain("1,25", invariant);                // and never with a decimal comma
    }

    [Fact]
    public void Generation_IsDeterministic()
    {
        Assert.Equal(ScriptGenerator.Generate(VideoSpec()), ScriptGenerator.Generate(VideoSpec()));
        var (spec, assets) = SelfContainedSpec();
        Assert.Equal(ScriptGenerator.Generate(spec, assets), ScriptGenerator.Generate(spec, assets));
    }

    // ---- structure ----

    [Fact]
    public void Script_CarriesVersionGateHashAndProtocol()
    {
        var spec = VideoSpec();
        var script = ScriptGenerator.Generate(spec);

        Assert.Contains($"SPEC_HASH = \"{SceneSpecSerializer.ComputeHash(spec)}\"", script);
        Assert.Contains("if bpy.app.version < (4, 2, 0):", script);
        Assert.Contains("bpy.ops.wm.read_factory_settings(use_empty=True)", script);
        Assert.Contains("bpy.ops.wm.ply_import(filepath=PLY_PATH)", script);
        Assert.Contains("thide(\"phase\", \"render\")", script);
        Assert.Contains("thide(\"done\", \"1\")", script);
        Assert.Contains("scene.frame_end = 120", script); // 24 fps × 5 s
        Assert.Contains("scene.cycles.seed = SEED", script);
    }

    [Fact]
    public void CyclesAuto_EmitsFullGpuCascade_InOrder()
    {
        var script = ScriptGenerator.Generate(VideoSpec());
        Assert.Contains("_gpu_cascade = [\"OPTIX\", \"CUDA\", \"HIP\", \"ONEAPI\", \"METAL\"]", script);
        Assert.Contains("scene.cycles.device = 'CPU'", script); // the fallback arm
        Assert.Contains("thide(\"device\", _kind)", script);
    }

    [Fact]
    public void CyclesSpecificGpu_EmitsSingleBackendCascade()
    {
        var (spec, assets) = SelfContainedSpec(); // uses Cuda
        var script = ScriptGenerator.Generate(spec, assets);
        Assert.Contains("_gpu_cascade = [\"CUDA\"]", script);
    }

    [Fact]
    public void CyclesCpuOnly_SkipsCascadeEntirely()
    {
        var spec = VideoSpec() with { Engine = VideoSpec().Engine with { Gpu = GpuMode.CpuOnly } };
        var script = ScriptGenerator.Generate(spec);
        Assert.DoesNotContain("_gpu_cascade", script);
        Assert.Contains("scene.cycles.device = 'CPU'", script);
        Assert.Contains("thide(\"device\", \"CPU\")", script);
    }

    [Fact]
    public void Eevee_ProbesEngineIdByAssignment_AndSkipsDeviceConfig()
    {
        var script = ScriptGenerator.Generate(StillSpec());
        Assert.Contains("if thide_enum(scene.render, \"engine\", (\"BLENDER_EEVEE_NEXT\", \"BLENDER_EEVEE\")) is None:", script);
        Assert.Contains("scene.eevee.taa_render_samples = 32", script);
        Assert.DoesNotContain("_gpu_cascade", script);
        Assert.DoesNotContain("cycles", script.Replace("# ", "")); // no cycles config for EEVEE
        Assert.Contains("scene.render.film_transparent = True", script);
        Assert.Contains("color_mode = \"RGBA\"", script); // transparent still → RGBA PNG
    }

    [Fact]
    public void HostileText_IsAlwaysEscaped()
    {
        var spec = VideoSpec() with
        {
            Name = "evil\nname",
            Source = new SourceSpec { PlyPath = "a\"; import os; os.system(\"rm -rf /\")\".ply" },
        };
        var script = ScriptGenerator.Generate(spec);

        // The injection attempt survives only as an escaped string literal.
        Assert.Contains("PLY_PATH = \"a\\\"; import os; os.system(\\\"rm -rf /\\\")\\\".ply\"", script);
        // The multi-line name was flattened in the header comment (no stray line).
        Assert.Contains("# Spec: evil name", script);
    }

    [Fact]
    public void WindowsPath_BackslashesAreEscaped()
    {
        var script = ScriptGenerator.Generate(StillSpec());
        Assert.Contains(@"PLY_PATH = ""C:\\caves\\model.ply""", script);
    }

    [Fact]
    public void SelfContained_EmbedsMetaAndMesh()
    {
        var (spec, assets) = SelfContainedSpec();
        var script = ScriptGenerator.Generate(spec, assets);

        Assert.Contains("SCENE_META_JSON = ", script);
        Assert.Contains("Peștera", script); // meta JSON content made it into the literal, diacritics raw
        // The JSON's own `\"` doubles when escaped into a Python literal: \\\" on disk.
        Assert.Contains("\\\\\\\"T\\\\\\\"", script);
        Assert.Contains($"PLY_BASE64 = \"{Convert.ToBase64String(assets.PlyBytes!)}\"", script);
        Assert.Contains("tempfile.NamedTemporaryFile", script);
        Assert.Contains("base64.b64decode(PLY_BASE64)", script);
        Assert.DoesNotContain("PLY_PATH = \"assets/model.ply\"", script); // imports the decoded temp file
    }

    [Fact]
    public void Interactive_BuildsSceneButSkipsOutputAndRender()
    {
        var render = ScriptGenerator.Generate(VideoSpec());
        var interactive = ScriptGenerator.Generate(VideoSpec(), purpose: ScriptPurpose.Interactive);

        // Same scene construction (model + camera) …
        Assert.Contains("bpy.ops.wm.ply_import(filepath=PLY_PATH)", interactive);
        Assert.Contains("scene.camera = cam", interactive);
        // … but no output target, no progress hooks, and — crucially — no render call, so opening
        // it in the GUI does not kick off a headless render (BA-B13).
        Assert.DoesNotContain("bpy.ops.render.render", interactive);
        Assert.DoesNotContain("image_settings.file_format", interactive);
        Assert.DoesNotContain("thide(\"done\"", interactive);
        // It frames the model through the render camera so the scene is visible on open.
        Assert.Contains("view_perspective = 'CAMERA'", interactive);
        Assert.Contains("thide(\"phase\", \"interactive\")", interactive);
        // The render variant of course still renders.
        Assert.Contains("bpy.ops.render.render(animation=True)", render);
    }

    [Fact]
    public void Video_FallsBackToPngFrames_WhenBuildHasNoFfmpeg()
    {
        var script = ScriptGenerator.Generate(VideoSpec());
        // Probe by ASSIGNMENT rather than enum_items (R-07) — 5.x still lists FFMPEG there
        // while rejecting the assignment until media_type is VIDEO; builds without the
        // FFMPEG movie writer must degrade to PNG frames, not crash.
        Assert.Contains("thide_enum(_img, \"media_type\", (\"VIDEO\",))", script);
        Assert.Contains("if thide_enum(_img, \"file_format\", (\"FFMPEG\",)) is not None:", script);
        Assert.Contains("_img.file_format = 'PNG'", script);
        Assert.Contains("no FFMPEG video encoder", script);
    }

    // ---- gating ----

    [Fact]
    public void InvalidSpec_FailsFast_WithFieldPath()
    {
        var spec = VideoSpec() with { Engine = VideoSpec().Engine with { Samples = 0 } };
        var ex = Assert.Throws<ArgumentException>(() => ScriptGenerator.Generate(spec));
        Assert.Contains("engine.samples", ex.Message);
    }

    [Fact]
    public void SelfContained_WithoutAssets_Throws()
    {
        var (spec, _) = SelfContainedSpec();
        Assert.Throws<ArgumentException>(() => ScriptGenerator.Generate(spec));
        Assert.Throws<ArgumentException>(
            () => ScriptGenerator.Generate(spec, new ScriptAssets { SceneMetaJson = "{}" })); // mesh still missing
    }

    // ---- opportunistic syntax check (TESTING.md: safety net without Blender) ----

    [Fact]
    public void GeneratedScripts_CompileAsPython_WhenPythonAvailable()
    {
        var python = FindPython();
        if (python is null) return; // no Python on PATH — silently satisfied (documented)

        foreach (var which in new[] { "video", "still", "self-contained", "interactive" })
        {
            var script = GenerateByName(which);
            var path = Path.Combine(Path.GetTempPath(), $"thide-golden-{which}-{Environment.ProcessId}.py");
            File.WriteAllText(path, script, new UTF8Encoding(false));
            try
            {
                var psi = new ProcessStartInfo(python, $"-m py_compile \"{path}\"")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(psi)!;
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(30_000);
                Assert.True(process.ExitCode == 0, $"py_compile failed for {which}: {stderr}");
            }
            finally
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo(name, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using var process = Process.Start(psi);
                if (process is null) continue;
                process.WaitForExit(10_000);
                if (process.ExitCode == 0) return name;
            }
            catch
            {
                // not on PATH — try the next candidate
            }
        }
        return null;
    }

    // ---- helpers ----

    private static string GenerateByName(string which) => which switch
    {
        "video" => ScriptGenerator.Generate(VideoSpec()),
        "still" => ScriptGenerator.Generate(StillSpec()),
        "self-contained" => GenerateSelfContained(),
        "interactive" => ScriptGenerator.Generate(VideoSpec(), purpose: ScriptPurpose.Interactive),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    private static string GenerateSelfContained()
    {
        var (spec, assets) = SelfContainedSpec();
        return ScriptGenerator.Generate(spec, assets);
    }

    private static string GoldenPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Therion.Blender.Tests", "Goldens");
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "Therion.Blender.Tests")))
                return Path.Combine(candidate, fileName);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate tests/Therion.Blender.Tests above the test output directory.");
    }
}
