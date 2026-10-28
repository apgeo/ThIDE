using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

/// <summary>The link dialog's state over an invented two-file project and a new trip.</summary>
public class LinkSurveyViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "link-vm", "cave");
    private static string P(string name) => Path.GetFullPath(Path.Combine(Root, name));

    private const string Leg = "  centreline\n    data normal from to length compass clino\n    1 2 10 0 0\n  endcentreline\n";

    private sealed class FakeEditor : IWorkspaceEditService
    {
        public EditBatch? Last;
        public Task<EditOutcome> ApplyAsync(EditBatch batch, CancellationToken ct = default)
        {
            Last = batch;
            return Task.FromResult(new EditOutcome([], null, batch.Edits.Select(e => e.FilePath).ToList()));
        }
    }

    private static (LinkSurveyViewModel Vm, FakeEditor Editor) Make(string? currentFile = null, string sourcePath = "../incoming/new.th")
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [P("main.th")] = "survey cave\n  input a.th\n  equate 1@a 1@a\nendsurvey\n",
            [P("a.th")] = "survey a\n" + Leg + "endsurvey\n",
            [P(sourcePath)] = "survey trip\n" + Leg + "endsurvey\n",
        };
        var parsed = files.Where(kv => !kv.Key.Contains("incoming") && !kv.Key.EndsWith("new.th"))
            .ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value), StringComparer.OrdinalIgnoreCase);
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        var editor = new FakeEditor();
        var env = new LinkSurveyEnvironment(ws, Root, p => files.GetValueOrDefault(Path.GetFullPath(p)), _ => [],
            (_, _) => false, editor, currentFile, 0, new DateOnly(2026, 10, 28));
        return (new LinkSurveyViewModel(env), editor);
    }

    private static async Task<LinkSurveyViewModel> Ready(LinkSurveyViewModel vm, string source = "../incoming/new.th", string? preset = null)
    {
        await vm.LoadAsync(P(source), preset);
        Assert.True(vm.SourceSelector.Picker.SelectByPayload(p => p.ToString()!.Contains("trip.1")));
        Assert.True(vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2")));
        return vm;
    }

    [Fact]
    public async Task Choosing_both_stations_generates_the_lines_into_the_suggested_file()
    {
        var (vm, _) = Make();
        await Ready(vm);

        Assert.Equal(P("main.th"), vm.SelectedDestination!.Candidate.FilePath);
        Assert.True(vm.CopySource); // the source lives outside the project
        Assert.Contains("input new.th", vm.GeneratedText); // copied beside the block's other inputs
        Assert.Contains("equate 1@trip 2@a", vm.GeneratedText);
        Assert.True(vm.CanInsert);
    }

    [Fact]
    public async Task Insert_hands_the_plan_to_the_edit_service()
    {
        var (vm, editor) = Make();
        await Ready(vm);
        EditOutcome? applied = null;
        vm.Applied += (_, o) => applied = o;

        await vm.InsertAsync();

        Assert.NotNull(applied);
        Assert.True(editor.Last!.RequiresWorkspaceReload);
        Assert.Equal(P("new.th"), Assert.Single(editor.Last.Copies).DestinationPath);
    }

    [Fact]
    public async Task Source_inside_the_project_is_input_in_place()
    {
        var (vm, _) = Make(sourcePath: "trips/new.th");
        await Ready(vm, "trips/new.th");

        Assert.False(vm.CopySource);
        Assert.Contains("input trips/new.th", vm.GeneratedText);
    }

    [Fact]
    public async Task Current_file_switch_is_offered_only_when_it_can_host_the_lines()
    {
        var (vm, _) = Make(currentFile: P("main.th"));
        await Ready(vm);
        Assert.True(vm.CanUseCurrentFile);

        var (other, _) = Make(currentFile: P("a.th"));
        await Ready(other);
        Assert.False(other.CanUseCurrentFile); // a.th sits inside cave.a; the equate must be in cave
    }

    [Fact]
    public async Task Comment_carries_the_date_and_the_source_name()
    {
        var (vm, _) = Make();
        await Ready(vm);

        vm.AddComment = true;

        Assert.Contains("# Added on 2026.10.28 from source file new.th", vm.GeneratedText);
    }

    [Fact]
    public async Task Preset_destination_from_the_editor_comes_first()
    {
        var (vm, _) = Make(currentFile: P("main.th"));
        await Ready(vm, preset: P("main.th"));

        Assert.True(vm.UseCurrentFile);
        Assert.Equal(P("main.th"), vm.SelectedDestination!.Candidate.FilePath);
    }
}
