// T-03.6: the --connect shim, exercised through the real executable. The full stdio<->HTTP bridge to a
// live IDE is a manual LM Studio smoke (it needs the running app); this covers the failure path a user
// actually hits — starting the shim with no IDE running — so it fails loudly, not with a silent hang.

using System.Diagnostics;
using Xunit;

namespace Therion.Mcp.Tests;

public class ShimTests
{
    [Fact]
    public void Connect_with_no_running_server_exits_3_and_says_why()
    {
        var missing = Path.Combine(
            Path.GetTempPath(), "ThIDE-test", Guid.NewGuid().ToString("N"), "mcp-endpoint.json");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { ServerHost.ServerDll, "--connect", missing },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })!;

        var stderr = process.StandardError.ReadToEnd();
        var stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "the shim did not exit");

        Assert.Equal(3, process.ExitCode);
        Assert.Contains("no running ThIDE MCP server", stderr);
        Assert.Equal("", stdout);   // stdout is the JSON-RPC channel — nothing must leak onto it
    }

    [Fact]
    public void Help_documents_the_connect_mode()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { ServerHost.ServerDll, "--help" },
            RedirectStandardOutput = true,
        })!;

        var stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(30_000));
        Assert.Contains("--connect", stdout);
    }
}
