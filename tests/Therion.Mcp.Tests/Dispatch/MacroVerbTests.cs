using Microsoft.Extensions.DependencyInjection;
using Therion.Mcp.Dispatch;

namespace Therion.Mcp.Tests.Dispatch;

public class MacroVerbTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-macro-cli-").FullName;
    private readonly StringWriter _out = new(), _err = new();
    private const string Messy = "survey cave\ncentreline\ndata normal from to length compass clino\n1 2 10 0 0\nendcentreline\nendsurvey\n";

    public MacroVerbTests()
    {
        File.WriteAllText(Path.Combine(_root, "cave.th"), Messy);
        File.WriteAllText(Path.Combine(_root, "cave.thconfig"), "source cave.th\n");
        Directory.CreateDirectory(Path.Combine(_root, ".thide", "macros"));
        File.WriteAllText(Path.Combine(_root, ".thide", "macros", "tidy.json"),
            """{ "title": "Tidy", "steps": [ { "id": "fmt", "tool": "format_file", "args": { "path": "cave.th", "write": true } } ] }""");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<int> Run(params string[] args) => MacroVerb.RunAsync(["macro", .. args], _out, _err, s => s.BuildServiceProvider(), Path.Combine(_root, "no-user-macros"));

    [Fact]
    public async Task Lists_built_in_and_project_macros()
    {
        Assert.Equal(0, await Run("list", "--workspace", _root));
        Assert.Contains("release-check", _out.ToString());
        Assert.Contains("tidy", _out.ToString());
    }

    [Fact]
    public async Task Run_previews_unless_applied()
    {
        var cave = Path.Combine(_root, "cave.th");

        Assert.Equal(0, await Run("run", "tidy", "--workspace", Path.Combine(_root, "cave.thconfig")));
        Assert.Equal(Messy, File.ReadAllText(cave));
        Assert.Contains("Preview only", _out.ToString());

        Assert.Equal(0, await Run("run", "tidy", "--workspace", Path.Combine(_root, "cave.thconfig"), "--apply"));
        Assert.NotEqual(Messy, File.ReadAllText(cave));
    }

    [Fact]
    public async Task Missing_variable_is_a_usage_error()
    {
        Assert.Equal(2, await Run("run", "end-of-trip", "--workspace", _root));
        Assert.Contains("source", _err.ToString());
    }

    [Fact]
    public async Task A_failed_preview_is_not_applied()
    {
        File.WriteAllText(Path.Combine(_root, ".thide", "macros", "bad.json"), """
            { "steps": [
              { "id": "fmt", "tool": "format_file", "args": { "path": "cave.th", "write": true } },
              { "id": "oops", "tool": "format_file", "args": { "path": "missing.th" } } ] }
            """);

        Assert.Equal(1, await Run("run", "bad", "--workspace", Path.Combine(_root, "cave.thconfig"), "--apply"));

        Assert.Equal(Messy, File.ReadAllText(Path.Combine(_root, "cave.th")));
        Assert.Contains("nothing was applied", _err.ToString());
    }
}
