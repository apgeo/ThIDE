// Per-template camera-script goldens (BA-B6 batch 3): each template compiles to a fixed
// Blender Python script (byte-exact, also under ro-RO culture — R-08), and every golden
// passes `python -m py_compile` when a Python is on PATH. Fixtures use fixed bounds/paths
// so goldens never depend on the host.
//
// Golden workflow: THIDE_UPDATE_GOLDENS=1 rewrites Goldens/*.py, then inspect + commit.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Geometry;

namespace Therion.Blender.Tests;

public class CameraScriptGoldenTests
{
    // A fixed local box + a fixed centerline route: goldens are host-independent.
    private static readonly BoundingBox Box = new(new CaveVector3(-40, -30, -20), new CaveVector3(60, 50, 20));

    private static readonly IReadOnlyList<CaveVector3> Route =
    [
        new(-30, -20, -10), new(-10, -5, -6), new(10, 5, -2), new(25, 15, 0), new(40, 30, 5),
    ];

    private static CameraFraming Framing(bool withPath = false)
        => new() { LocalBounds = Box, CenterlinePath = withPath ? Route : null };

    private static SceneSpec BaseSpec(CameraSpec camera, int fps, double seconds, OutputKind kind = OutputKind.Video) => new()
    {
        Name = "Golden camera",
        Seed = 5,
        Source = new SourceSpec { PlyPath = "assets/model.ply", SceneMetaPath = "assets/scene-meta.json" },
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 32, Gpu = GpuMode.CpuOnly },
        Camera = camera,
        Animation = new AnimationSpec { Fps = fps, DurationSeconds = seconds },
        Output = new OutputSpec
        {
            Kind = kind, Container = VideoContainer.Mp4,
            Width = 960, Height = 540, OutputDirectory = "out", BaseName = "golden-camera",
        },
    };

    private static (SceneSpec Spec, CameraFraming Framing) SpecFor(string which) => which switch
    {
        "orbit" => (BaseSpec(new CameraSpec
        {
            Template = CameraTemplate.Orbit, FocalLength = 35, AutoFramePadding = 1.2,
            Orbit = new OrbitParams { Revolutions = 1, ElevationDegrees = 20 },
        }, fps: 8, seconds: 2), Framing()),

        "helix" => (BaseSpec(new CameraSpec
        {
            Template = CameraTemplate.Helix,
            Helix = new HelixParams { Turns = 2, StartHeightFraction = 1, EndHeightFraction = 0, EndRadiusScale = 0.6 },
        }, fps: 8, seconds: 3), Framing()),

        "flythrough" => (BaseSpec(new CameraSpec
        {
            Template = CameraTemplate.Flythrough,
            Flythrough = new FlythroughParams { SmoothingIterations = 1, ClearanceMeters = 0, LookAheadMeters = 5 },
        }, fps: 8, seconds: 1), Framing(withPath: true)),

        "viewpoints" => (BaseSpec(new CameraSpec
        {
            Template = CameraTemplate.Viewpoints, FocalLength = 35,
            Dof = new DepthOfFieldSpec { Enabled = true, FStop = 2.0 }, // exercise the DOF block
            Viewpoints = new ViewpointParams
            {
                Easing = ViewpointEasing.Smooth,
                Viewpoints =
                [
                    new CameraViewpoint { Position = new(60, 0, 10), Target = new(10, 10, 0), HoldSeconds = 0.5 },
                    new CameraViewpoint { Position = new(0, 60, 20), Target = new(10, 10, 0), FocalLength = 50 },
                    new CameraViewpoint { Position = new(-40, 0, -10), Target = new(10, 10, 0) },
                ],
            },
        }, fps: 8, seconds: 3), Framing()),

        "still-set" => (BaseSpec(new CameraSpec
        {
            Template = CameraTemplate.StillSet,
            Stills = new StillSetParams { Views = [StandardView.Top, StandardView.Front, StandardView.Left, StandardView.IsoNE] },
        }, fps: 8, seconds: 1, kind: OutputKind.FrameSequence), Framing()),

        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    private static string Generate(string which)
    {
        var (spec, framing) = SpecFor(which);
        return ScriptGenerator.Generate(spec, assets: null, framing: framing);
    }

    [Theory]
    [InlineData("orbit")]
    [InlineData("helix")]
    [InlineData("flythrough")]
    [InlineData("viewpoints")]
    [InlineData("still-set")]
    public void Golden_ScriptsMatchByteExactly(string which)
    {
        var script = Generate(which);
        var goldenPath = GoldenPath($"camera-{which}.py");

        if (Environment.GetEnvironmentVariable("THIDE_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, script, new UTF8Encoding(false));
            return;
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
            invariant = Generate("helix"); // negative coords + fractional taper → real floats
            CultureInfo.CurrentCulture = new CultureInfo("ro-RO");
            romanian = Generate("helix");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
        Assert.Equal(invariant, romanian);
        // Tuple separators are always ", " (comma+space); a comma wedged directly between
        // two digits could only be a ro-RO decimal comma — there must be none.
        Assert.DoesNotMatch("[0-9],[0-9]", invariant);
    }

    [Fact]
    public void Keyframed_CameraStructureIsPresent()
    {
        var script = Generate("orbit");
        Assert.Contains("_track = cam.constraints.new(type='TRACK_TO')", script);
        Assert.Contains("_track.track_axis = 'TRACK_NEGATIVE_Z'", script);
        Assert.Contains("CAM_KEYS = [", script);
        Assert.Contains("cam.keyframe_insert(data_path=\"location\", frame=_k[0])", script);
        Assert.Contains("_kp.interpolation = \"LINEAR\"", script);
        Assert.Contains("scene.frame_end = 16", script); // 8 fps × 2 s
    }

    [Fact]
    public void StillSet_RendersOneFramePerView_AsASequence()
    {
        var script = Generate("still-set");
        Assert.Contains("scene.frame_end = 4", script);                 // Top/Front/Left/IsoNE
        Assert.Contains("_kp.interpolation = \"CONSTANT\"", script);    // discrete shots
        Assert.Contains("scene.render.image_settings.file_format = 'PNG'", script);
        Assert.Contains("bpy.ops.render.render(animation=True)", script);
    }

    [Fact]
    public void Viewpoints_EmitDofAndPerFrameFocal()
    {
        var script = Generate("viewpoints");
        Assert.Contains("cam_data.dof.use_dof = True", script);
        Assert.Contains("cam_data.dof.focus_object = cam_target", script);
        Assert.Contains("cam_data.keyframe_insert(data_path=\"lens\", frame=_k[0])", script);
        Assert.Contains("_kp.interpolation = \"BEZIER\"", script);
    }

    [Fact]
    public void GeneratedScripts_CompileAsPython_WhenPythonAvailable()
    {
        var python = FindPython();
        if (python is null) return; // no Python on PATH — documented no-op

        foreach (var which in new[] { "orbit", "helix", "flythrough", "viewpoints", "still-set" })
        {
            var script = Generate(which);
            var path = Path.Combine(Path.GetTempPath(), $"thide-camera-{which}-{Environment.ProcessId}.py");
            File.WriteAllText(path, script, new UTF8Encoding(false));
            try
            {
                var psi = new ProcessStartInfo(python, $"-m py_compile \"{path}\"")
                {
                    RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
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

    // ---- helpers ----

    private static string? FindPython()
    {
        foreach (var name in new[] { "python3", "python" })
        {
            try
            {
                var psi = new ProcessStartInfo(name, "--version")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
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
