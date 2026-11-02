namespace Therion.Workspace.Tests;

/// <summary>
/// Live reload: when a tracked file changes on disk, the workspace must follow the file's new
/// include graph, not only re-parse the one file. Otherwise a hand-written `input` line leaves the
/// new file out of the model until the thconfig is reactivated.
/// </summary>
public class WorkspaceLiveReloadTests
{
    [Fact]
    public async Task Changing_a_file_to_input_a_new_file_loads_it()
    {
        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "thconfig");
        var a = Path.Combine(dir.Path, "a.th");
        var b = Path.Combine(dir.Path, "b.th");
        await File.WriteAllTextAsync(cfg, "source a.th\n");
        await File.WriteAllTextAsync(a, "survey a\nendsurvey\n");
        await File.WriteAllTextAsync(b, "survey b\nendsurvey\n");

        await using var ws = new TherionWorkspace(options: new WorkspaceOptions { DisableDiskCache = true });
        await ws.LoadAsync(cfg);
        Assert.DoesNotContain(ws.LoadedFiles, p => string.Equals(p, Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase));

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ws.WorkspaceChanged += (_, _) => changed.TrySetResult();

        await File.WriteAllTextAsync(a, "input b.th\nsurvey a\nendsurvey\n");

        var fired = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(15))) == changed.Task;
        Assert.True(fired, "WorkspaceChanged did not fire after the tracked file was rewritten");

        // Give any follow-up loading a moment, so a fix that loads the new target asynchronously
        // after raising the event is not failed on timing alone.
        await Task.Delay(500);

        Assert.Contains(ws.LoadedFiles, p => string.Equals(p, Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Removing_an_input_drops_the_file_it_pulled_in()
    {
        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "thconfig");
        var a = Path.Combine(dir.Path, "a.th");
        var b = Path.Combine(dir.Path, "b.th");
        var c = Path.Combine(dir.Path, "c.th");
        await File.WriteAllTextAsync(cfg, "source a.th\n");
        await File.WriteAllTextAsync(a, "input b.th\nsurvey a\nendsurvey\n");
        await File.WriteAllTextAsync(b, "input c.th\nsurvey b\nendsurvey\n");
        await File.WriteAllTextAsync(c, "survey c\nendsurvey\n");

        await using var ws = new TherionWorkspace(options: new WorkspaceOptions { DisableDiskCache = true });
        await ws.LoadAsync(cfg);
        Assert.Contains(ws.LoadedFiles, p => Same(p, c));

        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ws.WorkspaceChanged += (_, _) => changed.TrySetResult();
        await File.WriteAllTextAsync(a, "survey a\nendsurvey\n");
        Assert.True(await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(15))) == changed.Task,
            "WorkspaceChanged did not fire after the tracked file was rewritten");

        Assert.DoesNotContain(ws.LoadedFiles, p => Same(p, b));
        Assert.DoesNotContain(ws.LoadedFiles, p => Same(p, c));   // reached only through b
        Assert.Contains(ws.LoadedFiles, p => Same(p, a));

        // Undoing a buffer overlay of the dropped file must not bring it back.
        ws.ReloadFileFromDisk(b);
        Assert.DoesNotContain(ws.LoadedFiles, p => Same(p, b));
    }

    private static bool Same(string loaded, string path)
        => string.Equals(loaded, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
}
