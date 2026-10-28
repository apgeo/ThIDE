using System.Text.Json.Nodes;
using Therion.Assistant;
using Therion.Assistant.Skills;

namespace Therion.Assistant.Tests;

public sealed class DataPolicyTests
{
    private static FakeTool Tool(string name, string result) =>
        new(new ToolDescriptor(name, "t", """{"type":"object"}""", ReadOnly: true), _ => new ToolOutcome(result, true));

    [Fact]
    public async Task Filtered_OffersOnlyDataFreeTools_AndRefusesTheRest()
    {
        var inner = new FakeCatalog(Tool("read_file", "secret"), Tool("describe_command", "doc"));
        var filtered = new FilteredToolCatalog(inner);

        Assert.Equal(["describe_command"], filtered.Tools.Select(t => t.Name));
        var refused = await filtered.CallAsync("read_file", new Dictionary<string, object?>(), default);
        Assert.False(refused.Ok);
        Assert.Empty(inner.Received);
        Assert.True((await filtered.CallAsync("describe_command", new Dictionary<string, object?>(), default)).Ok);
    }

    [Fact]
    public void Redactor_KeepsShapeCodesAndCounts_HidesValues()
    {
        var json = """
            {"ok":true,"count":2,"stations":[{"name":"cave.1","x":12.5,"comment":"near the lake"}],
             "diagnostics":[{"code":"TH0020","severity":"error","line":4,"message":"survey 'secret' not closed"}]}
            """;

        var red = JsonNode.Parse(ToolResultRedactor.Redact(json))!;

        Assert.True((bool)red["ok"]!);
        Assert.Equal(2, (int)red["count"]!);
        Assert.Equal("…", (string?)red["stations"]![0]!["name"]);
        Assert.Equal("#", (string?)red["stations"]![0]!["x"]);
        Assert.Equal("…", (string?)red["stations"]![0]!["comment"]);
        Assert.Equal("TH0020", (string?)red["diagnostics"]![0]!["code"]);
        Assert.Equal("error", (string?)red["diagnostics"]![0]!["severity"]);
        Assert.Equal(4, (int)red["diagnostics"]![0]!["line"]!);
        Assert.DoesNotContain("secret", red.ToJsonString());
    }

    [Fact]
    public void Redactor_ReplacesPlainTextByItsLength()
    {
        Assert.Equal("(redacted: 5 characters of text)", ToolResultRedactor.Redact("hello"));
    }

    [Fact]
    public void Redactor_KeepsOnlyRealDiagnosticCodes()
    {
        var red = JsonNode.Parse(ToolResultRedactor.Redact("""{"a":"TH0020","b":"THEODORA"}"""))!;
        Assert.Equal("TH0020", (string?)red["a"]);
        Assert.Equal("…", (string?)red["b"]);
    }

    [Fact]
    public void SkillPack_HasBuiltInText_AndAddsLayersInOrder()
    {
        Assert.Contains("dryRun", SkillPack.BuiltIn);
        var (text, warnings) = SkillPack.Assemble("user note", "project note");
        Assert.Empty(warnings);
        Assert.True(text.IndexOf("user note", StringComparison.Ordinal) < text.IndexOf("project note", StringComparison.Ordinal));
        Assert.StartsWith(SkillPack.BuiltIn.TrimEnd(), text);
    }

    [Fact]
    public void SkillPack_ShortensALongLayer()
    {
        var (text, warnings) = SkillPack.Assemble(new string('ă', 10_000), null);
        Assert.Single(warnings);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) < SkillPack.BuiltIn.Length * 2 + SkillPack.MaxLayerBytes + 200);
    }
}
