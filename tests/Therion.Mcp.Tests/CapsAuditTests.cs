using System.Text.Json;
using ModelContextProtocol.Client;

namespace Therion.Mcp.Tests;

/// <summary>
/// Local hosts run small context windows: a tool that dumps a whole cave into one message costs the
/// model the very context it needs to act on the answer. These tests read the *shipped* schemas, so a
/// new tool that forgets its caps fails the build rather than a user's session.
/// </summary>
public class CapsAuditTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>Tools whose result is a list of unbounded length. Each must take offset and limit.</summary>
    private static readonly string[] PagedTools =
    [
        "list_files", "get_diagnostics", "list_symbols", "find_references",
        "list_stations", "list_todos", "list_leads", "deps_graph", "structural_analysis",
    ];

    /// <summary>Tools whose result is a list that is capped but not paged — the counts stay complete.</summary>
    private static readonly string[] CappedTools = ["survey_graph", "survey_stats"];

    /// <summary>Tools that hand back a document. Each must take a byte budget.</summary>
    private static readonly string[] TextTools =
    [
        "read_file", "format_file", "import_survey", "export_gis", "export_tables", "generate_report", "deps_graph",
    ];

    [Fact]
    public async Task Every_list_returning_tool_takes_offset_and_limit()
    {
        var schemas = await SchemasAsync();

        foreach (var tool in PagedTools)
        {
            Assert.True(Has(schemas, tool, "offset"), $"{tool} returns a list but takes no offset.");
            Assert.True(Has(schemas, tool, "limit"), $"{tool} returns a list but takes no limit.");
        }
    }

    [Fact]
    public async Task Every_capped_list_tool_takes_a_limit()
    {
        var schemas = await SchemasAsync();

        foreach (var tool in CappedTools)
            Assert.True(Has(schemas, tool, "limit"), $"{tool} returns a list but takes no limit.");
    }

    [Fact]
    public async Task Every_document_returning_tool_takes_a_byte_budget()
    {
        var schemas = await SchemasAsync();

        foreach (var tool in TextTools)
            Assert.True(Has(schemas, tool, "maxBytes"), $"{tool} returns a document but takes no maxBytes.");
    }

    /// <summary>Paging that starts past the end must return an empty page, not throw or wrap around.</summary>
    [Fact]
    public async Task An_offset_past_the_end_returns_nothing()
    {
        using var fixture = FixtureWorkspace.Create();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var files = await new Tools.WorkspaceTools(host).ListFiles(offset: 10_000);

        Assert.True(files.Ok);
        Assert.Empty(files.Data!.Files);
        Assert.Equal(2, files.Data.Total);
        Assert.False(files.Data.Truncated);
    }

    /// <summary>A negative offset is a caller mistake, not a reason to fail: clamp it.</summary>
    [Fact]
    public async Task A_negative_offset_is_clamped_to_the_start()
    {
        using var fixture = FixtureWorkspace.Create();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var files = await new Tools.WorkspaceTools(host).ListFiles(offset: -5);

        Assert.Equal(0, files.Data!.Offset);
        Assert.Equal(2, files.Data.Files.Count);
    }

    [Fact]
    public void A_limit_beyond_the_ceiling_is_clamped()
    {
        Assert.Equal(ToolLimits.DefaultPageLimit, ToolLimits.ClampLimit(0));
        Assert.Equal(ToolLimits.DefaultPageLimit, ToolLimits.ClampLimit(-1));
        Assert.Equal(ToolLimits.MaxPageLimit, ToolLimits.ClampLimit(int.MaxValue));
        Assert.Equal(ToolLimits.DefaultMaxBytes, ToolLimits.ClampBytes(0));
        Assert.Equal(ToolLimits.HardMaxBytes, ToolLimits.ClampBytes(int.MaxValue));
    }

    private static bool Has(IReadOnlyDictionary<string, JsonElement> schemas, string tool, string parameter) =>
        schemas.TryGetValue(tool, out var schema)
        && schema.TryGetProperty("properties", out var properties)
        && properties.TryGetProperty(parameter, out _);

    private static async Task<IReadOnlyDictionary<string, JsonElement>> SchemasAsync()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        return (await client.ListToolsAsync(cancellationToken: cts.Token))
            .ToDictionary(t => t.Name, t => t.JsonSchema.Clone());
    }
}
