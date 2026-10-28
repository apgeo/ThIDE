using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Therion.Mcp;
using Therion.Mcp.Dispatch;

namespace Therion.Mcp.Tests.Dispatch;

public class MacroRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-macro-").FullName;
    private const string Messy = "survey cave\ncentreline\ndata normal from to length compass clino\n1 2 10 0 0\nendcentreline\nendsurvey\n";

    public MacroRunnerTests()
    {
        File.WriteAllText(Path.Combine(_root, "cave.th"), Messy);
        File.WriteAllText(Path.Combine(_root, "cave.thconfig"), "source cave.th\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private MacroRunner Runner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(Path.Combine(_root, "cave.thconfig")));
        services.AddToolServices();
        return new MacroRunner(new ToolDispatcher(services.BuildServiceProvider()));
    }

    private static MacroDefinition Macro(string json) => MacroCatalog.Load("t", json).Macro!;

    private static readonly MacroDefinition Tidy = Macro("""
        { "title": "Tidy", "vars": [ { "id": "file", "default": "cave.th" } ],
          "steps": [
            { "id": "fmt", "tool": "format_file", "args": { "path": "${file}", "write": true } },
            { "id": "build", "tool": "run_build" },
            { "id": "diag", "tool": "get_diagnostics", "args": { "file": "${file}" } } ] }
        """);

    [Fact]
    public async Task Preview_changes_nothing_and_lists_what_it_skips()
    {
        var run = await Runner().RunAsync(Tidy, new Dictionary<string, string>(), apply: false);

        Assert.Equal([StepStatus.Previewed, StepStatus.Skipped, StepStatus.Ran], run.Steps.Select(s => s.Status));
        Assert.Equal(Messy, File.ReadAllText(Path.Combine(_root, "cave.th")));
    }

    [Fact]
    public async Task Apply_after_preview_writes()
    {
        var runner = Runner();
        var macro = Macro("""{ "steps": [ { "id": "fmt", "tool": "format_file", "args": { "path": "cave.th", "write": true } } ] }""");
        var preview = await runner.RunAsync(macro, new Dictionary<string, string>(), apply: false);

        var run = await runner.RunAsync(macro, new Dictionary<string, string>(), apply: true, preview);

        Assert.False(run.Failed, run.Steps[0].Error);
        Assert.NotEqual(Messy, File.ReadAllText(Path.Combine(_root, "cave.th")));
    }

    [Fact]
    public void Check_reports_unknown_variables_steps_and_tools()
    {
        var bad = Macro("""
            { "steps": [
              { "id": "a", "tool": "get_diagnostics", "args": { "file": "${nope}", "x": "${steps.b.data}" } },
              { "id": "b", "tool": "no_tool" } ] }
            """);

        var problems = Runner().Check(bad, new Dictionary<string, string>());

        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public async Task Step_results_flow_into_later_steps_and_failures_stop_the_run()
    {
        var macro = Macro("""
            { "steps": [
              { "id": "info", "tool": "server_info" },
              { "id": "diag", "tool": "get_diagnostics", "args": { "file": "${steps.info.data.missing}" } },
              { "id": "last", "tool": "server_info" } ] }
            """);

        var run = await Runner().RunAsync(macro, new Dictionary<string, string>(), apply: true);

        Assert.Equal([StepStatus.Ran, StepStatus.Failed], run.Steps.Select(s => s.Status));
    }

    [Fact]
    public void Built_ins_load()
    {
        Assert.Equal(["release-check", "end-of-trip"], MacroCatalog.BuiltIns.Select(m => m.Name));
    }

    [Fact]
    public void Check_counts_defaults_as_given()
    {
        Assert.Empty(Runner().Check(Tidy, new Dictionary<string, string>()));
    }
}
