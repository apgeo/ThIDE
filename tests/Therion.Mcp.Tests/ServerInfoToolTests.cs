using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class ServerInfoToolTests
{
    [Fact]
    public async Task Reports_no_ui_bridge_and_no_workspace_when_headless_and_idle()
    {
        await using var host = new WorkspaceHost();

        var result = new ServerInfoTool(host, NullUiBridge.Instance).GetServerInfo();

        Assert.True(result.Ok);
        Assert.Equal("therion-mcp", result.Data!.Name);
        Assert.Equal("6.4.0", result.Data.SyntaxVersion);
        Assert.False(result.Data.UiBridge);
        Assert.False(result.Data.WorkspaceLoaded);
        Assert.Null(result.Data.WorkspaceRoot);
    }

    [Fact]
    public async Task Reports_the_workspace_once_one_is_open()
    {
        using var fixture = FixtureWorkspace.Create();
        await using var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);

        var result = new ServerInfoTool(host, NullUiBridge.Instance).GetServerInfo();

        Assert.True(result.Data!.WorkspaceLoaded);
        Assert.Equal(fixture.Root, result.Data.WorkspaceRoot);
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
}
