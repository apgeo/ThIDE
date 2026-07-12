// T-04.1: the workspace-data resources, over the real stdio server. Proves each is advertised and reads
// back the same data the tools return — including the multi-segment {path} template, the one thing the
// SDK's URI binding could get wrong.

using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp.Tests;

public class WorkspaceResourcesTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task The_resources_and_the_file_template_are_advertised()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var resources = (await client.ListResourcesAsync(cancellationToken: cts.Token)).Select(r => r.Uri).ToHashSet();
        var templates = (await client.ListResourceTemplatesAsync(cancellationToken: cts.Token))
            .Select(t => t.UriTemplate).ToHashSet();

        Assert.Contains("therion://diagnostics", resources);
        Assert.Contains("therion://stats", resources);
        Assert.Contains("therion://graph/survey", resources);
        Assert.Contains("therion://file/{+path}", templates);
    }

    [Fact]
    public async Task A_file_resource_returns_the_file_text_for_a_multi_segment_path()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var text = await ReadTextAsync(client, "therion://file/caves/upper.th", cts.Token);

        Assert.Contains("survey upper", text);
    }

    [Fact]
    public async Task A_file_resource_reports_a_missing_file_as_an_envelope_not_a_throw()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var json = await ReadTextAsync(client, "therion://file/caves/nope.th", cts.Token);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("file_not_found", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_diagnostics_resource_is_the_get_diagnostics_envelope()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var json = await ReadTextAsync(client, "therion://diagnostics", cts.Token);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").TryGetProperty("diagnostics", out _));
    }

    [Fact]
    public async Task The_thbook_resource_returns_a_citation_and_needs_no_workspace()
    {
        // No --workspace: the thbook index is bundled, so this resource answers on a bare server.
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var json = await ReadTextAsync(client, "therion://thbook/equate", cts.Token);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(34, doc.RootElement.GetProperty("data").GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task The_graph_resource_reports_the_cave_connectivity()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var json = await ReadTextAsync(client, "therion://graph/survey", cts.Token);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("data").GetProperty("stations").GetInt32() > 0);
    }

    private static async Task<string> ReadTextAsync(McpClient client, string uri, CancellationToken ct)
    {
        var result = await client.ReadResourceAsync(uri, cancellationToken: ct);
        return result.Contents.OfType<TextResourceContents>().Single().Text;
    }
}
