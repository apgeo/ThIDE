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
    [Fact]
    public async Task ReplaceAll_in_more_than_one_file_asks_first_and_a_no_writes_nothing()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "station A1\n");
        var b = dir.Write("b.th", "equate A1 A1\n");
        var vm = NewViewModel(dir.Path, mask: "*.th");
        var asked = new List<string>();
        vm.Confirm = q => { asked.Add(q); return Task.FromResult(false); };
        vm.Query = "A1";
        vm.Replacement = "B1";

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        var question = Assert.Single(asked);
        Assert.Contains("3", question);   // occurrences
        Assert.Contains("2", question);   // files
        Assert.Equal("station A1\n", File.ReadAllText(a));
        Assert.Equal("equate A1 A1\n", File.ReadAllText(b));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public async Task ReplaceAll_in_one_file_does_not_ask()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "station A1\n");
        dir.Write("b.th", "station C1\n");
        var vm = NewViewModel(dir.Path, mask: "*.th");
        vm.Confirm = _ => throw new InvalidOperationException("asked for a single file");
        vm.Query = "A1";
        vm.Replacement = "B1";

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.Equal("station B1\n", File.ReadAllText(a));
    }

    [Fact]
    public void ReplaceAll_is_disabled_while_Find_is_empty()
    {
        var vm = NewViewModel(Path.GetTempPath(), mask: "*.th");
        Assert.False(vm.ReplaceAllCommand.CanExecute(null));
        vm.Query = "A1";
        Assert.True(vm.ReplaceAllCommand.CanExecute(null));
        vm.Query = "";
        Assert.False(vm.ReplaceAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task After_ReplaceAll_and_Undo_the_query_is_kept_and_the_list_shows_the_files_as_they_are()
    {
        using var dir = new TempDir();
        dir.Write("a.th", "station A1\nstation A1\n");
        dir.Write("b.th", "equate A1 x\n");
        var vm = NewViewModel(dir.Path, mask: "*.th");
        vm.Confirm = _ => Task.FromResult(true);
        vm.Query = "A1";
        vm.Replacement = "B1";

        await vm.ReplaceAllCommand.ExecuteAsync(null);
        Assert.Equal("A1", vm.Query);
        Assert.Empty(vm.Results);   // nothing left of the query

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Results.Count);
        Assert.All(vm.Results, h => Assert.DoesNotContain("B1", h.Display));
    }

    [Fact]
    public async Task FindAll_counts_only_the_files_with_a_match()
    {
        using var dir = new TempDir();
        dir.Write("a.th", "station A1 A1 A1\n");
        for (int i = 0; i < 4; i++) dir.Write($"other{i}.th", "station C1\n");
        var vm = NewViewModel(dir.Path, mask: "*.th");
        vm.Query = "A1";

        await vm.FindAllCommand.ExecuteAsync(null);

        Assert.Equal("3 matches in 1 files", vm.Status);
    }

    [Fact]
    public async Task ReplaceAll_is_one_step_in_the_shared_workspace_history()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "station A1\n");
        var b = dir.Write("b.th", "equate A1 x\n");
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(
            m => m.Name == "get_Documents" ? new ObservableCollection<FileDocumentViewModel>() : null);
        var history = new WorkspaceUndoService(docs);
        var vm = new ReplaceInFilesViewModel(docs, history)
        {
            Scope = ReplaceInFilesViewModel.ScopeDirectory, Directory = dir.Path, FileMask = "*.th",
            Confirm = _ => Task.FromResult(true),
        };
        vm.Query = "A1";
        vm.Replacement = "B1";
        await vm.ReplaceAllCommand.ExecuteAsync(null);
        Assert.Equal(2, history.NextUndo!.Files.Count);

        Assert.Empty(vm.Results);

        await history.UndoAsync();   // Edit > Undo Replace in Files

        Assert.Equal("station A1\n", File.ReadAllText(a));
        Assert.Equal("equate A1 x\n", File.ReadAllText(b));
        Assert.True(vm.CanRedo);
        // The panel searched again: the original hits are back in its list.
        for (int i = 0; i < 100 && vm.Results.Count < 2; i++) await Task.Delay(20);
        Assert.Equal(2, vm.Results.Count);
    }

    [Fact]
    public async Task ReplaceAll_tells_the_file_watcher_about_each_file_it_writes()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "station A1\n");
        var b = dir.Write("b.th", "equate A1 x\n");
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(
            m => m.Name == "get_Documents" ? new ObservableCollection<FileDocumentViewModel>() : null);
        var suppressed = new List<string>();
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(
            _ => null, (m, args) => { if (m.Name == nameof(IWorkspaceSession.SuppressSelfWrite)) suppressed.Add((string)args[0]!); });
        var vm = new ReplaceInFilesViewModel(docs, null, session)
        {
            Scope = ReplaceInFilesViewModel.ScopeDirectory, Directory = dir.Path, FileMask = "*.th",
            Confirm = _ => Task.FromResult(true),
        };
        vm.Query = "A1";
        vm.Replacement = "B1";

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        Assert.Equal("station B1\n", File.ReadAllText(a));
        Assert.Equal(new[] { a, b }.OrderBy(x => x), suppressed.OrderBy(x => x));
    }
}
