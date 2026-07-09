using System;
using System.IO;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

// The active thconfig defines the project, so the workspace root must follow it: activating a config
// that lives outside the current root re-roots the session at that config's directory, instead of
// leaving the Workspace Explorer pointed at an unrelated tree.
public class WorkspaceSessionRootFollowsThconfigTests
{
    private static WorkspaceSessionService NewSession() => new(new StubSniffer(), new FakeSettings());

    [Fact]
    public async Task Activating_a_thconfig_outside_the_root_re_roots_at_its_directory()
    {
        using var first = new TempDir();
        using var other = new TempDir();
        first.Write("first.thconfig");
        var outside = other.Write("outside.thconfig");

        await using var session = NewSession();
        await session.SetRootAsync(first.Path);
        Assert.Equal(Full(first.Path), Full(session.RootPath!), ignoreCase: true);

        Assert.True(await session.SetActiveThconfigAsync(outside));

        Assert.Equal(Full(other.Path), Full(session.RootPath!), ignoreCase: true);
        Assert.Equal(Full(outside), Full(session.ActiveThconfig!.FullPath), ignoreCase: true);
    }

    [Fact]
    public async Task A_re_rooted_config_is_no_longer_flagged_external()
    {
        using var first = new TempDir();
        using var other = new TempDir();
        first.Write("first.thconfig");
        var outside = other.Write("outside.thconfig");

        await using var session = NewSession();
        await session.SetRootAsync(first.Path);
        await session.SetActiveThconfigAsync(outside);

        Assert.False(session.ActiveThconfig!.IsExternal);
        Assert.DoesNotContain(session.Candidates, c => c.FullPath.StartsWith(Full(first.Path), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Activating_a_thconfig_under_the_root_leaves_the_root_alone()
    {
        using var dir = new TempDir();
        dir.Write("top.thconfig");
        var deep = dir.Write("sub/deep.thconfig");

        await using var session = NewSession();
        await session.SetRootAsync(dir.Path);

        Assert.True(await session.SetActiveThconfigAsync(deep));

        Assert.Equal(Full(dir.Path), Full(session.RootPath!), ignoreCase: true);   // NOT dir/sub
    }

    [Fact]
    public async Task A_failed_activation_leaves_the_root_where_it_was()
    {
        using var dir = new TempDir();
        dir.Write("top.thconfig");

        await using var session = NewSession();
        await session.SetRootAsync(dir.Path);

        Assert.False(await session.SetActiveThconfigAsync(Path.Combine(Path.GetTempPath(), "no-such-dir", "gone.thconfig")));

        Assert.Equal(Full(dir.Path), Full(session.RootPath!), ignoreCase: true);
    }

    private static string Full(string p) => Path.GetFullPath(p);
}
