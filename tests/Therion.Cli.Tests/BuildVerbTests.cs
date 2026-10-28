using System.Diagnostics;
using Therion.Cli;
using Therion.Processing.Abstractions;

namespace Therion.Cli.Tests;

/// <summary>The build verb with a shell script standing in for Therion (no Therion needed).</summary>
public class BuildVerbTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-cli-build-").FullName;
    private readonly StringWriter _out = new(), _err = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class Fake : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ToolInfo?>(new ToolInfo(toolId, "/fake/therion", null, "test"));
    }

    // Exits 1 when the entry's name contains "bad", else prints a line and exits 0.
    private static ProcessStartInfo Script(ProcessStartInfo psi)
    {
        var entry = psi.ArgumentList.Last();
        psi.FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        psi.ArgumentList.Clear();
        if (OperatingSystem.IsWindows()) { psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(entry.Contains("bad") ? "exit /b 1" : "echo compiled"); }
        else { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(entry.Contains("bad") ? "exit 1" : "echo compiled"); }
        return psi;
    }

    private Task<int> Run(params string[] args) => BuildVerb.RunAsync(["build", .. args], _out, _err, new Fake(), Script);

    [Fact]
    public async Task Builds_every_thconfig_under_a_folder_and_fails_if_one_fails()
    {
        File.WriteAllText(Path.Combine(_root, "good.thconfig"), "");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "bad.thconfig"), "");

        Assert.Equal(1, await Run(_root, "--all", "--no-log"));
        Assert.Contains("1 of 2 succeeded", _out.ToString());
        Assert.Contains("compiled", _err.ToString());
    }

    [Fact]
    public async Task Json_summary_and_success_exit()
    {
        var cfg = Path.Combine(_root, "good.thconfig");
        File.WriteAllText(cfg, "");

        Assert.Equal(0, await Run(cfg, "--json", "--no-log", "--quiet"));
        Assert.Contains("\"succeeded\": true", _out.ToString());
    }

    [Fact]
    public async Task Usage_errors()
    {
        Assert.Equal(2, await Run(_root));                        // folder without --all
        Assert.Equal(2, await Run(Path.Combine(_root, "none")));  // missing
        Assert.Equal(2, await Run(_root, "--nope"));
    }
}
