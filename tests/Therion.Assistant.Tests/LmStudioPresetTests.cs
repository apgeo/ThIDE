using System.Text.Json;
using Therion.Assistant.Skills;

namespace Therion.Assistant.Tests;

public class LmStudioPresetTests
{
    [Fact]
    public void Preset_carries_the_pack_as_the_system_prompt()
    {
        using var doc = JsonDocument.Parse(SkillPack.LmStudioPreset("hello \"world\""));
        var fields = doc.RootElement.GetProperty("operation").GetProperty("fields").EnumerateArray().ToList();
        Assert.Equal("hello \"world\"", fields.Single(f => f.GetProperty("key").GetString() == "llm.prediction.systemPrompt").GetProperty("value").GetString());
        Assert.Equal(0, fields.Single(f => f.GetProperty("key").GetString() == "llm.prediction.temperature").GetProperty("value").GetInt32());
    }

    [Fact]
    public void Built_in_pack_describes_the_macros()
    {
        Assert.Contains("`audit`", SkillPack.BuiltIn);
        Assert.Contains("`release-check`", SkillPack.BuiltIn);
        Assert.Contains("therion-mcp call", SkillPack.BuiltIn);
    }

    /// <summary>The preset shipped in docs/ is generated from the pack; regenerate it when the pack changes.</summary>
    [Fact]
    public void Shipped_preset_matches_the_pack()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var shipped = File.ReadAllText(Path.Combine(dir!.FullName, "docs", "assistant", "thide.preset.json")).Replace("\r\n", "\n");
        Assert.True(SkillPack.LmStudioPreset(SkillPack.Assemble(null, null).Text) == shipped,
            "docs/assistant/thide.preset.json is out of date: run 'therion-mcp skills preset --out docs/assistant/thide.preset.json'.");
    }
}
