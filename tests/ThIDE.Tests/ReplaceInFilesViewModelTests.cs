using System.Collections.ObjectModel;
using System.Diagnostics;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

/// <summary>
/// Replace in Files, driven through its view model against a throwaway directory: what Replace All
/// writes, what Undo writes back, and whether Find All and Replace All agree on the matches.
/// </summary>
public class ReplaceInFilesViewModelTests
{
    private static ReplaceInFilesViewModel NewViewModel(
        string directory, string mask, ObservableCollection<FileDocumentViewModel>? open = null)
    {
        var docs = open ?? new ObservableCollection<FileDocumentViewModel>();
        var service = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(
            m => m.Name == "get_Documents" ? docs : null);
        return new ReplaceInFilesViewModel(service)
        {
            Scope = ReplaceInFilesViewModel.ScopeDirectory,
            Directory = directory,
            FileMask = mask,
        };
    }

    private static bool CaseSensitiveFileSystemAvailable() => OperatingSystem.IsLinux();

    private static bool CanMakeUnreadableFolder() =>
        OperatingSystem.IsLinux() && Environment.UserName != "root";

    private static void Chmod(string path, string mode)
    {
        using var p = Process.Start(new ProcessStartInfo("chmod", $"{mode} \"{path}\"") { UseShellExecute = false });
        p!.WaitForExit();
    }

    [Fact]
    public async Task ReplaceAll_DirectoryScopeWithEmptyMask_LeavesBinaryFilesUntouched()
    {
        using var dir = new TempDir();
        dir.Write("a.th", "survey A1\n");
        // A PNG-like header followed by bytes that are not valid UTF-8, then the query text.
        var binary = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF, 0xFE, (byte)'A', (byte)'1', 0xC3 };
        var png = Path.Combine(dir.Path, "scan.png");
        File.WriteAllBytes(png, binary);

        var vm = NewViewModel(dir.Path, mask: "");
        vm.Query = "A1";
        vm.Replacement = "B1";
        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.Equal("survey B1\n", File.ReadAllText(Path.Combine(dir.Path, "a.th")));
        Assert.Equal(binary, File.ReadAllBytes(png));
    }

    [Fact]
    public async Task Undo_AfterFileChangedOnDisk_KeepsTheLaterEdit()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "survey cave1\n");

        var vm = NewViewModel(dir.Path, mask: "*.th");
        vm.Query = "cave1";
        vm.Replacement = "cave2";
        await vm.ReplaceAllCommand.ExecuteAsync(null);
        Assert.Equal("survey cave2\n", File.ReadAllText(a));

        // Work done after the replace, outside the panel.
        File.AppendAllText(a, "station 10 extra\n");

        vm.UndoCommand.Execute(null);

        Assert.Contains("station 10 extra", File.ReadAllText(a));
    }

    [Fact]
    public async Task FindAll_DirectoryWithUnreadableSubfolder_CompletesWithTheReadableMatches()
    {
        if (!CanMakeUnreadableFolder()) return;

        using var dir = new TempDir();
        dir.Write("a.th", "survey A1\n");
        dir.Write(Path.Combine("locked", "b.th"), "survey A1\n");
        var locked = Path.Combine(dir.Path, "locked");
        Chmod(locked, "000");
        try
        {
            var vm = NewViewModel(dir.Path, mask: "*.th");
            vm.Query = "A1";

            var error = await Record.ExceptionAsync(() => vm.FindAllCommand.ExecuteAsync(null));

            Assert.Null(error);
            Assert.Single(vm.Results);
        }
        finally
        {
            Chmod(locked, "755");
        }
    }

    [Fact]
    public async Task ReplaceAll_AnchoredRegex_ReplacesEveryMatchFindAllListed()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "fix a\nfix b\n");

        var vm = NewViewModel(dir.Path, mask: "*.th");
        vm.UseRegex = true;
        vm.Query = "^fix";
        vm.Replacement = "x";

        await vm.FindAllCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.Equal("x a\nx b\n", File.ReadAllText(a));
    }

    [Fact]
    public async Task ReplaceAll_QuerySpanningTwoLines_ChangesNothingFindAllDidNotList()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "fix a\nfix b\n");

        var vm = NewViewModel(dir.Path, mask: "*.th");
        vm.Query = "a\nfix";
        vm.Replacement = "JOINED";

        await vm.FindAllCommand.ExecuteAsync(null);
        int previewed = vm.Results.Count;

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.True(previewed > 0 || File.ReadAllText(a) == "fix a\nfix b\n",
            $"Find All listed {previewed} matches but Replace All changed the file to {File.ReadAllText(a).Replace("\n", "\\n")}");
    }

    [Fact]
    public async Task ReplaceAll_CaseVariantFileNames_EachFileKeepsItsOwnContent()
    {
        if (!CaseSensitiveFileSystemAvailable()) return;

        using var dir = new TempDir();
        var upper = dir.Write("Cave.th", "station Cave A1\n");
        var lower = dir.Write("cave.th", "station cave A1\n");
        Assert.True(File.Exists(upper) && File.Exists(lower));

        // cave.th is open in the editor with unsaved text; Cave.th is not open.
        var open = new ObservableCollection<FileDocumentViewModel>
        {
            new(lower, "station cave OPEN A1\n", new MeasurementsViewModel()),
        };

        var vm = NewViewModel(dir.Path, mask: "*.th", open);
        vm.Query = "A1";
        vm.Replacement = "B1";
        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.Equal("station Cave B1\n", File.ReadAllText(upper));
        Assert.Equal("station cave OPEN B1\n", open[0].DocumentText);
    }
}
