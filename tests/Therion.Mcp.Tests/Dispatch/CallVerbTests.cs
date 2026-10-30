using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Therion.Mcp.Dispatch;

namespace Therion.Mcp.Tests.Dispatch;

public class CallVerbTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-call-cli-").FullName;
    private readonly StringWriter _out = new(), _err = new();
    private const string Messy = "survey cave\ncentreline\ndata normal from to length compass clino\n1 2 10 0 0\nendcentreline\nendsurvey\n";

    public CallVerbTests()
    {
        File.WriteAllText(Path.Combine(_root, "cave.th"), Messy);
        File.WriteAllText(Path.Combine(_root, "cave.thconfig"), "source cave.th\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<int> Run(params string[] args) => CallVerb.RunAsync(["call", .. args], _out, _err, s => s.BuildServiceProvider());

    [Fact]
    public async Task Lists_tools()
    {
        Assert.Equal(0, await Run("--list"));
        Assert.Contains("format_file", _out.ToString());
        Assert.Contains("survey_stats", _out.ToString());
    }

    [Fact]
    public async Task Calls_a_tool_and_prints_its_result()
    {
        Assert.Equal(0, await Run("survey_stats", "--workspace", Path.Combine(_root, "cave.thconfig")));
        using var doc = JsonDocument.Parse(_out.ToString());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("data").GetProperty("stations").GetInt32());
    }

    [Fact]
    public async Task Arguments_are_typed_by_the_schema_and_tools_keep_their_preview()
    {
        // write is a boolean in the schema: "false" must arrive as false, not as the string.
        Assert.Equal(0, await Run("format_file", "--workspace", Path.Combine(_root, "cave.thconfig"), "--arg", "path=cave.th", "--arg", "write=false"));
        Assert.Equal(Messy, File.ReadAllText(Path.Combine(_root, "cave.th")));
        Assert.Contains("  centreline", _out.ToString());

        Assert.Equal(0, await Run("format_file", "--workspace", Path.Combine(_root, "cave.thconfig"), "--args", """{ "path": "cave.th", "write": true }"""));
        Assert.StartsWith("survey cave\n  centreline", File.ReadAllText(Path.Combine(_root, "cave.th")));
    }

    [Fact]
    public async Task Failures_and_usage()
    {
        Assert.Equal(1, await Run("format_file", "--workspace", Path.Combine(_root, "cave.thconfig"), "--arg", "path=missing.th"));
        Assert.Contains("error:", _err.ToString());
        Assert.Equal(2, await Run("no_such_tool"));
        Assert.Equal(2, await Run("format_file", "--args", "[1]"));
        Assert.Equal(2, await Run());
    }

    [Fact]
    public void Prompts_offer_the_matching_macro_and_the_macro_exists()
    {
        Assert.Contains("'audit'", Therion.Mcp.Prompts.TherionPrompts.AuditWorkspace());
        Assert.Contains("'release-check'", Therion.Mcp.Prompts.TherionPrompts.PrepareRelease());
        Assert.Contains(MacroCatalog.BuiltIns, m => m.Name == "audit");
        Assert.Contains(MacroCatalog.BuiltIns, m => m.Name == "release-check");
    }
}
