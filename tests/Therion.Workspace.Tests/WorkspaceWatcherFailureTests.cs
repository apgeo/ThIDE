namespace Therion.Workspace.Tests;

/// <summary>
/// Runs alone: it deliberately uses up the user's inotify instances for a moment, and any other
/// test creating a workspace at the same time would fail for that reason instead of its own.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class InotifyExhaustionCollection
{
    public const string Name = "inotify exhaustion";
}

/// <summary>
/// The live-reload watcher is optional: a workspace must still load when a directory cannot be
/// watched. On Linux every <see cref="FileSystemWatcher"/> is one inotify instance, and the
/// per-user limit is shared with every other program the user runs.
/// </summary>
[Collection(InotifyExhaustionCollection.Name)]
public class WorkspaceWatcherFailureTests
{
    [Fact]
    public async Task Load_completes_when_the_directory_cannot_be_watched()
    {
        if (!OperatingSystem.IsLinux()) return;   // the exhaustion below is an inotify behaviour

        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "thconfig");
        var th = Path.Combine(dir.Path, "cave.th");
        await File.WriteAllTextAsync(cfg, "source cave.th\n");
        await File.WriteAllTextAsync(th, "survey cave\nendsurvey\n");

        var hogs = new List<FileSystemWatcher>();
        try
        {
            int limit = int.Parse(File.ReadAllText("/proc/sys/fs/inotify/max_user_instances").Trim());
            bool exhausted = false;
            for (int i = 0; i < limit + 16 && !exhausted; i++)
            {
                try { hogs.Add(new FileSystemWatcher(dir.Path) { EnableRaisingEvents = true }); }
                catch (IOException) { exhausted = true; }
            }
            Assert.True(exhausted, "could not use up the inotify instances; the condition was not reproduced");

            await using var ws = new TherionWorkspace(options: new WorkspaceOptions { DisableDiskCache = true });
            var error = await Record.ExceptionAsync(() => ws.LoadAsync(cfg).AsTask());

            Assert.True(error is null, "LoadAsync failed because the directory could not be watched: " + error?.Message);
            Assert.Contains(ws.LoadedFiles, p => string.Equals(p, Path.GetFullPath(th), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            foreach (var hog in hogs) hog.Dispose();
        }
    }
}
