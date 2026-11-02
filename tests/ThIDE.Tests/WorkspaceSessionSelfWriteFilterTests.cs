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
        session.ExternalFileChanged += (_, e) => { lock (reported) reported.Add(e); };

        // The IDE's own save.
        session.SuppressSelfWrite(tracked);
        File.WriteAllText(tracked, "survey a\n# saved by the IDE\nendsurvey\n");
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Somebody else rewrites the file right after (a post-save hook, an agent's edit).
        File.WriteAllText(tracked, "survey a\n# rewritten by a hook\nendsurvey\n");
        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        lock (reported)
            Assert.True(reported.Count > 0, "the external write after the IDE's own save raised no ExternalFileChanged");
    }
}
