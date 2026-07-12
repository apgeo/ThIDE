// Runner tests (BA-B10 batch 3): the failure taxonomy (success / script-error / crash /
// cancel / disk), job.log capture, argument shape, and progress reporting — all with a fake
// launcher so no real Blender is needed. Plus the pure disk-preflight estimate.

using System.Runtime.CompilerServices;
using Therion.Blender;
using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public class BlenderRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-run-" + Guid.NewGuid().ToString("N"));

    public BlenderRunnerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static readonly BlenderInstallation Blender = new("/opt/blender/blender", new BlenderVersion(4, 5, 0));

    private RenderJob Job(int frames = 2, OutputKind kind = OutputKind.Video, string baseName = "cave")
        => new(Path.Combine(_dir, "render.py"), _dir, frames,
            new OutputSpec { Kind = kind, Container = VideoContainer.Mp4, Width = 640, Height = 480, OutputDirectory = _dir, BaseName = baseName });

    private void WriteOutput(string name, int bytes = 16) => File.WriteAllBytes(Path.Combine(_dir, name), new byte[bytes]);

    private sealed class FakeProcess(string[] lines, int exitCode, int delayMs = 0) : IBlenderProcess
    {
        public bool Killed { get; private set; }

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var line in lines)
            {
                if (delayMs > 0) await Task.Delay(delayMs, ct);
                ct.ThrowIfCancellationRequested();
                yield return line;
            }
        }

        public int ExitCode => Killed ? -1 : exitCode;
        public void KillTree() => Killed = true;
        public void Dispose() { }
    }

    private sealed class FakeLauncher(Func<IBlenderProcess> factory) : IBlenderProcessLauncher
    {
        public IReadOnlyList<string>? Args { get; private set; }
        public string? Exe { get; private set; }
        public IBlenderProcess? Last { get; private set; }

        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd)
        {
            Exe = exe;
            Args = args;
            Last = factory();
            return Last;
        }
    }

    private BlenderRunner Runner(Func<IBlenderProcess> factory, long? free = 10_000_000_000L)
        => new(new FakeLauncher(factory), _ => free);

    // ---- happy path ----

    [Fact]
    public async Task Success_ReportsDoneDeviceAndCollectsOutput_AndWritesJobLog()
    {
        WriteOutput("cave.mp4"); // simulate the file Blender wrote
        var lines = new[]
        {
            "THIDE:phase=scene", "THIDE:phase=import", "THIDE:device=OPTIX",
            "THIDE:frame=1/2", "THIDE:frame=2/2", "THIDE:output=" + Path.Combine(_dir, "cave.mp4"), "THIDE:done=1",
        };
        var ticks = new List<RenderProgress>();
        var runner = Runner(() => new FakeProcess(lines, exitCode: 0));

        var result = await runner.RunAsync(Blender, Job(), new Progress<RenderProgress>(ticks.Add));

        Assert.True(result.Succeeded);
        Assert.Equal(RenderFailureKind.None, result.FailureKind);
        Assert.Equal("OPTIX", result.Device);
        Assert.Equal(Path.Combine(_dir, "cave.mp4"), Assert.Single(result.OutputPaths)); // from the collector
        Assert.Contains(ticks, t => t.Phase == RenderPhase.CollectingOutputs);
        Assert.True(File.Exists(Path.Combine(_dir, "job.log")));
        Assert.Contains(File.ReadAllLines(Path.Combine(_dir, "job.log")), l => l == "THIDE:done=1");
    }

    [Fact]
    public async Task DoneButNoFileOnDisk_IsNoOutput()
    {
        var runner = Runner(() => new FakeProcess(["THIDE:done=1"], 0)); // no file written
        var result = await runner.RunAsync(Blender, Job(), null);
        Assert.False(result.Succeeded);
        Assert.Equal(RenderFailureKind.NoOutput, result.FailureKind);
    }

    [Fact]
    public async Task FrameSequence_CollectsEveryFrame()
    {
        WriteOutput("seq_0001.png"); WriteOutput("seq_0002.png");
        var runner = Runner(() => new FakeProcess(["THIDE:frame=1/2", "THIDE:frame=2/2", "THIDE:done=1"], 0));
        var result = await runner.RunAsync(Blender, Job(frames: 2, kind: OutputKind.FrameSequence, baseName: "seq"), null);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.OutputPaths.Length);
    }

    [Fact]
    public async Task Launches_WithHeadlessFactoryStartupArgs()
    {
        var launcher = new FakeLauncher(() => new FakeProcess(["THIDE:done=1"], 0));
        var runner = new BlenderRunner(launcher, _ => 10_000_000_000L);
        await runner.RunAsync(Blender, Job(), null);

        Assert.Equal(Blender.Path, launcher.Exe);
        Assert.Equal(["-b", "--factory-startup", "--python", Path.Combine(_dir, "render.py")], launcher.Args);
    }

    // ---- failure taxonomy ----

    [Fact]
    public async Task ScriptError_IsClassified_WithTheMessage()
    {
        var lines = new[] { "THIDE:phase=import", "THIDE:error=PLY import produced no object" };
        var result = await Runner(() => new FakeProcess(lines, exitCode: 64)).RunAsync(Blender, Job(), null);

        Assert.False(result.Succeeded);
        Assert.Equal(RenderFailureKind.ScriptError, result.FailureKind);
        Assert.Equal("PLY import produced no object", result.ErrorMessage);
    }

    [Fact]
    public async Task NonzeroExit_WithoutDone_IsACrash_WithTail()
    {
        var lines = new[] { "Traceback (most recent call last):", "  File ...", "Segmentation fault" };
        var result = await Runner(() => new FakeProcess(lines, exitCode: 139)).RunAsync(Blender, Job(), null);

        Assert.False(result.Succeeded);
        Assert.Equal(RenderFailureKind.Crashed, result.FailureKind);
        Assert.Contains("139", result.ErrorMessage);
        Assert.Contains("Segmentation fault", result.ErrorMessage);
    }

    [Fact]
    public async Task Cancellation_KillsTheProcessTree_AndReportsCancelled()
    {
        var lines = new[] { "THIDE:phase=render", "THIDE:frame=1/100", "THIDE:frame=2/100", "THIDE:frame=3/100" };
        var launcher = new FakeLauncher(() => new FakeProcess(lines, exitCode: 0, delayMs: 50));
        var runner = new BlenderRunner(launcher, _ => 10_000_000_000L);
        using var cts = new CancellationTokenSource(60); // cancel mid-stream

        var result = await runner.RunAsync(Blender, Job(frames: 100), null, cts.Token);

        Assert.Equal(RenderFailureKind.Cancelled, result.FailureKind);
        Assert.False(result.Succeeded);
        Assert.True(((FakeProcess)launcher.Last!).Killed);
    }

    [Fact]
    public async Task DiskPreflight_FailsFast_WhenSpaceIsShort()
    {
        // Huge frame sequence, almost no free space → refuse before launching.
        bool launched = false;
        var runner = new BlenderRunner(
            new FakeLauncher(() => { launched = true; return new FakeProcess(["THIDE:done=1"], 0); }),
            _ => 1_000_000L); // 1 MB free
        var result = await runner.RunAsync(Blender, Job(frames: 500, kind: OutputKind.FrameSequence), null);

        Assert.Equal(RenderFailureKind.DiskSpace, result.FailureKind);
        Assert.False(launched);
    }

    [Fact]
    public async Task UnknownFreeSpace_ProceedsAnyway()
    {
        WriteOutput("cave.mp4");
        var runner = new BlenderRunner(new FakeLauncher(() => new FakeProcess(["THIDE:done=1"], 0)), _ => null);
        var result = await runner.RunAsync(Blender, Job(), null);
        Assert.True(result.Succeeded);
    }

    // ---- pure disk estimate ----

    [Fact]
    public void Estimate_VideoIsLighterThanFrameSequence()
    {
        long video = DiskPreflight.EstimateBytes(240, 1920, 1080, OutputKind.Video);
        long frames = DiskPreflight.EstimateBytes(240, 1920, 1080, OutputKind.FrameSequence);
        Assert.True(frames > video);
        Assert.True(video > 0);
    }

    [Fact]
    public void IsSufficient_AppliesSafetyFactor()
    {
        Assert.False(DiskPreflight.IsSufficient(estimatedBytes: 1000, freeBytes: 1200)); // < 1.5×
        Assert.True(DiskPreflight.IsSufficient(estimatedBytes: 1000, freeBytes: 2000));
    }
}
