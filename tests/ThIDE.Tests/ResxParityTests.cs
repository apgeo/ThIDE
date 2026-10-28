using System.Xml.Linq;

namespace ThIDE.Tests;

/// <summary>
/// Every user-facing string exists in English and Romanian. <c>Tr.Get</c> falls back to the key when
/// one is missing, so a forgotten translation shows up in the UI as an identifier, not as a failure.
/// </summary>
public class ResxParityTests
{
    private static string ResourcesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ThIDE", "Resources");
    }

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(Path.Combine(ResourcesDir(), file)).Root!.Elements("data")
            .GroupBy(d => (string)d.Attribute("name")!)
            .ToDictionary(g => g.Key, g => (string?)g.Last().Element("value") ?? "");

    [Fact]
    public void English_and_Romanian_have_the_same_keys()
    {
        var en = Read("Strings.resx");
        var ro = Read("Strings.ro.resx");

        var missingInRo = en.Keys.Except(ro.Keys).Order().ToList();
        var missingInEn = ro.Keys.Except(en.Keys).Order().ToList();

        Assert.True(missingInRo.Count == 0 && missingInEn.Count == 0,
            $"Missing in Strings.ro.resx: {string.Join(", ", missingInRo)}; missing in Strings.resx: {string.Join(", ", missingInEn)}");
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.ro.resx")]
    public void No_key_is_defined_twice(string file)
    {
        var duplicates = XDocument.Load(Path.Combine(ResourcesDir(), file)).Root!.Elements("data")
            .GroupBy(d => (string)d.Attribute("name")!).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(duplicates.Count == 0, $"Defined twice in {file}: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void No_string_is_empty()
    {
        var empty = Read("Strings.resx").Concat(Read("Strings.ro.resx"))
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).Distinct().ToList();

        Assert.True(empty.Count == 0, $"Empty strings: {string.Join(", ", empty)}");
    }
}
