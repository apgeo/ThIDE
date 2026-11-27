// Tests of the "Derive survey from 3-D model" dialog's view model, without a window: presets and
// validation, opening an invented model, the live leg check, chaining points into legs, a full run,
// a rejected guide, previewing and saving. The models are invented caves from the shared generator.

using Therion.Cave3D;
using Therion.CaveGeometry.Writers;
using Therion.MeshSurvey;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Tests.Synthetic;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class MeshSurveyViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-meshvm-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    private sealed class FakePicker : IStoragePicker
    {
        public string? Open, Save;
        public string? SaveSuggestion;
        public Task<string?> PickOpenFileAsync(string title) => Task.FromResult(Open);
        public Task<string?> PickOpenThconfigAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string? startFolder = null)
        {
            SaveSuggestion = suggestedName;
            return Task.FromResult(Save);
        }
        public Task<string?> PickOpenLayoutAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLayoutAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
    }

    private sealed class FakeNotifications : INotificationService
    {
        public readonly List<string> Errors = [];
        public event EventHandler<AppNotification>? Posted { add { } remove { } }
        public event EventHandler? UnreadChanged { add { } remove { } }
        public System.Collections.ObjectModel.ObservableCollection<AppNotification> History { get; } = [];
        public int UnreadCount => 0;
        public void Post(AppNotification notification) { }
        public void Info(string title, string message, string? actionLabel = null, Action? action = null) { }
        public void Success(string title, string message, string? actionLabel = null, Action? action = null) { }
        public void Warning(string title, string message, string? actionLabel = null, Action? action = null) { }
        public void Error(string title, string message, string? actionLabel = null, Action? action = null) => Errors.Add(message);
        public void MarkAllRead() { }
        public void Clear() { }
    }

    private string WriteBend()
    {
        var path = Path.Combine(_dir, "bend.stl");
        StlWriter.WriteFile(SyntheticCave.Bend().Mesh(0.25), path);
        return path;
    }

    private MeshSurveyViewModel Make(FakePicker? picker = null, List<string>? opened = null, List<string>? previews = null) =>
        new(picker ?? new FakePicker(), new FakeNotifications(),
            p => { opened?.Add(p); return Task.CompletedTask; },
            p => previews?.Add(p),
            TimeSpan.Zero);

    private async Task<MeshSurveyViewModel> OpenedAsync(FakePicker? picker = null, List<string>? opened = null, List<string>? previews = null)
    {
        var vm = Make(picker, opened, previews);
        vm.ModelPath = WriteBend();
        await vm.OpenModelAsync();
        Assert.True(vm.HasModel, vm.ErrorMessage);
        return vm;
    }

    private static GuidePointRow Point(MeshSurveyViewModel vm, string name, double x, double y, double z)
    {
        vm.AddPoint();
        var row = vm.Points[^1];
        row.Name = name;
        row.SetPosition(new CaveVector3(x, y, z));
        return row;
    }

    [Fact]
    public void Choosing_a_preset_fills_the_fields()
    {
        var vm = Make();
        vm.SelectedPreset = "mainline-only";
        Assert.Equal(LateralDepthChoice.Mainline, vm.LateralDepth);
        Assert.False(vm.SideStations);
        Assert.Equal(Therion.MeshSurvey.LoopPolicy.Never, vm.LoopPolicy);

        vm.SelectedPreset = "thorough";
        Assert.Equal(98, vm.CoverageTargetPercent);
        Assert.Equal("98", vm.CoverageTargetText);
        Assert.Equal(SplayMode.Adaptive, vm.SplayMode);
        Assert.Equal("24", vm.SplayCount);
        Assert.Equal(LateralDepthChoice.Unlimited, vm.LateralDepth);

        var options = vm.BuildOptions([]);
        Assert.Equal(0.98, options.Coverage.Target, 6);
        Assert.Equal(24, options.Splays.Count);
        Assert.Null(options.Laterals.MaxDepth);
    }

    [Fact]
    public void Settings_that_contradict_each_other_show_a_message()
    {
        var vm = Make();
        Assert.False(vm.HasIssues);
        vm.LegMin = "8";   // above the preferred 5
        Assert.True(vm.HasIssues);
        Assert.Contains("legs.min must not exceed legs.preferred", vm.IssueText);
        vm.LegMin = "1";
        Assert.False(vm.HasIssues);
        vm.LegPreferred = "abc";
        Assert.True(vm.HasIssues);
        Assert.False(vm.RunCommand.CanExecute(null));
    }

    [Fact]
    public void Empty_optional_fields_mean_no_limit_and_numbers_use_the_invariant_culture()
    {
        var vm = Make();
        vm.LateralMaxCount = "";
        vm.LateralLengthBudget = "";
        vm.LegMaxVertical = "";
        vm.LegClearance = "0,25";   // a comma is read as the decimal mark
        var o = vm.BuildOptions([]);
        Assert.Null(o.Laterals.MaxCount);
        Assert.Null(o.Laterals.MaxTotalLength);
        Assert.Null(o.Legs.MaxVertical);
        Assert.Equal(0.25, o.Legs.Clearance);
        vm.LateralMaxCount = "3";
        vm.LateralLengthBudget = "40.5";
        o = vm.BuildOptions([]);
        Assert.Equal(3, o.Laterals.MaxCount);
        Assert.Equal(40.5, o.Laterals.MaxTotalLength);
    }

    [Fact]
    public async Task Opening_a_model_fills_the_inspect_summary()
    {
        var vm = await OpenedAsync();
        Assert.NotNull(vm.Inspect);
        Assert.False(string.IsNullOrWhiteSpace(vm.InspectSummary));
        Assert.True(vm.Inspect!.Size.X > 20);
        Assert.True(vm.Inspect.EstimatedVoxels > 0);
        Assert.Contains("Triangles", vm.InspectSummary);
        Assert.False(vm.InputChanged);
        vm.NorthRotation = "10";
        Assert.True(vm.InputChanged);   // the model must be read again to apply it
    }

    [Fact]
    public async Task Browsing_for_a_model_opens_it()
    {
        var vm = Make(new FakePicker { Open = WriteBend() });
        await vm.BrowseAsync();
        Assert.True(vm.HasModel, vm.ErrorMessage);
        Assert.EndsWith("bend.stl", vm.ModelPath);
    }

    [Fact]
    public async Task A_missing_file_reports_an_error()
    {
        var vm = Make();
        vm.ModelPath = Path.Combine(_dir, "none.stl");
        await vm.OpenModelAsync();
        Assert.False(vm.HasModel);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_leg_through_the_corner_is_blocked_and_a_clear_leg_is_clear()
    {
        var vm = await OpenedAsync();
        Point(vm, "a", 6, 0, 0);
        Point(vm, "b", 20, 7, -0.8);
        Point(vm, "c", 2, 0, 0);
        Point(vm, "d", 12, 0, 0);
        vm.AddLeg(); var cut = vm.Legs[^1]; cut.From = "a"; cut.To = "b";
        vm.AddLeg(); var clear = vm.Legs[^1]; clear.From = "c"; clear.To = "d";

        await vm.WaitForLegChecksAsync();

        Assert.Equal(LegCheckStatus.Blocked, cut.Check?.Status);
        Assert.Equal(LegStatusKind.Bad, cut.StatusKind);
        Assert.Equal(LegCheckStatus.Clear, clear.Check?.Status);
        Assert.Equal(LegStatusKind.Ok, clear.StatusKind);

        // Moving a point checks the leg again.
        vm.Points.First(p => p.Name == "b").SetPosition(new CaveVector3(12, 0, 0));
        await vm.WaitForLegChecksAsync();
        Assert.Equal(LegCheckStatus.Clear, cut.Check?.Status);
    }

    [Fact]
    public async Task A_renamed_point_keeps_its_legs()
    {
        var vm = await OpenedAsync();
        var a = Point(vm, "a", 2, 0, 0);
        Point(vm, "b", 12, 0, 0);
        vm.ChainPoints();
        a.Name = "entrance";
        await vm.WaitForLegChecksAsync();
        Assert.Equal("entrance", vm.Legs[0].From);
        Assert.Equal(LegCheckStatus.Clear, vm.Legs[0].Check?.Status);
    }

    [Fact]
    public void Chaining_the_points_creates_one_leg_between_each_consecutive_pair()
    {
        var vm = Make();
        for (int i = 0; i < 5; i++) Point(vm, "p" + (i + 1), i, 0, 0);
        vm.ChainPoints();
        Assert.Equal(4, vm.Legs.Count);
        Assert.Equal(("p1", "p2"), (vm.Legs[0].From, vm.Legs[0].To));
        Assert.Equal(("p4", "p5"), (vm.Legs[3].From, vm.Legs[3].To));
        vm.ChainPoints();   // no duplicates
        Assert.Equal(4, vm.Legs.Count);

        // A start point that is used comes first in the chain.
        vm.StartAutomatic = false;
        vm.StartPoint.Name = "s";
        vm.ChainPoints();
        Assert.Equal(5, vm.Legs.Count);
        Assert.Equal(("s", "p1"), (vm.Legs[4].From, vm.Legs[4].To));
    }

    [Fact]
    public void The_guide_round_trips_through_the_fields()
    {
        var vm = Make();
        Point(vm, "a", 1, 2, 3);
        Point(vm, "b", 4, 5, 6);
        vm.ChainPoints();
        vm.Legs[0].Force = true;
        vm.GuideMode = GuideMode.OnlyGiven;
        vm.StartAutomatic = false;
        vm.StartPoint.Name = "s";
        vm.StartPoint.SetPosition(new CaveVector3(0, 0, 0));

        var guide = vm.BuildGuide();
        var other = Make();
        other.LoadGuide(guide);
        var again = other.BuildGuide();

        Assert.Equal("s", again.Start?.Name);
        Assert.Equal(2, again.Points.Count);
        Assert.Equal(new CaveVector3(4, 5, 6), again.Points[1].Position);
        Assert.True(again.Legs[0].Force);
        Assert.Equal(GuideMode.OnlyGiven, again.Mode);
        Assert.Empty(again.Validate());
    }

    [Fact]
    public async Task A_full_run_gives_a_summary_and_can_be_previewed_and_saved()
    {
        var picker = new FakePicker { Save = Path.Combine(_dir, "out", "bend.th") };
        Directory.CreateDirectory(Path.Combine(_dir, "out"));
        var opened = new List<string>();
        var previews = new List<string>();
        var vm = await OpenedAsync(picker, opened, previews);
        vm.VoxelSize = "0.25";
        vm.WriteDebugLayers = true;
        Assert.False(vm.HasIssues, vm.IssueText);

        await vm.RunAsync();

        Assert.True(vm.HasResult, vm.ErrorMessage);
        Assert.NotNull(vm.ReportPlan);
        Assert.True(vm.ReportPlan!.Legs > 0);
        Assert.Contains("Stations:", vm.ReportSummary);
        Assert.Contains("Coverage:", vm.ReportSummary);

        await vm.PreviewAsync();
        Assert.Single(previews);
        Assert.True(File.Exists(previews[0]));
        Assert.EndsWith(".lox", previews[0]);

        await vm.SaveAsync();
        Assert.Equal("bend.th", picker.SaveSuggestion);
        var th = picker.Save!;
        Assert.Contains("centreline", File.ReadAllText(th));
        Assert.True(File.Exists(Path.ChangeExtension(th, ".mesh-survey.json")));
        Assert.True(File.Exists(Path.Combine(_dir, "out", "bend.skeleton.ply")));
        Assert.Equal([th], opened);
    }

    [Fact]
    public async Task Saving_is_cancelled_when_the_picker_returns_nothing()
    {
        var picker = new FakePicker { Save = null };
        var opened = new List<string>();
        var vm = await OpenedAsync(picker, opened);
        vm.VoxelSize = "0.25";
        await vm.RunAsync();
        Assert.True(vm.HasResult, vm.ErrorMessage);
        await vm.SaveAsync();
        Assert.Empty(opened);
        Assert.Null(vm.SavedPath);
    }

    [Fact]
    public async Task A_guide_leg_through_rock_stops_the_run_with_its_message()
    {
        var vm = await OpenedAsync();
        vm.VoxelSize = "0.25";
        Point(vm, "a", 6, 0, 0);
        Point(vm, "b", 20, 7, -0.8);
        vm.ChainPoints();
        vm.OnBlocked = GuideBlockedPolicy.Error;

        await vm.RunAsync();

        Assert.False(vm.HasResult);
        Assert.Null(vm.Result);
        Assert.Contains("Guide leg a", vm.ErrorMessage);
        Assert.Equal(LegStatusKind.Bad, vm.Legs[0].StatusKind);
    }

    [Fact]
    public void Every_choice_has_a_label_in_the_resources()
    {
        var vm = Make();
        var labels = new List<(string Value, string Label)>();
        void Add<T>(IReadOnlyList<Choice<T>> list) => labels.AddRange(list.Select(c => (c.Value!.ToString()!, c.Label)));
        Add(vm.Presets); Add(vm.UnitChoices); Add(vm.UpAxisChoices); Add(vm.LegStrategies); Add(vm.AnchorModes);
        Add(vm.DepthChoices); Add(vm.LoopPolicies); Add(vm.SplayModes); Add(vm.ChamberPolicies); Add(vm.NamingChoices);
        Add(vm.SnapChoices); Add(vm.GuideModes); Add(vm.BlockedPolicies); Add(vm.FrameChoices);
        Assert.All(labels, l => Assert.DoesNotContain("MeshSurvey_", l.Label));
        foreach (var stop in Enum.GetValues<StopReason>())
            Assert.DoesNotContain("MeshSurvey_", ThIDE.ViewModels.MeshSurveyChoices.Label("Stop", stop.ToString()));
    }
}
