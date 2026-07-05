using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

// Regression coverage for batch item #2 (set-active thconfig + switching directories).
public class WorkspaceSessionRootSwitchTests
{
    private static string? Corpus()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var c = Path.Combine(dir.FullName, "tests", "Corpus", "sample_projects",
                "Therion_202502x", "500", "PS-1intrare");
            if (Directory.Exists(c)) return c;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task Every_corpus_thconfig_can_be_set_active()
    {
        var root = Corpus();
        if (root is null) return; // corpus not present

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(root);

        var configs = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".thc", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".thconfig", StringComparison.OrdinalIgnoreCase));

        foreach (var c in configs)
            Assert.True(await session.SetActiveThconfigAsync(c), $"failed to activate {c}");
    }

    [Fact]
    public async Task Switching_to_a_new_root_drops_the_previous_directorys_thconfigs()
    {
        using var a = new TempDir();
        using var b = new TempDir();
        var aCfg = a.Write("a.thconfig", "encoding utf-8\n");
        var bCfg = b.Write("b.thconfig", "encoding utf-8\n");
        var external = a.Write("ext/external.thc", "encoding utf-8\n");

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(a.Path);
        session.RegisterExternalThconfig(external);
        Assert.Contains(session.Candidates, c => string.Equals(c.FullPath, Path.GetFullPath(aCfg), StringComparison.OrdinalIgnoreCase));

        // Move to a different directory: only B's config should remain, A's (and the external) are gone.
        await session.SetRootAsync(b.Path);

        Assert.Contains(session.Candidates, c => string.Equals(c.FullPath, Path.GetFullPath(bCfg), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.Candidates, c => string.Equals(c.FullPath, Path.GetFullPath(aCfg), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(session.Candidates, c => string.Equals(c.FullPath, Path.GetFullPath(external), StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(session.ActiveThconfig);
        Assert.Equal(Path.GetFullPath(bCfg), Path.GetFullPath(session.ActiveThconfig!.FullPath), ignoreCase: true);
    }
}
