// What a crashed run leaves behind (a lock nobody holds, its recovery copies) is detected and offered
// back by the next instance, including the single-folder layout written by earlier versions.

using ThIDE.Services;

namespace ThIDE.Tests;

public class CrashRecoveryCrashedRunTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-recovery-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private static readonly (string Path, string Text)[] UnsavedEdit =
        [("/invented/project/cave.th", "survey cave\nendsurvey\n# unsaved edit\n")];

    [Fact]
    public void A_crashed_runs_buffers_are_offered_and_cleared_once_handled()
    {
        // A run that autosaved and then died: its lock file is still there but no process holds it.
        var crashed = new CrashRecoveryService(_root);
        crashed.SaveBuffers(UnsavedEdit);
        File.WriteAllText(Path.Combine(_root, "running.4242-dead.lock"), "2026-11-03T08:00:00.0000000Z");

        var next = new CrashRecoveryService(_root);
        next.MarkRunning();

        Assert.True(next.PreviousRunCrashed);
        Assert.Equal(UnsavedEdit[0].Text, Assert.Single(next.GetRecoverable()).Text);

        next.ClearRecovery();
        Assert.Empty(next.GetRecoverable());
        next.MarkCleanShutdown();
        Assert.Empty(Directory.GetFiles(_root, "*.lock"));
    }

    [Fact]
    public void A_live_instances_copies_survive_another_instance_clearing_recovery()
    {
        var live = new CrashRecoveryService(_root);
        live.MarkRunning();
        live.SaveBuffers(UnsavedEdit);

        var other = new CrashRecoveryService(_root);
        other.MarkRunning();
        other.ClearRecovery();

        Assert.Single(live.GetRecoverable());
        live.MarkCleanShutdown();
        Assert.Empty(live.GetRecoverable());
    }

    [Fact]
    public void The_earlier_single_folder_layout_is_read_as_a_crashed_run()
    {
        var recovery = Directory.CreateDirectory(Path.Combine(_root, "recovery")).FullName;
        File.WriteAllText(Path.Combine(_root, "running.lock"), "2026-11-03T08:00:00.0000000Z");
        File.WriteAllText(Path.Combine(recovery, "cave.0123.bak"), UnsavedEdit[0].Text);
        File.WriteAllText(Path.Combine(recovery, "manifest.json"),
            """[{"OriginalPath":"/invented/project/cave.th","RecoveryFile":"cave.0123.bak","SavedUtc":"2026-11-03T08:00:00Z"}]""");

        var next = new CrashRecoveryService(_root);
        next.MarkRunning();

        Assert.True(next.PreviousRunCrashed);
        Assert.Equal("/invented/project/cave.th", Assert.Single(next.GetRecoverable()).OriginalPath);
        next.ClearRecovery();
        Assert.Empty(next.GetRecoverable());
        Assert.False(File.Exists(Path.Combine(_root, "running.lock")));
    }
}
