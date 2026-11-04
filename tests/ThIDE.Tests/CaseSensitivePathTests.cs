// On Linux two files whose names differ only by case are two files: the shell must not open one in
// the other's tab, or plan one's rename against the other's text.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Tests;

public class CaseSensitivePathTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    [Fact]
    public async Task Files_differing_only_by_case_open_in_their_own_tabs()
    {
        if (!OperatingSystem.IsLinux()) return;   // case-insensitive file systems: they are one file
        using var dir = new TempDir();
        var upper = dir.Write("Cave.th", "survey upper\nendsurvey\n");
        var lower = dir.Write("cave.th", "survey lower\nendsurvey\n");
        var svc = new DocumentService(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

        await svc.OpenFileAsync(upper);
        await svc.OpenFileAsync(lower);

        Assert.Equal(2, svc.Documents.Count);
        Assert.Contains("lower", svc.Documents.Single(d => d.FilePath == lower).DocumentText);
        Assert.Contains("upper", svc.Documents.Single(d => d.FilePath == upper).DocumentText);
    }

    [Fact]
    public async Task Comment_rename_plans_each_case_variant_against_its_own_text()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var dir = new TempDir();
        var upper = dir.Write("Cave.th", "survey upper\nendsurvey\n# st1 upper\n");
        var lower = dir.Write("cave.th", "survey lower\nendsurvey\n# st1 lower\n");
        var cfg = dir.Write("thconfig", "source cave.th\n");

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);
        var model = ws.BuildSemanticModel();
        Assert.Contains(lower, model.PerFile.Keys);   // precondition: cave.th is the project's file

        // Cave.th (outside the project) is the active document, with an unsaved edit in its buffer.
        var active = new FileDocumentViewModel(upper, "survey upper\nendsurvey\n\n# st1 upper, edited\n", new MeasurementsViewModel());
        var changes = RenamePlanner.CollectCommentRenameChanges(model, active, "st1", new List<RenameFileChanges>());

        Assert.Equal(active.DocumentText, changes.Single(c => c.FilePath == upper).FileText);
        Assert.Equal(File.ReadAllText(lower), changes.Single(c => c.FilePath == lower).FileText);
    }
}
