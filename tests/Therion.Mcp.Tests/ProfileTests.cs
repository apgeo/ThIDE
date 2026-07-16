using System.Text.Json;
using ModelContextProtocol.Client;

namespace Therion.Mcp.Tests;

/// <summary>
/// The `data` profile is a structural guarantee, not a prompt: a model cannot be talked into using a
/// tool that was never registered. These tests spawn the real server, because registration is the
/// thing under test.
/// </summary>
public class ProfileTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>Every tool the read-only profile exposes must actually be read-only.</summary>
    [Fact]
    public async Task The_data_profile_exposes_only_read_only_tools()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--profile", "data");

        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);

        Assert.NotEmpty(tools);
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint,
            $"{t.Name} is offered by the data profile but is not read-only."));
    }

    [Fact]
    public async Task The_data_profile_hides_every_tool_that_can_write()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(cts.Token, "--profile", "data");

        var names = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToHashSet();

        Assert.DoesNotContain("rename_symbol", names);
        Assert.DoesNotContain("format_file", names);
        Assert.DoesNotContain("run_build", names);
        Assert.DoesNotContain("export_gis", names);
        Assert.DoesNotContain("set_lead_status", names);
        Assert.DoesNotContain("project_metadata_set", names);

        // …but the reads it splits from are still there.
        Assert.Contains("get_diagnostics", names);
        Assert.Contains("project_metadata_get", names);
    }

    /// <summary>Calling a tool the profile withheld must fail at the protocol, not silently succeed.</summary>
    [Fact]
    public async Task A_withheld_tool_cannot_be_called_anyway()
    {
        using var fixture = FixtureWorkspace.Create();
        using var cts = new CancellationTokenSource(Timeout);
        await using var client = await ServerHost.ConnectAsync(
            cts.Token, "--profile", "data", "--workspace", fixture.Thconfig);
        var before = File.ReadAllText(fixture.PathTo("caves", "upper.th"));

        await Assert.ThrowsAnyAsync<Exception>(() => client.CallToolAsync("rename_symbol",
            new Dictionary<string, object?> { ["name"] = "upper", ["newName"] = "haut", ["dryRun"] = false },
            cancellationToken: cts.Token).AsTask());

        Assert.Equal(before, File.ReadAllText(fixture.PathTo("caves", "upper.th")));
    }

    [Fact]
    public async Task The_full_profile_is_the_default_and_includes_the_writers()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var defaulted = await ServerHost.ConnectAsync(cts.Token);
        await using var explicitly = await ServerHost.ConnectAsync(cts.Token, "--profile", "full");

        var a = (await defaulted.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToHashSet();
        var b = (await explicitly.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToHashSet();

        Assert.Equal(a, b);
        Assert.Contains("rename_symbol", a);
        Assert.Contains("run_build", a);
    }

    /// <summary>The profiles partition the catalog: nothing is in neither, nothing is in both twice.</summary>
    [Fact]
    public async Task Data_plus_the_writers_is_the_whole_catalog()
    {
        using var cts = new CancellationTokenSource(Timeout);
        await using var dataClient = await ServerHost.ConnectAsync(cts.Token, "--profile", "data");
        await using var fullClient = await ServerHost.ConnectAsync(cts.Token, "--profile", "full");

        var data = (await dataClient.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToHashSet();
        var full = (await fullClient.ListToolsAsync(cancellationToken: cts.Token)).ToList();

        Assert.True(data.IsProperSubsetOf(full.Select(t => t.Name)));
        Assert.All(full.Where(t => !data.Contains(t.Name)),
            t => Assert.False(t.ProtocolTool.Annotations?.ReadOnlyHint,
                $"{t.Name} is read-only but the data profile withholds it."));
    }

    [Fact]
    public void An_unknown_profile_is_refused_at_startup()
    {
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { ServerHost.ServerDll, "--profile", "readonly" },
            RedirectStandardError = true,
        })!;

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("unknown profile", stderr);
    }
}
