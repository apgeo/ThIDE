// A live switch of the UI language reaches the texts built in code, not only {l:Loc} bindings: the
// Stations kind and "(none)" flag cells, the status-bar file type, the Object Browser kinds, the
// Overview figures, Go to File and the Mainline Preview status. Counts take a real
// singular or plural in both languages. All inputs are invented.

using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Collections;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class LiveLanguageSwitchTests
{
    private static readonly CultureInfo Ro = new("ro-RO");

    private const string Cave = """
        survey cave
          centreline
            data normal from to length compass clino
            1 2 10 0 0
          endcentreline
        endsurvey
        """;

    private const string TwoCaves = """
        survey a
          centreline
            data normal from to length compass clino
            1 2 10 0 -10
          endcentreline
        endsurvey
        survey b
          centreline
            data normal from to length compass clino
            1 2 5 90 0
            2 3 5 90 0
          endcentreline
        endsurvey
        """;

    private static SemanticModel Bind(string path, string text) => new SemanticBinder().Bind(new ThParser().Parse(path, text).Value!);

    private static WorkspaceSemanticModel Workspace(string text)
    {
        const string path = "/w/cave.th";
        return WorkspaceSemanticModel.Build(
            new Dictionary<string, ParseResult<TherionFile>> { [path] = new ThParser().Parse(path, text) },
            Array.Empty<XviFile>());
    }

    private static void English(Action body) => UiLanguageCollection.In(CultureInfo.InvariantCulture, () => UiThread.Run(body));

    private sealed class FakeLanguage : ILanguageService
    {
        public CultureInfo CurrentCulture => Tr.Culture;
        public event EventHandler? LanguageChanged;
        public void SetLanguage(string cultureName) { }

        /// <summary>What LanguageService does on a switch: the new culture, the bindings, then the listeners.</summary>
        public void SwitchTo(CultureInfo culture)
        {
            Tr.Culture = culture;
            LocProxy.Instance.Invalidate();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public void Station_kind_and_no_flags_cells_follow_a_language_switch() => English(() =>
    {
        var vm = new MeasurementsViewModel();
        vm.Load(Bind("/w/cave.th", Cave));
        vm.StationsGroupBy = MeasurementsViewModel.GroupByKind;
        var station = vm.StationsView!.SourceCollection.Cast<StationMeasurementRow>().First();
        var shot = vm.View!.SourceCollection.Cast<MeasurementRow>().Single();
        Assert.Equal("Shot", station.Kind);
        Assert.Equal("(none)", shot.Flags);

        var raised = new List<string?>();
        station.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Tr.Culture = Ro;
        vm.Relocalize();

        Assert.Equal("Viză", station.Kind);
        Assert.Contains(nameof(StationMeasurementRow.Kind), raised);
        Assert.Equal("(niciunul)", shot.Flags);
        Assert.Equal("Viză", ((DataGridCollectionViewGroup)vm.StationsView!.Groups![0]!).Key);
    });

    [Fact]
    public void The_status_bar_file_type_follows_a_language_switch()
    {
        using var dir = new TempDir();
        var path = dir.Write("notes.txt", "invented notes\n");
        English(() =>
        {
            var docs = new ScriptedDocumentService();
            var doc = docs.OpenTextDocument(path, "invented notes\n");
            var language = new FakeLanguage();
            var vm = MainWindowViewModelHost.Create(docs, language: language);
            docs.SetActive(doc);
            Assert.Equal("Plain text (.txt) · not parsed", vm.StatusFileType);

            language.SwitchTo(Ro);

            Assert.Equal("Text simplu (.txt)", doc.InterpretedTypeText);
            Assert.Equal("Text simplu (.txt) · neanalizat", vm.StatusFileType);
        });
    }

    [Fact]
    public void Object_Browser_station_kinds_follow_a_language_switch() => English(() =>
    {
        var language = new FakeLanguage();
        var vm = new ObjectBrowserViewModel(new TrLocalizer(), language);
        vm.Load(Bind("/w/cave.th", Cave));
        var before = vm.Stations;
        Assert.Equal("Shot", before[0].Kind);

        language.SwitchTo(Ro);

        Assert.NotSame(before, vm.Stations);   // a new list, so the grid reads the cells again
        Assert.Equal("Viză", vm.Stations[0].Kind);
    });

    private static IWorkspaceSession Session() =>
        WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name switch
        {
            "get_Candidates" => Array.Empty<ThconfigCandidate>(),
            _ => null,
        });

    private static IDocumentService Documents(WorkspaceSemanticModel? workspace) =>
        WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Workspace" => workspace,
            "get_Documents" => new ObservableCollection<FileDocumentViewModel>(),
            _ => null,
        });

    [Fact]
    public void Overview_figures_take_singular_and_plural_in_English_and_Romanian() => English(() =>
    {
        var one = new ProjectDashboardViewModel(Session(), Documents(Workspace(Cave)), new BuildViewModel(), new DiagnosticsViewModel());
        Assert.Equal("1 survey", one.SurveySummary);
        Assert.Equal("2 stations · 1 leg", one.StationsText);
        Assert.Equal("0 entrances · 0 fixed points", one.EntrancesText);
        Assert.Equal("0.0 m vertical range", one.DepthText);
        Assert.Equal("No warnings or errors", one.DiagnosticsText);
        Assert.Equal("Not compiled this session", one.BuildStatus);

        var two = new ProjectDashboardViewModel(Session(), Documents(Workspace(TwoCaves)), new BuildViewModel(), new DiagnosticsViewModel());
        Assert.Equal("2 surveys", two.SurveySummary);
        Assert.Equal("5 stations · 3 legs", two.StationsText);

        Tr.Culture = Ro;
        LocProxy.Instance.Invalidate();   // the dashboard rebuilds its figures in the new language

        Assert.Equal("1 topografie", one.SurveySummary);
        Assert.Equal("2 stații · 1 segment", one.StationsText);
        Assert.Equal("0 intrări · 0 puncte fixe", one.EntrancesText);
        Assert.Equal("denivelare: 0.0 m", one.DepthText);
        Assert.Equal("Fără avertismente sau erori", one.DiagnosticsText);
        Assert.Equal("Nicio compilare în această sesiune", one.BuildStatus);
        Assert.Equal("2 topografii", two.SurveySummary);
        Assert.Equal("5 stații · 3 segmente", two.StationsText);
    });

    [Fact]
    public void Overview_panel_summaries_have_no_s_plurals() => English(() =>
    {
        Assert.Equal("1 orphan file · 2 unreferenced scraps · 0 unexported maps",
            $"{Tr.Plural("Count_OrphanFiles", 1)} · {Tr.Plural("Count_UnreferencedScraps", 2)} · {Tr.Plural("Count_UnexportedMaps", 0)}");
        Assert.Equal("3 tags in 1 file.", Tr.Plural("Todo_Summary", 3, Tr.Plural("Count_Files", 1)));
        var tree = new SurveyTreeItem(new SurveyTreeNode("cave", "cave", default) { Stations = 2, Shots = 1, Length = 10 });
        Assert.Equal("2 stations · 1 leg · 10.0 m", tree.Counts);
        Tr.Culture = Ro;
        Assert.Equal("1 fișier orfan · 2 schițe nereferențiate · 0 hărți neexportate",
            $"{Tr.Plural("Count_OrphanFiles", 1)} · {Tr.Plural("Count_UnreferencedScraps", 2)} · {Tr.Plural("Count_UnexportedMaps", 0)}");
        Assert.Equal("20 de etichete în 1 fișier.", Tr.Plural("Todo_Summary", 20, Tr.Plural("Count_Files", 1)));
    });

    [Fact]
    public void Go_to_File_title_and_watermark_are_Romanian_in_the_Romanian_UI() => English(() =>
    {
        var icons = WorkspaceEditServiceTests.Stub<IFileIconProvider>.Create(_ => null);
        var provider = new QuickOpenProvider(Documents(null), Session(), new FakeSettings(), icons);
        var en = provider.CreatePalette(_ => Task.CompletedTask);
        Assert.Equal("Go to File", en.Title);
        Assert.StartsWith("Search files by name or path…", en.Watermark);

        Tr.Culture = Ro;
        var ro = provider.CreatePalette(_ => Task.CompletedTask);
        Assert.Equal("Salt la fișier", ro.Title);
        Assert.StartsWith("Căutați fișiere după nume sau cale…", ro.Watermark);
    });

    [Fact]
    public void Mainline_Preview_status_counts_surveys_and_files_in_the_singular_or_plural() => English(() =>
    {
        var vm = LivePreviewViewModel.CreateStandalone();
        vm.SetModels([Bind("a.th", Cave.Replace("cave", "a"))]);
        Assert.Contains(" · 1 leg · 1 survey · 1 file", vm.Status);
        Assert.DoesNotContain("(s)", vm.Status);

        vm.SetModels([Bind("a.th", Cave.Replace("cave", "a")), Bind("b.th", Cave.Replace("cave", "b"))]);
        Assert.Contains(" · 2 legs · 2 surveys · 2 files", vm.Status);

        Tr.Culture = Ro;
        vm.SetModels([Bind("a.th", Cave.Replace("cave", "a")), Bind("b.th", Cave.Replace("cave", "b"))]);
        Assert.StartsWith("Plan · 2 segmente · 2 topografii · 2 fișiere", vm.Status);
        Assert.EndsWith("(doar previzualizare — nu este o randare Therion)", vm.Status);
    });
}
