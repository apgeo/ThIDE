using System.Text.Json;
using ModelContextProtocol.Protocol;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class ServerInfoToolTests
{
    [Fact]
    public void Reports_no_ui_bridge_when_headless()
    {
        var result = ServerInfoTool.GetServerInfo(NullUiBridge.Instance);

        Assert.True(result.Ok);
        Assert.NotNull(result.Data);
        Assert.Equal("therion-mcp", result.Data.Name);
        Assert.Equal("6.4.0", result.Data.SyntaxVersion);
        Assert.False(result.Data.UiBridge);
    }

    [Fact]
    public async Task Stdio_host_serves_the_tool_catalog_and_server_info()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        Assert.Equal("therion-mcp", client.ServerInfo.Name);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        var serverInfo = Assert.Single(tools, t => t.Name == "server_info");
        Assert.False(string.IsNullOrWhiteSpace(serverInfo.Description));

        var call = await client.CallToolAsync("server_info", cancellationToken: cts.Token);
        Assert.NotEqual(true, call.IsError);

        var payload = JsonDocument.Parse(SoleTextBlock(call)).RootElement;
        Assert.True(payload.GetProperty("ok").GetBoolean());

        var data = payload.GetProperty("data");
        Assert.Equal("therion-mcp", data.GetProperty("name").GetString());
        Assert.Equal("6.4.0", data.GetProperty("syntaxVersion").GetString());
        Assert.False(data.GetProperty("uiBridge").GetBoolean());
    }

    /// <summary>Ring R3 has no place in a headless host — the null bridge must keep it unregistered.</summary>
    [Fact]
    public async Task Stdio_host_exposes_no_ui_tools()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        Assert.DoesNotContain(tools, t => t.Name is "open_file" or "run_command" or "get_ui_state");
    }

    private static string SoleTextBlock(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
}
