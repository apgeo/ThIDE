using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Linking;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class NewTripViewModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "new-trip-vm");
    internal static string P(string name) => Path.GetFullPath(Path.Combine(Root, name));

    internal sealed class FakeEditor : IWorkspaceEditService
    {
        public EditBatch? Last;
        public Task<EditOutcome> ApplyAsync(EditBatch batch, CancellationToken ct = default)
        {
            Last = batch;
            return Task.FromResult(new EditOutcome([], null, []));
        }
    }

    internal static Task<(NewTripViewModel, FakeEditor, Dictionary<string, string>)> Make() => Make(new()
    {
        [P("main.th")] = "survey cave\n  input trips/a.th\n  equate 1@a 1@a\nendsurvey\n",
        [P("trips/a.th")] = "survey a\n  centreline\n    date 2026.09.01\n    team \"Ana\"\n    units length meters compass grads\n    data normal from to length compass clino\n    1 2 5 0 0\n  endcentreline\nendsurvey\n",
    });

    private static async Task<(NewTripViewModel, FakeEditor, Dictionary<string, string>)> Make(Dictionary<string, string> files)
    {
        var parsed = files.ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value));
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        var editor = new FakeEditor();
        var vm = new NewTripViewModel(new NewTripEnvironment(ws, Root, p => files.GetValueOrDefault(Path.GetFullPath(p)),
            p => files.ContainsKey(Path.GetFullPath(p)), editor, (_, _) => Task.CompletedTask, new DateTime(2026, 10, 28)));
        await vm.LoadAsync();
        return (vm, editor, files);
    }

    [Fact]
    public async Task Prefills_from_the_latest_trip_and_names_the_survey_after_the_date()
    {
        var (vm, _, _) = await Make();

        Assert.Equal("2026.10.28", vm.Date);
        Assert.Equal("trip_2026_10_28", vm.SurveyName);
        Assert.Equal("units length meters compass grads", vm.Units);
        Assert.True(vm.Team.Single(t => t.Name == "Ana").IsChecked);
        Assert.Contains("team \"Ana\"", vm.Generated);
        Assert.False(vm.CanCreate); // no tie-in station yet
    }

    [Fact]
    public async Task Tie_in_writes_the_file_next_to_the_trips_with_input_and_equate()
    {
        var (vm, editor, files) = await Make();

        Assert.True(vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2")));

        Assert.True(vm.CanCreate, string.Join("; ", vm.Warnings.Select(w => w.Text)));
        Assert.Equal(P("trips/trip_2026_10_28.th"), vm.FilePath);
        Assert.Null(await vm.CreateAsync());
        var edited = TextSplicer.Splice(files[P("main.th")], editor.Last!.Edits.Single().Edits)!;
        Assert.Contains("  input trips/trip_2026_10_28.th\n", edited);
        Assert.Contains("  equate 0@trip_2026_10_28 2@a\n", edited);
        Assert.StartsWith("encoding utf-8\nsurvey trip_2026_10_28", editor.Last.Creates.Single().Content);
    }

    [Fact]
    public async Task Nesting_moves_the_equate_into_the_tie_in_survey()
    {
        var (vm, _, _) = await Make();
        vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2"));

        vm.Nest = true;

        Assert.Equal(P("trips/a.th"), vm.SelectedDestination!.Candidate.FilePath);
        Assert.Contains("equate 0@trip_2026_10_28 2", vm.Plan!.EquateLine);
    }

    [Fact]
    public async Task A_bad_survey_name_blocks()
    {
        var (vm, _, _) = await Make();
        vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2"));

        vm.SurveyName = "two words";

        Assert.False(vm.CanCreate);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("/tmp/x")]
    public async Task A_survey_name_with_a_path_in_it_blocks(string name)
    {
        var (vm, _, _) = await Make();
        vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2"));

        vm.SurveyName = name;

        Assert.False(vm.CanCreate);
    }

    [Fact]
    public async Task A_malformed_date_blocks()
    {
        var (vm, _, _) = await Make();
        vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2"));

        vm.Date = "2026.ab.cd";

        Assert.False(vm.CanCreate);
    }
    [Fact]
    public async Task No_tie_in_writes_only_the_input_into_the_chosen_survey()
    {
        // What File ▸ New from Template ▸ Single cave leaves: a survey with no stations yet.
        var (vm, editor, files) = await Make(new() { [P("cave.th")] = "encoding utf-8\nsurvey cave -title \"Cave\"\nendsurvey\n" });
        Assert.False(vm.CanCreate);
        Assert.Empty(vm.Stations.Items);

        vm.NoTieIn = true;
        Assert.False(vm.CanCreate); // the survey the trip goes into is still to choose
        Assert.True(vm.Surveys.SelectByPayload(p => p is SurveyEntry { Name: "cave" }));

        Assert.True(vm.CanCreate, string.Join("; ", vm.Warnings.Select(w => w.Text)));
        Assert.Null(vm.Plan!.EquateLine);
        Assert.Null(await vm.CreateAsync());
        var edited = TextSplicer.Splice(files[P("cave.th")], editor.Last!.Edits.Single().Edits)!;
        Assert.Equal("encoding utf-8\nsurvey cave -title \"Cave\"\n  input trip_2026_10_28.th\nendsurvey\n", edited);
    }

    [Fact]
    public async Task Turning_no_tie_in_off_asks_for_the_station_again()
    {
        var (vm, _, _) = await Make();
        vm.NoTieIn = true;
        vm.Surveys.SelectByPayload(p => p is SurveyEntry { Name: "cave" });
        Assert.True(vm.CanCreate);

        vm.NoTieIn = false;

        Assert.False(vm.CanCreate);
        Assert.Contains(vm.Warnings, w => w.Text == ThIDE.Resources.Tr.Get("NewTrip_PickTieIn"));
    }
}

/// <summary>The tie-in comment goes into the user's survey data, so the UI language must not change it.</summary>
[Collection(UiLanguageCollection.Name)]
public class NewTripTieInCommentTests
{
    [Fact]
    public async Task The_tie_in_comment_is_english_whatever_the_ui_language()
    {
        var (vm, _, _) = await NewTripViewModelTests.Make();

        UiLanguageCollection.In(new System.Globalization.CultureInfo("ro"),
            () => vm.Stations.SelectByPayload(p => p.ToString()!.Contains("a.2")));

        Assert.Contains("# check the tie-in", vm.Plan!.Text);
    }
}
