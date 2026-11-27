// Tools ▸ Export as a single .th file, as the view model runs it after the options dialog. INVENTED
// survey data only.

using Therion.Workspace.Flatten;
using ThIDE.Services;

namespace ThIDE.Tests;

public sealed class ExportSingleThTests
{
    private sealed class SavePicker(string path) : IStoragePicker
    {
        public string? StartFolder { get; private set; }
        public Task<string?> PickOpenFileAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenThconfigAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string? startFolder = null)
        {
            StartFolder = startFolder;
            return Task.FromResult<string?>(path);
        }
        public Task<string?> PickOpenLayoutAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLayoutAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
    }

    private static IWorkspaceSession SessionFor(string cfg) =>
        WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name switch
        {
            "get_ActiveThconfig" => new ThconfigCandidate(cfg, Path.GetFileName(cfg), false),
            "get_RootPath" => Path.GetDirectoryName(cfg),
            "get_Candidates" => new List<ThconfigCandidate> { new(cfg, Path.GetFileName(cfg), false) },
            _ => null,
        });

    [Fact]
    public Task Merges_the_project_with_the_editors_unsaved_text_and_writes_the_names_too() => UiThread.RunAsync(async () =>
    {
        using var dir = new TempDir();
        var cfg = dir.Write("thconfig", "source cave.th\n");
        var cave = dir.Write("cave.th", "survey cave\n  centreline\n    0 1 10 0 0\n  endcentreline\nendsurvey\n");
        var output = Path.Combine(dir.Path, "out", "cave-single.th");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var documents = new ScriptedDocumentService();
        // An edit not saved yet: the export takes the editor's text, not the file's.
        documents.OpenTextDocument(cave, "survey cave\n  centreline\n    0 1 10 0 0\n    1 2 7 90 0\n  endcentreline\nendsurvey\n");
        var vm = MainWindowViewModelHost.Create(documents, session: SessionFor(cfg));
        var picker = new SavePicker(output);
        vm.AttachStoragePicker(picker);

        await vm.ExportSingleThAsync(new FlattenOptions { Standalone = true }, writeStationNames: true);

        Assert.Equal(dir.Path, picker.StartFolder);
        var text = File.ReadAllText(output);
        Assert.Contains("survey cave", text);
        Assert.Matches(@"\n\s+1 2\s+7\.000\s+90\.000\s+0\.000\n", text);
        Assert.True(File.Exists(Path.ChangeExtension(output, ".names.csv")));
        Assert.StartsWith("Wrote cave-single.th", vm.StatusText);
    });

    [Fact]
    public void Without_a_project_it_asks_for_one_instead_of_opening_the_options()
    {
        var vm = MainWindowViewModelHost.Create(new ScriptedDocumentService());
        bool asked = false;
        vm.ExportSingleThRequested += (_, _) => asked = true;
        vm.ExportSingleThCommand.Execute(null);
        Assert.False(asked);
    }

    [Fact]
    public void The_default_survey_name_comes_from_the_project_folder_for_a_plain_thconfig()
    {
        using var dir = new TempDir();
        var cfg = dir.Write("thconfig", "source cave.th\n");
        var vm = MainWindowViewModelHost.Create(new ScriptedDocumentService(), session: SessionFor(cfg));
        string? suggested = null;
        vm.ExportSingleThRequested += (_, name) => suggested = name;
        vm.ExportSingleThCommand.Execute(null);
        Assert.Equal(Therion.Workspace.Import.ScaffoldValueRules.SafeSurveyName(Path.GetFileName(dir.Path)), suggested);
    }
}
