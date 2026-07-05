using System.Linq;
using Therion.Processing.Abstractions;
using Therion.Workspace;

namespace ThIDE.Tests;

public class ThconfigDiscoveryTests
{
    [Fact]
    public void Scan_finds_thconfig_thc_and_bare_thconfig_recursively()
    {
        using var dir = new TempDir();
        var top = dir.Write("cave.thconfig");
        var thc = dir.Write("sub/area.thc");
        var bare = dir.Write("nested/deep/thconfig");
        dir.Write("notes.th");      // not a config
        dir.Write("readme.txt");    // not a config

        var found = ThconfigDiscovery.Scan(dir.Path, new StubSniffer());

        Assert.Equal(3, found.Count);
        Assert.Contains(top, found);
        Assert.Contains(thc, found);
        Assert.Contains(bare, found);
    }

    [Fact]
    public void Scan_returns_empty_for_missing_directory()
    {
        var found = ThconfigDiscovery.Scan(@"X:\does\not\exist", new StubSniffer());
        Assert.Empty(found);
    }

    [Fact]
    public void IsCandidate_uses_sniffer_for_extensionless_non_thconfig_names()
    {
        using var dir = new TempDir();
        var weird = dir.Write("project_config");

        Assert.False(ThconfigDiscovery.IsCandidate(weird, new StubSniffer(SnifferVerdict.Unknown)));
        Assert.True(ThconfigDiscovery.IsCandidate(weird, new StubSniffer(SnifferVerdict.Likely)));
    }
}
