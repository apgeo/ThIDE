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

    [Fact]
    public async Task The_seam_can_set_the_environment()
    {
        var seen = new List<ProcessStartInfo>();
        var entry = Path.Combine(_dir, "thconfig");
        File.WriteAllText(entry, "");
        var options = new TherionCompileOptions
        {
            ConfigureProcess = psi => { psi.Environment["TMPDIR"] = _dir; seen.Add(psi); return Capturing(new()).ConfigureProcess!(psi); },
        };

        await new TherionCompiler(new FixedLocator()).CompileAsync(entry, options);

        Assert.Equal(_dir, Assert.Single(seen).Environment["TMPDIR"]);
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
