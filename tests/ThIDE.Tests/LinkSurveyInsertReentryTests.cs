using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

/// <summary>
/// Insert in the link dialog stays enabled while the edit service is still applying (the apply
/// awaits a full workspace reload before it returns). A second Insert during that time must not
/// splice the same plan into the file a second time.
/// </summary>
public class LinkSurveyInsertReentryTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "link-reentry", "cave");
    private static string P(string name) => Path.GetFullPath(Path.Combine(Root, name));

    private const string Leg = "  centreline\n    data normal from to length compass clino\n    1 2 10 0 0\n  endcentreline\n";

    /// <summary>
    /// Writes the batch into an in-memory file set the way the real service writes to disk, then
    /// holds the call open (the real one awaits the workspace reload here) until released.
    /// </summary>
    private sealed class SlowEditor(Dictionary<string, string> files) : IWorkspaceEditService
    {
        public readonly TaskCompletionSource ReloadDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Applies;

        public async Task<EditOutcome> ApplyAsync(EditBatch batch, CancellationToken ct = default)
        {
            Applies++;
            var check = EditBatchChecker.Check(batch, p => files.GetValueOrDefault(Path.GetFullPath(p)), p => files.ContainsKey(Path.GetFullPath(p)), p => files.ContainsKey(Path.GetFullPath(p)));
            if (check.Problems.Count > 0) return new EditOutcome(check.Problems, null, []);
            foreach (var (path, text) in check.NewTexts) files[Path.GetFullPath(path)] = text;
            await ReloadDone.Task;
            return new EditOutcome([], null, check.NewTexts.Keys.ToList());
        }
    }

    private static (LinkSurveyViewModel Vm, SlowEditor Editor, Dictionary<string, string> Files) Make()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [P("main.th")] = "survey cave\n  input a.th\n  equate 1@a 1@a\nendsurvey\n",
            [P("a.th")] = "survey a\n" + Leg + "endsurvey\n",
            // Already inside the project folder: linked in place, so the batch holds edits only.
            [P("trips/new.th")] = "survey trip\n" + Leg + "endsurvey\n",
        };
        var parsed = files.Where(kv => !kv.Key.EndsWith("new.th", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value), StringComparer.OrdinalIgnoreCase);
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        var editor = new SlowEditor(files);
        var env = new LinkSurveyEnvironment(ws, Root, p => files.GetValueOrDefault(Path.GetFullPath(p)), _ => [],
            (_, _) => false, editor, null, 0, new DateOnly(2026, 10, 28));
        return (new LinkSurveyViewModel(env), editor, files);
    }

    [Fact]
    public async Task Second_insert_while_the_first_is_applying_does_nothing()
    {
        var (vm, editor, files) = Make();
        await vm.LoadAsync(P("trips/new.th"));
        Assert.True(vm.SourceSelector.Picker.SelectByPayload(p => p.ToString()!.Contains("trip.1")));
        Assert.True(vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2")));
        Assert.False(vm.CopySource);
        Assert.True(vm.CanInsert);

        var first = vm.InsertAsync();
        Assert.True(vm.IsBusy);
        var second = vm.InsertAsync();   // Insert is still enabled (IsDefault + Enter, or a double click)
        editor.ReloadDone.SetResult();
        await Task.WhenAll(first, second);

        var main = files[P("main.th")];
        int inputs = main.Split("input trips/new.th").Length - 1;
        Assert.Equal(1, inputs);
        Assert.Equal(1, editor.Applies);
    }
}
