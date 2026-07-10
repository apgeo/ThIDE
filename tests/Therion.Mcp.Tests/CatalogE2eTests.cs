using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp.Tests;

/// <summary>
/// Drives the real <c>therion-mcp</c> process the way a host does: spawn, handshake, list tools, call
/// them. Unit tests reach the handlers directly and would never notice a schema that cannot be
/// generated, a DTO that will not serialize, or a tool missing from the registration list.
/// </summary>
public class CatalogE2eTests
{
    /// <summary>Every ring-R1 tool, with arguments that should succeed against the fixture workspace.</summary>
    private static readonly (string Tool, Dictionary<string, object?>? Args)[] ReadOnlyCatalog =
    [
        ("server_info", null),
        ("workspace_info", null),
        ("list_files", null),
        ("read_file", new() { ["path"] = "caves/upper.th" }),
        ("get_diagnostics", null),
        ("explain_diagnostic", new() { ["code"] = "TH_SEM_015" }),
        ("list_symbols", null),
        ("goto_definition", new() { ["name"] = "upper" }),
        ("find_references", new() { ["name"] = "upper", ["kind"] = "survey" }),
        ("survey_graph", null),
        ("survey_stats", null),
        ("deps_graph", new() { ["dot"] = true }),
        ("list_stations", null),
        ("list_todos", null),
        ("list_leads", null),
        ("structural_analysis", new() { ["file"] = "caves/upper.th" }),
        ("convert_units", new() { ["value"] = 100.0, ["from"] = "foot", ["to"] = "metre" }),
        ("convert_coordinates", new() { ["latitude"] = 46.77, ["longitude"] = 22.83 }),
    ];

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task Every_read_only_tool_answers_ok_on_a_real_workspace()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        foreach (var (tool, args) in ReadOnlyCatalog)
        {
            var call = await client.CallToolAsync(tool, args, cancellationToken: cts.Token);

            Assert.False(call.IsError is true, $"{tool} returned a protocol error.");
            var payload = JsonDocument.Parse(SoleTextBlock(call)).RootElement;
            Assert.True(payload.GetProperty("ok").GetBoolean(),
                $"{tool} answered ok:false — {payload}");
        }
    }

    /// <summary>The catalog the model sees must be exactly the catalog we think we registered.</summary>
    [Fact]
    public async Task Tool_list_is_the_twenty_read_only_tools()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var names = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToHashSet();

        // The two the happy-path sweep cannot exercise: get_declination needs a WMM.COF that does not
        // ship, and load_workspace would replace the fixture the sweep is running against.
        Assert.Equal(ReadOnlyCatalog.Length + 2, names.Count);
        Assert.Contains("get_declination", names);
        Assert.Contains("load_workspace", names);
        foreach (var (tool, _) in ReadOnlyCatalog) Assert.Contains(tool, names);
    }

    /// <summary>Every tool is described, annotated read-only, and takes a schema — this is what a model reads.</summary>
    [Fact]
    public async Task Every_tool_is_described_and_annotated_read_only()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        foreach (var tool in await client.ListToolsAsync(cancellationToken: cts.Token))
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has no description.");
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint, $"{tool.Name} is not annotated readOnlyHint.");
            Assert.Equal(JsonValueKind.Object, tool.JsonSchema.ValueKind);
        }
    }

    [Fact]
    public async Task Starting_with_a_workspace_makes_the_project_answerable_without_load_workspace()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var call = await client.CallToolAsync("workspace_info", cancellationToken: cts.Token);

        var data = JsonDocument.Parse(SoleTextBlock(call)).RootElement.GetProperty("data");
        Assert.True(data.GetProperty("loaded").GetBoolean());
        Assert.Equal("project.thconfig", data.GetProperty("entryPoint").GetString());
    }

    [Fact]
    public async Task Without_a_workspace_tools_say_so_rather_than_failing_the_call()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var call = await client.CallToolAsync("survey_stats", cancellationToken: cts.Token);

        Assert.False(call.IsError is true);
        var payload = JsonDocument.Parse(SoleTextBlock(call)).RootElement;
        Assert.False(payload.GetProperty("ok").GetBoolean());
        Assert.Equal("workspace_not_loaded", payload.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>A path escape must be refused across the wire, not only in the handler unit test.</summary>
    [Fact]
    public async Task The_path_jail_holds_over_the_wire()
    {
        using var fixture = FixtureWorkspace.CreateAnnotated();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var call = await client.CallToolAsync("read_file",
            new Dictionary<string, object?> { ["path"] = "../../../etc/passwd" }, cancellationToken: cts.Token);

        var payload = JsonDocument.Parse(SoleTextBlock(call)).RootElement;
        Assert.False(payload.GetProperty("ok").GetBoolean());
        Assert.Equal("path_outside_workspace", payload.GetProperty("error").GetProperty("code").GetString());
    }

    private static string SoleTextBlock(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
