// Overview figures (Statistics, Quality, Dashboard) follow the workspace model they describe: a graph
// rebuilt from unsaved buffers — or restored from disk when a dirty tab is discarded — refreshes them,
// leg metrics leave splays out, and the Dashboard counts what the Diagnostics panel and the thconfig
// switcher show.

using System.Collections.ObjectModel;
using AvaloniaEdit.Document;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using ThIDE.Editor;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class OverviewFiguresTests
{
    private const string OneLeg = """
        survey cave
          centreline
            data normal from to length compass clino
            0 1 10 0 0
          endcentreline
        endsurvey
        """;

    private const string LongLeg = """
        survey cave
          centreline
            data normal from to length compass clino
            0 1 100 0 0
          endcentreline
        endsurvey
        """;

    private static WorkspaceSemanticModel Model(string text)
    {
        const string path = "/w/cave.th";
        return WorkspaceSemanticModel.Build(
            new Dictionary<string, ParseResult<TherionFile>> { [path] = new ThParser().Parse(path, text) },
            Array.Empty<XviFile>());
    }

    // A session whose event subscriptions the test captures, so it can raise them itself.
    private sealed class Session
    {
        public readonly List<EventHandler> Revalidated = new();
        public IReadOnlyList<ThconfigCandidate> Candidates = Array.Empty<ThconfigCandidate>();
        public string? Root;
        public IWorkspaceSession Proxy { get; }

        public Session()
        {
            Proxy = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(
                m => m.Name switch
                {
                    "get_Candidates" => Candidates,
                    "get_RootPath" => Root,
                    _ => null,
                },
                (m, args) => { if (m.Name == "add_BuffersRevalidated") Revalidated.Add((EventHandler)args[0]!); });
        }

        public void RaiseRevalidated() { foreach (var h in Revalidated) h(this, EventArgs.Empty); }
    }

    private static IDocumentService Documents(Func<WorkspaceSemanticModel?> workspace) =>
        WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Workspace" => workspace(),
            "get_Documents" => new ObservableCollection<FileDocumentViewModel>(),
            _ => null,
        });

    [Fact]
    public void Statistics_follow_a_graph_rebuilt_from_buffers()
    {
        UiThread.Run(() =>
        {
            var model = Model(LongLeg);                       // the unsaved +90 m edit
            var session = new Session();
            var vm = new DataAnalyticsViewModel(Documents(() => model), new FakeSettings(), session.Proxy);
            Assert.Equal("100.0 m", vm.Statistics.Single(s => s.Label == "Total length").Value);

            model = Model(OneLeg);                            // "Don't save": the graph is back to disk
            session.RaiseRevalidated();

            Assert.Equal("10.0 m", vm.Statistics.Single(s => s.Label == "Total length").Value);
        });
    }

    [Fact]
    public void Dashboard_follows_a_graph_rebuilt_from_buffers()
    {
        UiThread.Run(() =>
        {
            var model = Model(LongLeg);
            var session = new Session();
            var vm = new ProjectDashboardViewModel(session.Proxy, Documents(() => model), new BuildViewModel());
            Assert.Equal("100.0 m", vm.LengthText);

            model = Model(OneLeg);
            session.RaiseRevalidated();

            Assert.Equal("10.0 m", vm.LengthText);
        });
    }

    [Fact]
    public void Quality_total_legs_leaves_splays_out()
    {
        UiThread.Run(() =>
        {
            var model = Model("""
                survey cave
                  centreline
                    data normal from to length compass clino
                    0 1 10 0 0
                    1 2 5 90 0
                    1 - 2 0 0
                    1 - 2 90 0
                    2 . 3 180 85
                  endcentreline
                endsurvey
                """);
            var vm = new DataAnalyticsViewModel(Documents(() => model), new FakeSettings(), new Session().Proxy);

            int Row(string metric) => vm.Quality.Single(q => q.Metric == metric).Count;
            Assert.Equal(2, Row("Total legs"));
            Assert.Equal(2, Row("Legs without LRUD"));
            Assert.Equal(0, Row("Steep legs (80–90°)"));
            Assert.Equal(3, Row("Splay shots"));
        });
    }

    [Fact]
    public void Dashboard_counts_the_diagnostics_the_panel_lists()
    {
        UiThread.Run(() =>
        {
            var model = Model(OneLeg);   // the model itself carries no diagnostics
            var panel = new DiagnosticsViewModel();
            var vm = new ProjectDashboardViewModel(new Session().Proxy, Documents(() => model), new BuildViewModel(), panel);
            Assert.Equal("No diagnostics", vm.DiagnosticsText);

            var at = new SourceLocation(1, 1);
            var span = new SourceSpan("/w/cave.th", at, at, 0, 0);
            panel.Load(new[]
            {
                Diagnostic.Create("TH_SEM_015", DiagnosticSeverity.Warning, "w1", span),
                Diagnostic.Create("TH_SEM_012", DiagnosticSeverity.Warning, "w2", span),
                Diagnostic.Create("TH_SEM_001", DiagnosticSeverity.Error, "e1", span),
            });

            Assert.Equal("1 errors · 2 warnings", vm.DiagnosticsText);
        });
    }

    [Fact]
    public void Dashboard_counts_thconfigs_like_the_switcher()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "a.thconfig"), "source cave.th\n");
        File.WriteAllText(Path.Combine(dir.Path, "cave.th"), OneLeg);
        UiThread.Run(() =>
        {
            var session = new Session
            {
                Root = dir.Path,
                Candidates = new[]
                {
                    new ThconfigCandidate(Path.Combine(dir.Path, "a.thconfig"), "a.thconfig", false),
                    new ThconfigCandidate(Path.Combine(dir.Path, "thconfig"), "thconfig", false),
                    new ThconfigCandidate("/elsewhere/b.thconfig", "b.thconfig", true),
                },
            };
            var vm = new ProjectDashboardViewModel(session.Proxy, Documents(() => null), new BuildViewModel());

            Assert.Equal("1 .th · 0 .th2 · 3 .thconfig", vm.FileSummary);
        });
    }

    [Fact]
    public void A_multi_line_selection_does_not_become_the_search_query()
    {
        var vm = new SearchViewModel { Query = "station" };
        vm.SeedAndSearch("1 2 5 90 0\n2 3 4 0 0");
        Assert.Equal("station", vm.Query);
    }

    [Fact]
    public void Breadcrumb_path_is_never_empty_so_the_bar_keeps_its_row()
    {
        // The bar used to appear on the first caret move into a block, shifting the text under the click.
        var doc = new TextDocument("# header\nsurvey cave\n  centreline\n    0 1 10 0 0\n  endcentreline\nendsurvey\n");

        var top = TherionBlocks.BreadcrumbPath(doc, 1, "cave.th");
        Assert.Equal(new[] { ("cave.th", 1) }, top);

        var inner = TherionBlocks.BreadcrumbPath(doc, 4, "cave.th");
        Assert.Equal(new[] { "cave.th", "survey cave", "centreline" }, inner.Select(c => c.Label));
    }
}
