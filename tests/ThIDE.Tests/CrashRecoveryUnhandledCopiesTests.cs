using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// The copies a crashed run left behind are offered at the next start. A later clean exit of that
/// next run, in which the user neither restored nor dismissed them, must not delete them.
/// </summary>
public class CrashRecoveryUnhandledCopiesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-recovery-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    [Fact]
    public void A_crashed_runs_copies_survive_a_clean_exit_that_did_not_restore_them()
    {
        const string text = "survey cave\nendsurvey\n# unsaved edit\n";
        var crashed = new CrashRecoveryService(_root);
        crashed.SaveBuffers([("/invented/project/cave.th", text)]);
        File.WriteAllText(Path.Combine(_root, "running.4242-dead.lock"), "2026-11-03T08:00:00.0000000Z");

        var next = new CrashRecoveryService(_root);
        next.MarkRunning();
        Assert.Single(next.GetRecoverable()); // offered; the user does not click Restore
        next.MarkCleanShutdown();             // nothing dirty in this run: a plain clean exit

        var third = new CrashRecoveryService(_root);
        third.MarkRunning();
        var offered = third.GetRecoverable();
        third.MarkCleanShutdown(keepRecovery: true);

        Assert.True(offered.Count == 1, "the crashed run's unsaved edit was deleted by a clean exit in which the user never restored or dismissed it");
        Assert.Equal(text, offered[0].Text);
    }
}
