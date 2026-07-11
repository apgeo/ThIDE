// T-03.6: the discovery file the in-app host writes and the --connect shim reads. The shim treats
// every unreadable/malformed/incomplete case as "no server running", so those are the tests that matter.

using System.IO;
using Therion.Mcp;
using Xunit;

namespace Therion.Mcp.Tests;

public class McpEndpointTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "ThIDE-test", Guid.NewGuid().ToString("N"), "mcp-endpoint.json");

    [Fact]
    public void It_round_trips_through_the_camel_case_json()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var written = new McpEndpoint(1234, "deadbeef", 4321, "2026-07-11T10:00:00.0000000Z", "http://127.0.0.1:1234/");
        File.WriteAllText(path, written.ToJson());

        Assert.Contains("\"port\": 1234", File.ReadAllText(path));   // camelCase on the wire (D-012)
        Assert.Equal(written, McpEndpoint.TryRead(path));
    }

    [Fact]
    public void A_missing_file_reads_as_no_server()
    {
        Assert.Null(McpEndpoint.TryRead(TempFile()));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{ \"port\": 0, \"token\": \"t\", \"url\": \"http://x/\" }")]   // no listener
    [InlineData("{ \"port\": 1234, \"token\": \"\", \"url\": \"http://x/\" }")]  // no token
    [InlineData("{ \"port\": 1234, \"token\": \"t\", \"url\": \"\" }")]          // no url
    public void A_malformed_or_incomplete_file_reads_as_no_server(string contents)
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);

        Assert.Null(McpEndpoint.TryRead(path));
    }

    [Fact]
    public void The_default_path_lives_under_the_app_data_ThIDE_dir()
    {
        var path = McpEndpoint.DefaultPath();
        Assert.EndsWith(Path.Combine("ThIDE", "mcp-endpoint.json"), path);
        Assert.True(Path.IsPathRooted(path));
    }
}
