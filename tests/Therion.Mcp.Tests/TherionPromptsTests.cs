// T-04.3: the MCP prompts, over the real stdio server. They need no workspace (they are guidance text),
// so a bare server advertises and expands them.

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Therion.Mcp.Tests;

public class TherionPromptsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task The_prompts_are_advertised()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var names = (await client.ListPromptsAsync(cancellationToken: cts.Token)).Select(p => p.Name).ToHashSet();

        Assert.Contains("audit_workspace", names);
        Assert.Contains("fix_diagnostic", names);
        Assert.Contains("summarize_survey", names);
        Assert.Contains("prepare_release", names);
        Assert.Contains("plan_exploration", names);
        Assert.Contains("summarize_history", names);
    }

    [Fact]
    public async Task Fix_diagnostic_interpolates_the_code_argument()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var result = await client.GetPromptAsync("fix_diagnostic",
            new Dictionary<string, object?> { ["code"] = "TH_SEM_015" }, cancellationToken: cts.Token);

        Assert.Contains("TH_SEM_015", Text(result));
    }

    [Fact]
    public async Task A_no_argument_prompt_expands()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token);

        var result = await client.GetPromptAsync("audit_workspace", cancellationToken: cts.Token);

        Assert.Contains("get_diagnostics", Text(result));
    }

    private static string Text(GetPromptResult result) =>
        string.Concat(result.Messages.Select(m => (m.Content as TextContentBlock)?.Text));
}
