using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// When a trash helper is present the explorer tells the user the delete is undoable. A failing
/// trash helper must therefore not turn the operation into a permanent delete behind their back.
/// Linux only: the service resolves `gio` on PATH, which the test replaces with a stub that fails.
/// </summary>
public class UnixFileOperationsTrashFailureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-trash-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private UnixFileOperations MakeWithFailingTrash()
    {
        // A `gio` stub that always exits 1, placed first on PATH for the constructor's probe.
        var bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        var gio = Path.Combine(bin, "gio");
        File.WriteAllText(gio, "#!/bin/sh\nexit 1\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(gio, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", bin + Path.PathSeparator + original);
        try { return new UnixFileOperations(); }
        finally { Environment.SetEnvironmentVariable("PATH", original); }
    }

    [Fact]
    public void Delete_keeps_the_folder_when_the_trash_helper_fails()
    {
        if (!OperatingSystem.IsLinux()) return;

        var ops = MakeWithFailingTrash();
        Assert.True(ops.DeleteIsUndoable, "precondition: the stub trash helper was found, so the UI promises an undoable delete");

        var survey = Path.Combine(_root, "survey");
        Directory.CreateDirectory(survey);
        File.WriteAllText(Path.Combine(survey, "main.th"), "survey main\nendsurvey\n");

        bool result = ops.Delete(survey);

        Assert.True(Directory.Exists(survey), "the folder was permanently deleted although the trash helper failed and the user was told it could be restored");
        Assert.False(result);
    }
}
