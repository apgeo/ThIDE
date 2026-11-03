// Two ThIDE processes (e.g. a file opened from the OS file association while the app is already
// running) each run their own crash-recovery service against the same profile folder. The sentinel
// and the recovery copies must not be mistaken for, or wiped by, another live instance.

using ThIDE.Services;

namespace ThIDE.Tests;

public class CrashRecoveryInstanceIsolationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-recovery-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private static readonly (string Path, string Text)[] UnsavedEdit =
        [("/invented/project/cave.th", "survey cave\nendsurvey\n# unsaved edit\n")];

    [Fact]
    public void Second_running_instance_is_not_reported_as_a_crash_of_the_first()
    {
        var first = new CrashRecoveryService(_root);
        first.MarkRunning();
        first.SaveBuffers(UnsavedEdit);

        var second = new CrashRecoveryService(_root);
        second.MarkRunning();

        Assert.False(second.PreviousRunCrashed,
            "a second instance started while the first still runs is treated as an unclean shutdown of the first");
        Assert.Empty(second.GetRecoverable());
    }

    [Fact]
    public void Clean_shutdown_of_a_second_instance_keeps_the_first_instances_recovery_copies()
    {
        var first = new CrashRecoveryService(_root);
        first.MarkRunning();
        first.SaveBuffers(UnsavedEdit);

        var second = new CrashRecoveryService(_root);
        second.MarkRunning();
        second.MarkCleanShutdown();

        Assert.Single(first.GetRecoverable());
    }
}
