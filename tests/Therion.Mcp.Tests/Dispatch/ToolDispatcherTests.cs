using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Therion.Mcp;
using Therion.Mcp.Dispatch;

namespace Therion.Mcp.Tests.Dispatch;

/// <summary>The in-process dispatcher over an invented project.</summary>
public class ToolDispatcherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-dispatch-").FullName;

    public ToolDispatcherTests()
    {
        File.WriteAllText(Path.Combine(_root, "cave.th"),
            "survey cave\ncentreline\ndata normal from to length compass clino\n1 2 10 0 0\nendcentreline\nendsurvey\n");
        File.WriteAllText(Path.Combine(_root, "cave.thconfig"), "source cave.th\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ToolDispatcher Dispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(Path.Combine(_root, "cave.thconfig")));
        services.AddToolServices();
        return new ToolDispatcher(services.BuildServiceProvider());
    }

    private static JsonElement Args(object o) => JsonSerializer.SerializeToElement(o);

    [Fact]
    public void Lists_the_tools_with_their_capabilities()
    {
        var tools = Dispatcher().Tools;

        Assert.Contains(tools, t => t.Name == "server_info" && t.ReadOnly);
        var format = Assert.Single(tools, t => t.Name == "format_file");
        Assert.False(format.ReadOnly);
        Assert.True(format.HasExpectedSha);
        Assert.Contains(tools, t => t.Name == "add_map_members" && t.HasDryRun);
        Assert.DoesNotContain(tools, t => t.Name == "run_command");   // no UI ring without a UI
    }

    [Fact]
    public async Task Calls_a_read_tool_with_arguments_and_defaults()
    {
        var outcome = await Dispatcher().CallAsync("get_diagnostics", Args(new { minSeverity = "error" }));

        Assert.True(outcome.Found);
        Assert.True(outcome.Ok, outcome.Error);
        Assert.True(outcome.Result.GetProperty("data").TryGetProperty("diagnostics", out _));
    }

    [Fact]
    public async Task A_preview_writes_nothing()
    {
        var before = File.ReadAllText(Path.Combine(_root, "cave.th"));

        var outcome = await Dispatcher().CallAsync("format_file", Args(new { path = "cave.th" }));

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "cave.th")));
    }

    [Fact]
    public async Task Unknown_tool_and_bad_argument_are_outcomes_not_exceptions()
    {
        var d = Dispatcher();

        var unknown = await d.CallAsync("no_such_tool", Args(new { }));
        var bad = await d.CallAsync("get_diagnostics", Args(new { limit = "lots" }));

        Assert.False(unknown.Found);
        Assert.True(bad.Found);
        Assert.False(bad.Ok);
        Assert.NotNull(bad.Error);
    }
}
