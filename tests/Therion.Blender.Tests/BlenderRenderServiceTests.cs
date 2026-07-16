// Render-service tests (G3 capstone): the convert → generate → (run) chain end to end, using
// the real av_cerbul .lox as the resolved source, a fake locator (a "usable" Blender), and a
// fake launcher (whose process writes the output file so collection succeeds). Export mode is
// verified without any Blender.

using System.Runtime.CompilerServices;
using Therion.Blender;
using Therion.Blender.Execution;
using Therion.Blender.Sources;

namespace Therion.Blender.Tests;

public class BlenderRenderServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-svc-" + Guid.NewGuid().ToString("N"));

    public BlenderRenderServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static RenderSource CorpusSource() => new(new ResolvedModelSource
    {
        Path = TestCorpus.AvCerbulLox(),
        Format = CaveSourceFormat.Lox,
        Kind = ModelSourceKind.ExternalFile,
    });

    private static SceneSpec OrbitVideoSpec() => new()
    {
        Name = "Service orbit",
        Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 1 } },
        Engine = new EngineSpec { Gpu = GpuMode.CpuOnly, Samples = 16 },
        Animation = new AnimationSpec { Fps = 1, DurationSeconds = 2 }, // 2 frames
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 64, Height = 64, BaseName = "render" },
    };

    // ---- fakes ----

    private sealed class FakeProbe(BlenderVersion? version) : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => version;
    }

    private static BlenderLocator Locator(BlenderVersion? version)
        => new(new FakeProbe(version), () => new[] { "/fake/blender" });

    private sealed class FakeProcess(string[] lines, int exitCode) : IBlenderProcess
    {
        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var line in lines) { ct.ThrowIfCancellationRequested(); yield return line; await Task.Yield(); }
        }
        public int ExitCode => exitCode;
        public void KillTree() { }
        public void Dispose() { }
    }

    private sealed class FakeLauncher(Action<string> onStart) : IBlenderProcessLauncher
    {
        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd)
        {
            onStart(cwd); // simulate Blender writing outputs into the job folder
            return new FakeProcess(["THIDE:device=CPU", "THIDE:frame=1/2", "THIDE:frame=2/2", "THIDE:done=1"], 0);
        }
    }

    // version = null ⇒ probe finds nothing (BlenderNotFound); otherwise that version is "installed".
    private BlenderRenderService Service(IBlenderProcessLauncher launcher, BlenderVersion? version)
        => new(Locator(version),
               new BlenderRunner(launcher, _ => 50_000_000_000L),
               Path.Combine(_root, "jobs"));

    private static readonly BlenderVersion Usable = new(4, 5, 0);

    // ---- export mode (no Blender) ----

    [CorpusFact]
    public async Task Export_WritesScriptAndSidecarAssets_ReferencingThem()
    {
        var service = Service(new FakeLauncher(_ => { }), Usable);
        var outDir = Path.Combine(_root, "export");
        var phases = new List<RenderPhase>();

        var scriptPath = await service.ExportScriptAsync(
            OrbitVideoSpec(), CorpusSource(), outDir, new Progress<RenderProgress>(p => phases.Add(p.Phase)));

        Assert.Equal(Path.Combine(outDir, "render.py"), scriptPath);
        Assert.True(File.Exists(scriptPath));
        Assert.True(File.Exists(Path.Combine(outDir, "model.ply")));
        Assert.True(File.Exists(Path.Combine(outDir, "scene-meta.json")));
        var script = await File.ReadAllTextAsync(scriptPath);
        Assert.Contains("model.ply", script);              // the generated script points at the sidecar
        Assert.Contains("CAM_KEYS", script);               // the orbit camera was framed from the geometry
        Assert.Contains(RenderPhase.ConvertingGeometry, phases);
        Assert.Contains(RenderPhase.GeneratingScript, phases);
    }

    [CorpusFact]
    public async Task Export_SelfContained_EmbedsTheMeta()
    {
        var spec = OrbitVideoSpec() with { Source = new SourceSpec { SelfContained = true } };
        var service = Service(new FakeLauncher(_ => { }), Usable);
        var scriptPath = await service.ExportScriptAsync(spec, CorpusSource(), Path.Combine(_root, "sc"));
        var script = await File.ReadAllTextAsync(scriptPath);
        Assert.Contains("SCENE_META_JSON = ", script);
    }

    // ---- render mode ----

    [CorpusFact]
    public async Task Render_ConvertsGeneratesRunsAndCollects()
    {
        // The fake launcher writes the video into the job folder so the collector finds it.
        var service = Service(new FakeLauncher(cwd => File.WriteAllBytes(Path.Combine(cwd, "render.mp4"), new byte[128])), Usable);
        var phases = new List<RenderPhase>();

        var result = await service.RenderAsync(
            OrbitVideoSpec(), CorpusSource(), new Progress<RenderProgress>(p => phases.Add(p.Phase)));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("CPU", result.Device);
        Assert.EndsWith("render.mp4", Assert.Single(result.OutputPaths));
        Assert.Contains(RenderPhase.ConvertingGeometry, phases);
        Assert.Contains(RenderPhase.Rendering, phases);
        Assert.Contains(RenderPhase.Done, phases);
    }

    [CorpusFact]
    public async Task Render_BlenderNotFound_FailsFast_WithoutRunning()
    {
        bool launched = false;
        var service = Service(new FakeLauncher(_ => launched = true), version: null); // probe finds nothing

        var result = await service.RenderAsync(OrbitVideoSpec(), CorpusSource());

        Assert.False(result.Succeeded);
        Assert.Equal(RenderFailureKind.BlenderNotFound, result.FailureKind);
        Assert.False(launched);
    }

    [CorpusFact]
    public async Task Render_BlenderTooOld_IsDistinct()
    {
        var service = Service(new FakeLauncher(_ => { }), version: new BlenderVersion(3, 6, 0));
        var result = await service.RenderAsync(OrbitVideoSpec(), CorpusSource());
        Assert.Equal(RenderFailureKind.BlenderTooOld, result.FailureKind);
    }
}
