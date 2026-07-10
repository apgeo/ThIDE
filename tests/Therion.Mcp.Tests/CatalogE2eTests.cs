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

    /// <summary>Read-only tools the happy-path sweep above cannot exercise.</summary>
    private static readonly string[] UnsweptReadOnlyTools =
    [
        "get_declination",   // needs a WMM.COF that does not ship
        "load_workspace",    // would replace the fixture the sweep runs against
    ];

    /// <summary>Tools that can write. Every one of these must be annotated destructive, not read-only.</summary>
    private static readonly string[] MutatingTools =
    [
        "rename_symbol",
        "format_file",
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
    public async Task Tool_list_is_exactly_the_registered_catalog()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var names = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToHashSet();

        var expected = ReadOnlyCatalog.Select(c => c.Tool)
            .Concat(UnsweptReadOnlyTools)
            .Concat(MutatingTools)
            .ToHashSet();

        Assert.Equal(expected, names);
    }

    /// <summary>
    /// Every tool is described and carries a schema, and its annotations tell the truth about whether
    /// it can write. A host decides when to ask the user for confirmation from exactly these bits.
    /// </summary>
    [Fact]
    public async Task Every_tool_is_described_and_annotated_honestly()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        foreach (var tool in await client.ListToolsAsync(cancellationToken: cts.Token))
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has no description.");
            Assert.Equal(JsonValueKind.Object, tool.JsonSchema.ValueKind);

            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);

            if (MutatingTools.Contains(tool.Name))
            {
                Assert.False(annotations.ReadOnlyHint, $"{tool.Name} writes but is annotated readOnlyHint.");
                Assert.True(annotations.DestructiveHint, $"{tool.Name} writes but is not annotated destructiveHint.");
            }
            else
            {
                Assert.True(annotations.ReadOnlyHint, $"{tool.Name} is not annotated readOnlyHint.");
            }
        }
    }

    /// <summary>The safety default has to hold over the wire, not just in a handler unit test.</summary>
    [Fact]
    public async Task A_mutating_tool_called_without_dry_run_still_writes_nothing()
    {
        using var fixture = FixtureWorkspace.CreateLinked();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        var call = await client.CallToolAsync("rename_symbol",
            new Dictionary<string, object?> { ["name"] = "upper", ["newName"] = "haut", ["kind"] = "survey" },
            cancellationToken: cts.Token);

        var payload = JsonDocument.Parse(SoleTextBlock(call)).RootElement;
        Assert.True(payload.GetProperty("ok").GetBoolean(), payload.ToString());
        Assert.True(payload.GetProperty("data").GetProperty("mutation").GetProperty("dryRun").GetBoolean());
        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
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
