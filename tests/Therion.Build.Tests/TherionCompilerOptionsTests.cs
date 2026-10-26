using System.Diagnostics;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace Therion.Build.Tests;

// The process seam lets these tests see the start info Therion would get and run a harmless
// command in its place, so no Therion installation is needed.
public class TherionCompilerOptionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thc_" + Guid.NewGuid().ToString("N"));

    public TherionCompilerOptionsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private sealed class FixedLocator : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ToolInfo?>(new ToolInfo(toolId, "/opt/fake/therion", "6.3.4", "test"));
    }

    // Records what the compiler built, then runs `dotnet --version` (present wherever the tests run).
    private static TherionCompileOptions Capturing(List<ProcessStartInfo> seen, TherionCompileOptions? baseOptions = null)
        => (baseOptions ?? TherionCompileOptions.Default) with
        {
            ConfigureProcess = psi =>
            {
                seen.Add(psi);
                var replacement = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = psi.WorkingDirectory,
                    RedirectStandardOutput = psi.RedirectStandardOutput,
                    RedirectStandardError = psi.RedirectStandardError,
                    RedirectStandardInput = psi.RedirectStandardInput,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                replacement.ArgumentList.Add("--version");
                return replacement;
            },
        };

    [Fact]
    public async Task Default_run_passes_only_the_full_entry_path_and_runs_in_its_directory()
    {
        var seen = new List<ProcessStartInfo>();
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");

        var result = await new TherionCompiler(new FixedLocator()).CompileAsync(entry, Capturing(seen));

        var psi = Assert.Single(seen);
        Assert.Equal("/opt/fake/therion", psi.FileName);
        Assert.Equal(new[] { entry }, psi.ArgumentList);
        Assert.Equal(_dir, psi.WorkingDirectory);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.LogPath);   // the stand-in wrote no log
    }

    [Fact]
    public async Task A_relative_entry_is_resolved_to_a_full_path()
    {
        var seen = new List<ProcessStartInfo>();
        File.WriteAllText(Path.Combine(_dir, "thconfig"), "");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(_dir, "thconfig"));

        await new TherionCompiler(new FixedLocator()).CompileAsync(relative, Capturing(seen));

        Assert.Equal(Path.Combine(_dir, "thconfig"), Assert.Single(seen).ArgumentList.Last());
    }

    [Fact]
    public async Task Quiet_and_log_file_become_flags_with_the_log_resolved_in_the_working_directory()
    {
        var seen = new List<ProcessStartInfo>();
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");

        await new TherionCompiler(new FixedLocator())
            .CompileAsync(entry, Capturing(seen, new TherionCompileOptions { Quiet = true, LogFile = "out/run.log" }));

        Assert.Equal(new[] { "-q", "-l", Path.Combine(_dir, "out", "run.log"), entry }, Assert.Single(seen).ArgumentList);
    }

    [Fact]
    public async Task Constructor_options_apply_to_the_interface_call()
    {
        var seen = new List<ProcessStartInfo>();
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");
        ITherionCompiler compiler = new TherionCompiler(new FixedLocator(),
            defaults: Capturing(seen, new TherionCompileOptions { Quiet = true }));

        await compiler.CompileAsync(entry);

        Assert.Equal("-q", Assert.Single(seen).ArgumentList[0]);
    }

    // Turns the compiler's start info into a shell running `script`, keeping its environment and
    // redirections. Therion's arguments follow as the script's positional parameters ($1, $2 …).
    private static ProcessStartInfo AsShell(ProcessStartInfo psi, string unixScript, string windowsScript)
    {
        var args = psi.ArgumentList.ToList();
        psi.FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        psi.ArgumentList.Clear();
        if (OperatingSystem.IsWindows())
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(windowsScript);
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(unixScript);
            psi.ArgumentList.Add("sh");
        }
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private sealed class Lines : IProgress<CompilerOutputLine>
    {
        public List<string> Seen { get; } = new();
        public void Report(CompilerOutputLine value) { lock (Seen) Seen.Add(value.Text); }
    }

    [Fact]
    public async Task The_seam_can_set_the_environment_of_the_started_process()
    {
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");
        var options = new TherionCompileOptions
        {
            ConfigureProcess = psi =>
            {
                psi.Environment["TMPDIR"] = _dir;
                return AsShell(psi, "echo \"TMPDIR=$TMPDIR\"", "echo TMPDIR=%TMPDIR%");
            },
        };
        var lines = new Lines();

        var result = await new TherionCompiler(new FixedLocator()).CompileAsync(entry, options, lines);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains($"TMPDIR={_dir}", lines.Seen);
    }

    // `-l <path>` is the second and third argument when only LogFile is set.
    private static TherionCompileOptions WritingLog(string unixScript, string windowsScript) => new()
    {
        LogFile = "run.log",
        ConfigureProcess = psi => AsShell(psi, unixScript, windowsScript),
    };

    [Fact]
    public async Task A_log_the_run_writes_is_named_in_the_result()
    {
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");

        var result = await new TherionCompiler(new FixedLocator())
            .CompileAsync(entry, WritingLog("echo x > \"$2\"", $"echo x > \"{Path.Combine(_dir, "run.log")}\""));

        Assert.Equal(Path.Combine(_dir, "run.log"), result.LogPath);
    }

    [Fact]
    public async Task A_fresh_log_with_a_time_before_the_start_still_counts()
    {
        // A file system with coarse timestamps can give a just-written file a time before the start.
        if (OperatingSystem.IsWindows()) return;
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");

        var result = await new TherionCompiler(new FixedLocator())
            .CompileAsync(entry, WritingLog("echo x > \"$2\" && touch -t 200001010000 \"$2\"", ""));

        Assert.Equal(Path.Combine(_dir, "run.log"), result.LogPath);
    }

    [Fact]
    public async Task A_leftover_log_the_run_did_not_touch_is_not_named()
    {
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");
        var log = Path.Combine(_dir, "run.log");
        File.WriteAllText(log, "from an earlier run");
        // Stamped after the start, as a coarse clock or a run a moment earlier can leave it.
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddMinutes(5));

        var result = await new TherionCompiler(new FixedLocator()).CompileAsync(entry, WritingLog("true", "rem"));

        Assert.Null(result.LogPath);
    }
}

public class OutputArtifactCollectorKindsTests
{
    [Theory]
    [InlineData(".sql")] [InlineData(".csv")] [InlineData(".plt")] [InlineData(".prj")]
    [InlineData(".shx")] [InlineData(".wrl")] [InlineData(".3dmf")] [InlineData(".xhtml")]
    public void Collects_database_compass_vrml_and_shapefile_side_files(string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), "thk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "rez", "cave-shp"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "rez", "cave-shp", "cave" + extension), "x");
            Assert.Single(new OutputArtifactCollector().Collect(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
