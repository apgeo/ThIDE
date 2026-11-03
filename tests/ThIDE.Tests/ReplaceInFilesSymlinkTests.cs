using System.Collections.ObjectModel;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

/// <summary>Replace in Files over a directory that contains a symlink to a folder outside it.</summary>
public class ReplaceInFilesSymlinkTests
{
    [Fact]
    public async Task ReplaceAll_DirectoryScope_DoesNotRewriteFilesBehindADirectorySymlink()
    {
        if (OperatingSystem.IsWindows()) return;   // directory symlinks need privileges there

        using var project = new TempDir();
        using var elsewhere = new TempDir();
        project.Write("a.th", "survey A1\n");
        var outside = elsewhere.Write("other.th", "survey A1\n");
        Directory.CreateSymbolicLink(Path.Combine(project.Path, "extra"), elsewhere.Path);

        var docs = new ObservableCollection<FileDocumentViewModel>();
        var service = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(
            m => m.Name == "get_Documents" ? docs : null);
        var vm = new ReplaceInFilesViewModel(service)
        {
            Scope = ReplaceInFilesViewModel.ScopeDirectory,
            Directory = project.Path,
            FileMask = "*.th",
            Query = "A1",
            Replacement = "B1",
        };

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.Equal("survey B1\n", File.ReadAllText(Path.Combine(project.Path, "a.th")));
        Assert.True(File.ReadAllText(outside) == "survey A1\n",
            "Replace All rewrote a file outside the directory, reached through a symlink inside it");
    }
}
