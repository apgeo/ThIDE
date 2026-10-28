using Therion.Workspace.Editing;
using Therion.Workspace.Templates;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class NewFileViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "new-file-vm");
    private static string P(string name) => Path.GetFullPath(Path.Combine(Root, name));

    private sealed class FakeEditor : IWorkspaceEditService
    {
        public EditBatch? Last;
        public Task<EditOutcome> ApplyAsync(EditBatch batch, CancellationToken ct = default)
        {
            Last = batch;
            return Task.FromResult(new EditOutcome([], null, []));
        }
    }

    private static (NewFileViewModel Vm, FakeEditor Editor, List<(string, int)> Opened) Make(HashSet<string>? existing = null, string? folder = null)
    {
        existing ??= [];
        var files = new Dictionary<string, string> { [P("trips/index.th")] = "survey trips\n  input a.th\nendsurvey\n", [P("main.th")] = "survey cave\nendsurvey\n" };
        var editor = new FakeEditor();
        var opened = new List<(string, int)>();
        var env = new NewFileEnvironment(FileTemplateCatalog.BuiltIns, folder ?? P("trips"), Root,
            [P("main.th"), P("trips/index.th")], new HashSet<string> { P("trips/index.th") },
            p => files.GetValueOrDefault(Path.GetFullPath(p)), existing.Contains,
            v => v == "DATE" ? "2026.10.28" : null, editor,
            (p, c) => { opened.Add((p, c)); return Task.CompletedTask; }, null);
        return (new NewFileViewModel(env), editor, opened);
    }

    [Fact]
    public void Defaults_to_the_first_template_a_free_name_and_the_folders_input_parent()
    {
        var (vm, _, _) = Make(existing: [P("trips/trip.th")]);

        Assert.Equal("th-trip", vm.SelectedTemplate!.Template.Id);
        Assert.Equal("trip-2.th", vm.FileName);
        Assert.Equal(P("trips/index.th"), vm.SelectedInputTarget!.Path);
        Assert.Contains("survey trip-2", vm.Preview);
        Assert.True(vm.CanCreate);
    }

    [Fact]
    public void Switching_template_changes_the_suggested_name_until_the_user_types_one()
    {
        var (vm, _, _) = Make();

        vm.SelectedTemplate = vm.Templates.Single(t => t.Template.Id == "thconfig-basic");
        Assert.Equal("thconfig.thconfig", vm.FileName);

        vm.FileName = "cave.thconfig";
        vm.SelectedTemplate = vm.Templates.Single(t => t.Template.Id == "blank");
        Assert.Equal("cave.th", vm.FileName); // the typed stem stays, the extension follows the template
    }

    [Fact]
    public async Task Create_writes_the_file_and_the_input_then_opens_it_at_the_caret()
    {
        var (vm, editor, opened) = Make();

        Assert.Null(await vm.CreateAsync());

        Assert.Equal(P("trips/trip.th"), Assert.Single(editor.Last!.Creates).FilePath);
        Assert.Contains("input trip.th", TextSplicer.Splice("survey trips\n  input a.th\nendsurvey\n", editor.Last.Edits.Single().Edits));
        Assert.Equal(P("trips/trip.th"), opened.Single().Item1);
    }

    [Fact]
    public void An_existing_target_cannot_be_created()
    {
        var (vm, _, _) = Make(existing: [P("trips/mine.th")]);

        vm.FileName = "mine.th";

        Assert.False(vm.CanCreate);
        Assert.NotEmpty(vm.Message);
    }

    [Fact]
    public void A_typed_name_takes_the_new_templates_extension()
    {
        var (vm, _, _) = Make();
        vm.FileName = "north.th";

        vm.SelectedTemplate = vm.Templates.Single(t => t.Template.Id == "thconfig-basic");

        Assert.Equal("north.thconfig", vm.FileName);
        vm.FileName = "cave2";
        Assert.EndsWith("cave2.thconfig", vm.TargetPath);
    }

    [Fact]
    public void A_picked_input_target_survives_folder_edits()
    {
        var (vm, _, _) = Make();
        vm.SelectedInputTarget = vm.InputTargets.Single(t => t.Path == P("main.th"));

        vm.Folder = P("trips/sub");

        Assert.Equal(P("main.th"), vm.SelectedInputTarget!.Path);
    }
}
