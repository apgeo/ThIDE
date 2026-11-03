using System;
using System.Threading;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

// Re-rooting the session must stop watching the previous root: a file written under the old
// directory is no longer part of the workspace and must not drive the explorer/media refreshes.
public class WorkspaceSessionRootWatchTests
{
    [Fact]
    public async Task Switching_root_stops_watching_the_old_root()
    {
        using var oldRoot = new TempDir();
        using var newRoot = new TempDir();
        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());

        await session.SetRootAsync(oldRoot.Path);
        await session.SetRootAsync(newRoot.Path);

        using var raised = new ManualResetEventSlim();
        session.FileSystemChanged += (_, _) => raised.Set();

        oldRoot.Write("stray.th", "survey stray\nendsurvey\n");

        Assert.False(raised.Wait(TimeSpan.FromSeconds(2)),
            "a file written under the previous root still raised FileSystemChanged");
    }
}
