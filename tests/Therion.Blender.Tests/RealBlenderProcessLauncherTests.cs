// The real launcher, with /bin/sh (or setpriv) standing in for Blender: its output and exit code
// come through, a process that is still running when it is disposed is ended with what it started,
// a program that is not there fails to start as before, and on Linux the process is started with a
// parent-death signal, so that it cannot outlive the process that started it.

using System.Diagnostics;
using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public sealed class RealBlenderProcessLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"thide-launch-{Guid.NewGuid():N}");

    public RealBlenderProcessLauncherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static async Task<List<string>> AllLines(IBlenderProcess process)
    {
        var lines = new List<string>();
        await foreach (var line in process.ReadLinesAsync(CancellationToken.None)) lines.Add(line);
        return lines;
    }

    [Fact]
    public async Task TheOutputAndTheExitCode_ComeThrough()
    {
        if (OperatingSystem.IsWindows()) return;
        using var process = new RealBlenderProcessLauncher().Start("/bin/sh", ["-c", "echo out; echo err >&2; exit 3"], _dir);
        var lines = await AllLines(process);
        Assert.Equal(["err", "out"], lines.Order(StringComparer.Ordinal));
        Assert.Equal(3, process.ExitCode);
    }

    [Fact]
    public async Task AProcessStillRunning_IsEnded_WithWhatItStarted_WhenDisposed()
    {
        if (OperatingSystem.IsWindows()) return;
        var process = new RealBlenderProcessLauncher().Start("/bin/sh", ["-c", "sleep 60 & echo $!; wait"], _dir);
        await using var lines = process.ReadLinesAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await lines.MoveNextAsync());
        int child = int.Parse(lines.Current, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(IsRunning(child));
        process.Dispose();
        for (int i = 0; i < 100 && IsRunning(child); i++) await Task.Delay(50);
        Assert.False(IsRunning(child), "what Blender started outlived it");
    }

    [Fact]
    public async Task OnLinux_TheProcessIsStartedWithAParentDeathSignal()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/setpriv")) return;
        // setpriv itself in Blender's place says what it was started with.
        using var process = new RealBlenderProcessLauncher().Start("/usr/bin/setpriv", ["--dump"], _dir);
        var lines = await AllLines(process);
        Assert.Contains(lines, l => l.StartsWith("Parent death signal:", StringComparison.Ordinal) && l.Contains("KILL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABlenderThatIsNotThere_FailsToStart_AsItDidBefore()
    {
        string missing = Path.Combine(_dir, "no-blender");
        var result = await new BlenderRunner(new RealBlenderProcessLauncher(), _ => null).RunAsync(
            new BlenderInstallation(missing, new BlenderVersion(4, 5, 0)),
            new RenderJob(Path.Combine(_dir, "render.py"), _dir, 1,
                new OutputSpec { Kind = OutputKind.Still, Width = 8, Height = 8, OutputDirectory = _dir, BaseName = "x" }));
        Assert.Equal(RenderFailureKind.Crashed, result.FailureKind);
        Assert.Equal(missing, result.StartFailedPath);
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;
            // A zombie is gone for every purpose but its exit code.
            return !OperatingSystem.IsLinux() || !File.ReadAllText($"/proc/{pid}/stat").Contains(") Z ", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { return false; }
    }
}
