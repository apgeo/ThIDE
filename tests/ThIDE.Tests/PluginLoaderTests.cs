using System.IO;
using System.Linq;
using Therion.Semantics;
using ThIDE.Services;

namespace ThIDE.Tests;

// the plugin loader discovers ISemanticRule implementations in external assemblies.
public class PluginLoaderTests
{
    [Fact]
    public void Missing_directory_returns_empty()
    {
        Assert.Empty(PluginLoader.LoadSemanticRules(Path.Combine(Path.GetTempPath(), "no-such-dir-" + System.Guid.NewGuid())));
    }

    [Fact]
    public void Empty_directory_returns_empty()
    {
        using var dir = new TempDir();
        Assert.Empty(PluginLoader.LoadSemanticRules(dir.Path));
    }

    [Fact]
    public void Discovers_semantic_rules_in_an_assembly()
    {
        // Drop the Therion.Semantics assembly (which contains built-in ISemanticRule types) into a
        // plugins folder and confirm the loader instantiates at least one rule from it.
        using var dir = new TempDir();
        var src = typeof(ISemanticRule).Assembly.Location;
        var dest = Path.Combine(dir.Path, Path.GetFileName(src));
        File.Copy(src, dest);

        var rules = PluginLoader.LoadSemanticRules(dir.Path);
        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.False(string.IsNullOrEmpty(r.Id)));
    }

    [Fact]
    public void Default_plugin_directory_is_under_thide()
    {
        var dir = PluginLoader.DefaultPluginDirectory();
        Assert.EndsWith("plugins", dir);
        Assert.Contains("ThIDE", dir);
    }
}
