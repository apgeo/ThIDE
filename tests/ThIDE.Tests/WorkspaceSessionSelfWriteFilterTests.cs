using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// The self-write mark exists so the IDE's own save is not reported as an external edit. It must not
/// also hide a genuine external write that follows the save shortly afterwards (a save hook that
/// post-processes the file, an agent that saves and then edits).
/// </summary>
public class WorkspaceSessionSelfWriteFilterTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task External_write_shortly_after_own_save_is_reported()
    {
        using var dir = new TempDir();
        var tracked = dir.Write("a.th", "survey a\nendsurvey\n");
        var thconfig = dir.Write("cave.thconfig", "encoding utf-8\nsource a.th\n");

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(dir.Path);
        Assert.True(await session.SetActiveThconfigAsync(thconfig));
        Assert.True(session.Covers(tracked));

        var reported = new List<ExternalFileChange>();
        var hookReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ExternalFileChanged += (_, e) => { lock (reported) reported.Add(e); hookReported.TrySetResult(); };

        // The IDE's own save. Wait until the watcher has delivered its event, so the next write is a
        // separate event and not one the watcher's debounce merges into the save (that merge is a
        // known limit of the mark, not what this test is about).
        session.SuppressSelfWrite(tracked);
        File.WriteAllText(tracked, "survey a\n# saved by the IDE\nendsurvey\n");
        await WaitUntil(() => !session.IsSelfWriteUnseen(tracked));
        Assert.False(session.IsSelfWriteUnseen(tracked), "the watcher never delivered the IDE's own save");
        // Only the write after the save counts. (Should the mark have expired before its event came, the
        // save itself was reported; that is not the behaviour under test.)
        lock (reported) reported.Clear();
        hookReported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Somebody else rewrites the file right after (a post-save hook, an agent's edit).
        File.WriteAllText(tracked, "survey a\n# rewritten by a hook\nendsurvey\n");
        await Task.WhenAny(hookReported.Task, Task.Delay(Patience));

        lock (reported)
            Assert.True(reported.Count > 0, "the external write after the IDE's own save raised no ExternalFileChanged");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Patience;
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(20);
    }
}
