// The names of the render node protocol end where the string ends, in the protocol's own checks
// and in its schema alike: a line break after a job id, a file name or a SHA-256 is no part of it.
// .NET's '$' matches before a final line break; ECMA-262's, which the schema means, does not.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Therion.Blender.Tests.Player;

namespace Therion.Blender.Tests;

public class RenderNodeNamesTests
{
    [Theory]
    [InlineData("job-1", true)]
    [InlineData("a", true)]
    [InlineData("job-1\n", false)]
    [InlineData("job-1\r\n", false)]
    [InlineData("\njob-1", false)]
    [InlineData("job 1", false)]
    [InlineData(".job", false)]
    [InlineData("", false)]
    public void AJobId_IsASafeName_EndToEnd_AndTheSchemaSaysTheSame(string id, bool safe)
    {
        Assert.Equal(safe, RenderNodeProtocol.IsJobId(id));
        Assert.Equal(safe, RenderNodeProtocol.IsFileName(id));
        const string job = """{"protocol":1,"jobId":%id%,"attempt":1,"leaseSeconds":120,"plan":"full","bundle":{"url":"b","size":1,"sha256":"%sha%"}}""";
        string json = job.Replace("%id%", JsonSerializer.Serialize(id)).Replace("%sha%", new string('f', 64));
        Assert.Equal(safe, RenderNodeProtocol.Validate("job", json).Count == 0);
        Assert.Equal(safe, JsonSchemaSubset.Validate(RenderNodeTests.SchemaOf("job"), json).Count == 0);
    }

    [Fact]
    public void ASha256WithALineBreakAfterIt_IsNoSha256()
    {
        string hex = new('a', 64);
        Assert.True(RenderNodeProtocol.IsSha256(hex));
        Assert.False(RenderNodeProtocol.IsSha256(hex + "\n"));
        Assert.False(RenderNodeProtocol.IsFileName("film.mp4\n"));
    }

    [Theory]
    [InlineData("^a$", "^a$(?!\n)")]
    [InlineData("^[$]x$", "^[$]x$(?!\n)")]
    [InlineData(@"\$a", @"\$a")]
    [InlineData(@"[\]$]$", "[\\]$]$(?!\n)")]
    public void ASchemaPattern_EndsWhereECMAScriptEndsIt(string pattern, string held)
        => Assert.Equal(held, RenderNodeValidator.AsEcma(pattern));

    [Fact]
    public void TheSchemasNamePatterns_AreTheProtocolsOwn()
    {
        var defs = JsonNode.Parse(RenderNodeProtocol.Schema)!["$defs"]!;
        string jobId = defs["jobId"]!["pattern"]!.GetValue<string>();
        string sha = defs["sha256"]!["pattern"]!.GetValue<string>();
        string file = defs["file"]!["properties"]!["name"]!["pattern"]!.GetValue<string>();
        foreach (string value in new[] { "job-1", "Job.2_x", "-x", "x\n", "x y", new string('a', 64), new string('a', 64) + "\n", new string('a', 65), new string('a', 129), "" })
        {
            Assert.Equal(RenderNodeProtocol.IsJobId(value), Regex.IsMatch(value, RenderNodeValidator.AsEcma(jobId), RegexOptions.ECMAScript));
            Assert.Equal(RenderNodeProtocol.IsFileName(value), Regex.IsMatch(value, RenderNodeValidator.AsEcma(file), RegexOptions.ECMAScript));
            Assert.Equal(RenderNodeProtocol.IsSha256(value), Regex.IsMatch(value, RenderNodeValidator.AsEcma(sha), RegexOptions.ECMAScript));
        }
    }
}
