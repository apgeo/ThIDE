using System.Reflection;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// On macOS the explorer promises that a deleted item can be restored from the Trash (Finder is the
/// trash backend). When the Finder helper fails, the item must stay where it was rather than being
/// removed for good. The macOS state is reproduced on Linux: the Finder route is switched on and
/// `osascript` is a stub on PATH that always fails.
/// </summary>
public class FinderTrashFailureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-finder-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Delete_keeps_the_folder_when_the_finder_helper_fails()
    {
        if (OperatingSystem.IsWindows()) return;

        var bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        var osascript = Path.Combine(bin, "osascript");
        File.WriteAllText(osascript, "#!/bin/sh\nexit 1\n");
        File.SetUnixFileMode(osascript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var original = Environment.GetEnvironmentVariable("PATH");
        UnixFileOperations ops;
        // Only the stub directory on PATH: no gio / trash-cli is found, as on a Mac.
        Environment.SetEnvironmentVariable("PATH", bin);
        try { ops = new UnixFileOperations(); }
        finally { Environment.SetEnvironmentVariable("PATH", original); }
        typeof(UnixFileOperations).GetField("_useFinder", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(ops, true);
        Assert.True(ops.DeleteIsUndoable, "precondition: with Finder as the backend the UI promises an undoable delete");

        var survey = Path.Combine(_root, "survey");
        Directory.CreateDirectory(survey);
        File.WriteAllText(Path.Combine(survey, "main.th"), "survey main\nendsurvey\n");

        bool result;
        Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + original);
        try { result = ops.Delete(survey); }
        finally { Environment.SetEnvironmentVariable("PATH", original); }

        Assert.True(Directory.Exists(survey), "the folder was permanently deleted although the Finder trash failed and the user was told it could be restored");
        Assert.False(result);
    }
}
