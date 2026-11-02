// The render service must honour the output directory the caller put in the spec
// ("Directory the render writes into") and must not leave its scratch transport mesh
// behind under the job root once the render has finished.

using System.Runtime.CompilerServices;
using Therion.Blender.Execution;
using Therion.Blender.Sources;

namespace Therion.Blender.Tests;

public class RenderOutputDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thide-outdir-" + Guid.NewGuid().ToString("N"));

    public RenderOutputDirectoryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    // ---- fakes ----

    private sealed class FakeProbe : IBlenderProbe
    {
        public BlenderVersion? Probe(string path) => new(4, 5, 0);
    }

    private sealed class FakeProcess(int exitCode) : IBlenderProcess
    {
        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var line in new[] { "THIDE:device=CPU", "THIDE:frame=1/2", "THIDE:frame=2/2", "THIDE:done=1" })
            {
                ct.ThrowIfCancellationRequested();
                yield return line;
                await Task.Yield();
            }
        }
        public int ExitCode => exitCode;
        public void KillTree() { }
        public void Dispose() { }
    }

    /// <summary>Simulates Blender writing the video into the given directories (the job folder
    /// it is started in, and the directory the spec asked for) so output collection succeeds
    /// whichever one the service renders into.</summary>
    private sealed class FakeLauncher(IEnumerable<string> dirs) : IBlenderProcessLauncher
    {
        public IBlenderProcess Start(string exe, IReadOnlyList<string> args, string cwd)
        {
            foreach (var dir in dirs.Append(cwd).Distinct())
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "render.mp4"), new byte[128]);
            }
            return new FakeProcess(0);
        }
    }

    private RenderSource SyntheticSource()
    {
        var bytes = Survex3dFixtureBuilder.V8("cave", "EPSG:31700", '.')
            .V8Label(0, 0, "cave.1", 0, 0, 0)
            .V8Label(0, 1, "2", 10, 0, 0)
            .V8Style(0x00)
            .V8Move(0, 0, 0)
            .V8Line(0, (6, "cave"), 10, 0, 0)
            .V8Stop()
            .Build();
        string path = Path.Combine(_root, "cave.3d");
        File.WriteAllBytes(path, bytes);
        return new RenderSource(new ResolvedModelSource { Path = path, Format = CaveSourceFormat.Survex3d, Kind = ModelSourceKind.ExternalFile }, []);
    }

    private static SceneSpec VideoSpec(string outputDirectory) => new()
    {
        Name = "Output directory",
        Camera = new CameraSpec { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 1 } },
        Engine = new EngineSpec { Gpu = GpuMode.CpuOnly, Samples = 16 },
        Animation = new AnimationSpec { Fps = 1, DurationSeconds = 2 },
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 64, Height = 64, BaseName = "render", OutputDirectory = outputDirectory },
    };

    private BlenderRenderService Service(string wanted) => new(
        new BlenderLocator(new FakeProbe(), () => new[] { "/fake/blender" }),
        new BlenderRunner(new FakeLauncher([wanted]), _ => 50_000_000_000L),
        Path.Combine(_root, "jobs"));

    [Fact]
    public async Task Render_WritesOutputsIntoTheSpecifiedOutputDirectory()
    {
        string wanted = Path.Combine(_root, "wanted");
        var result = await Service(wanted).RenderAsync(VideoSpec(wanted), SyntheticSource());

        Assert.True(result.Succeeded, result.ErrorMessage);
        string output = Assert.Single(result.OutputPaths);
        Assert.StartsWith(wanted + Path.DirectorySeparatorChar, output);
    }

    [Fact]
    public async Task Render_LeavesNoScratchMeshUnderTheJobRoot_AfterSuccess()
    {
        string wanted = Path.Combine(_root, "wanted");
        var result = await Service(wanted).RenderAsync(VideoSpec(wanted), SyntheticSource());

        Assert.True(result.Succeeded, result.ErrorMessage);
        var leftovers = Directory.GetFiles(Path.Combine(_root, "jobs"), "model.ply", SearchOption.AllDirectories);
        Assert.True(leftovers.Length == 0, "scratch transport mesh left under the job root: " + string.Join(", ", leftovers));
    }
    [Fact]
    public async Task Render_WithoutOutputDirectory_KeepsOutputsAndLogInTheJobFolder_AndDropsScratch()
    {
        var result = await Service(Path.Combine(_root, "unused")).RenderAsync(VideoSpec(""), SyntheticSource());

        Assert.True(result.Succeeded, result.ErrorMessage);
        string output = Assert.Single(result.OutputPaths);
        string jobDir = Path.GetDirectoryName(output)!;
        Assert.StartsWith(Path.Combine(_root, "jobs") + Path.DirectorySeparatorChar, jobDir);
        Assert.True(File.Exists(output));
        Assert.True(File.Exists(Path.Combine(jobDir, "job.log")));
        Assert.False(File.Exists(Path.Combine(jobDir, "model.ply")));
        Assert.False(File.Exists(Path.Combine(jobDir, "scene-meta.json")));
        Assert.False(File.Exists(Path.Combine(jobDir, "render.py")));
    }

    [Fact]
    public async Task Render_DiskPreflight_ProbesTheOutputDirectory()
    {
        string wanted = Path.Combine(_root, "wanted");
        var probed = new List<string>();
        var service = new BlenderRenderService(
            new BlenderLocator(new FakeProbe(), () => new[] { "/fake/blender" }),
            new BlenderRunner(new FakeLauncher([wanted]), p => { probed.Add(p); return 50_000_000_000L; }),
            Path.Combine(_root, "jobs"));

        var result = await service.RenderAsync(VideoSpec(wanted), SyntheticSource());

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal([wanted], probed);
    }
}
