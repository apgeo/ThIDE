// CAP-02.2: the workspace context card/pack resources, over the real stdio server. Proves each is
// advertised, that the card orients with totals + the survey tree and carries the honest "verify with
// tools" caveat, that the pack adds the file list and inventory, and that a missing workspace degrades
// to the {ok:false,error} envelope (which the pane detects and declines to inject).

using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp.Tests;

public class ContextCardResourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task The_card_and_pack_resources_are_advertised()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var resources = (await client.ListResourcesAsync(cancellationToken: cts.Token)).Select(r => r.Uri).ToHashSet();

        Assert.Contains("therion://context/card", resources);
        Assert.Contains("therion://context/pack", resources);
    }

    [Fact]
    public async Task The_card_orients_with_totals_and_the_survey_tree_and_the_caveat()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var text = await ReadTextAsync(client, "therion://context/card", cts.Token);

        Assert.Contains("ThIDE workspace context", text);
        Assert.Contains("Totals:", text);
        Assert.Contains("upper", text);              // the survey in the fixture
        Assert.Contains("verify with tools", text);  // the honest snapshot caveat (CD-06 spirit)
        Assert.DoesNotContain("\"ok\":", text);      // markdown card, not the JSON envelope
    }

    [Fact]
    public async Task The_pack_adds_the_file_list_and_the_inventory()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--workspace", fixture.Thconfig);

        var text = await ReadTextAsync(client, "therion://context/pack", cts.Token);

        Assert.Contains("## Files", text);
        Assert.Contains("upper.th", text);
        Assert.Contains("## Inventory", text);
    }

    [Fact]
    public async Task The_card_degrades_to_an_envelope_when_no_workspace_is_loaded()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token); // no --workspace

        var json = await ReadTextAsync(client, "therion://context/card", cts.Token);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    private static async Task<string> ReadTextAsync(McpClient client, string uri, CancellationToken ct)
    {
        var result = await client.ReadResourceAsync(uri, cancellationToken: ct);
        return result.Contents.OfType<TextResourceContents>().Single().Text;
    }
}
