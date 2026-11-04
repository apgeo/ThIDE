// Structural Geology panel view-model: the per-row choices a user makes in the grids and the
// declination inputs derived from the project. Runs headless over an in-memory IDocumentService.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Structural;
using Therion.Syntax;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class StructuralGeologyViewModelTests
{
    // A dated survey with a geographic fix and one structural station ("geo1") carrying three legs,
    // which is the minimum for a plane fit.
    private const string SurveyText = """
        survey s
          centreline
            date 1998.07.12
            cs lat-long
            fix 1 46.5 8.0 500
            data normal from to length compass clino
            1 geo1 5 10 -5
            geo1 a 2 0 0
            geo1 b 2 90 0
            geo1 c 2 180 -30
          endcentreline
        endsurvey
        """;

    private const double FixLat = 46.5, FixLon = 8.0, SurveyDecimalYear = 1998.5;

    private static (FakeDocumentService Docs, StructuralGeologyViewModel Vm) Build()
    {
        var path = "/invented/s.th";
        var model = new SemanticBinder().Bind(new ThParser().Parse(path, SurveyText).Value!);
        var perFile = new Dictionary<string, SemanticModel> { [path] = model }
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        var workspace = new WorkspaceSemanticModel(perFile, XviIndex.Empty,
            ImmutableArray<FileGraphEdge>.Empty, ImmutableArray<Diagnostic>.Empty);
        var docs = new FakeDocumentService { CurrentSemantics = model, Workspace = workspace, CurrentPath = path };
        return (docs, new StructuralGeologyViewModel(docs));
    }

    [Fact]
    public void Unticked_measurement_stays_unticked_after_a_document_change()
    {
        var (docs, vm) = Build();
        vm.OnPanelActivated();
        var row = Assert.Single(vm.Planes).Rows.First(r => !r.IsOrigin);
        var key = (row.Measurement.From, row.Measurement.To);

        row.Include = false;
        docs.RaiseDocumentChanged();   // a tab switch or a re-parse of the active file

        var same = vm.Measurements.Single(r => (r.Measurement.From, r.Measurement.To) == key);
        Assert.False(same.Include, "the user's exclusion must survive a document change");
    }

    [Fact]
    public void Hidden_plane_and_its_type_survive_a_document_change()
    {
        var (docs, vm) = Build();
        vm.OnPanelActivated();
        var plane = Assert.Single(vm.Planes);

        plane.Visible = false;
        plane.Type = "Fault";
        docs.RaiseDocumentChanged();

        var same = Assert.Single(vm.Planes);
        Assert.False(same.Visible, "the plane the user hid must stay hidden");
        Assert.Equal("Fault", same.Type);
    }

    [Fact]
    public void Project_scope_wmm_declination_is_evaluated_at_the_survey_date()
    {
        using var cof = new TestGeoModel();
        var geo = GeoMagneticModelLoader.TryLoadDefault();
        Assert.NotNull(geo);
        var expected = geo!.Declination(FixLat, FixLon, 0, SurveyDecimalYear);
        var today = DateTime.UtcNow;
        var atToday = geo.Declination(FixLat, FixLon, 0, today.Year + (today.DayOfYear - 1) / 365.0);
        Assert.NotEqual(expected, atToday);   // the model must have secular variation for this to mean anything

        var (_, vm) = Build();
        vm.DeclinationSource = DeclinationSource.WmmAuto;
        vm.OnPanelActivated();

        var plane = Assert.Single(vm.Planes).Plane;
        Assert.True(plane.IsValid, plane.ErrorReason ?? "");
        Assert.True(Math.Abs(expected - plane.DeclinationApplied) < 1e-6,
            $"declination applied {plane.DeclinationApplied:0.000000}; expected {expected:0.000000} for {SurveyDecimalYear}, " +
            $"the value for today's date would be {atToday:0.000000}");
    }

    // Two files of one project, each declaring its own declination and dated twenty years apart, each
    // with one structural station. Only the first has the fix point.
    private const string OldText = """
        survey old
          centreline
            date 1990.01.10
            declination 2 deg
            cs lat-long
            fix 1 46.5 8.0 500
            data normal from to length compass clino
            1 geo1 5 10 -5
            geo1 a 2 0 0
            geo1 b 2 90 0
            geo1 c 2 180 -30
          endcentreline
        endsurvey
        """;

    private const string NewText = """
        survey young
          centreline
            date 2020.01.20
            declination 5 deg
            data normal from to length compass clino
            1 geo2 5 10 -5
            geo2 a 2 0 0
            geo2 b 2 90 0
            geo2 c 2 180 -30
          endcentreline
        endsurvey
        """;

    private static StructuralGeologyViewModel BuildProject()
    {
        var files = new[] { ("/invented/old.th", OldText), ("/invented/young.th", NewText) };
        var perFile = files
            .ToDictionary(f => f.Item1, f => new SemanticBinder().Bind(new ThParser().Parse(f.Item1, f.Item2).Value!))
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        var workspace = new WorkspaceSemanticModel(perFile, XviIndex.Empty,
            ImmutableArray<FileGraphEdge>.Empty, ImmutableArray<Diagnostic>.Empty);
        var docs = new FakeDocumentService
        {
            CurrentSemantics = perFile["/invented/young.th"], Workspace = workspace, CurrentPath = "/invented/young.th",
        };
        return new StructuralGeologyViewModel(docs);
    }

    private static StructuralPlaneRow PlaneOf(StructuralGeologyViewModel vm, string file) =>
        Assert.Single(vm.Planes, p => p.Batch.SourceFile == file);

    [Fact]
    public void Project_scope_survey_declared_declination_is_each_files_own()
    {
        var vm = BuildProject();
        vm.DeclinationSource = DeclinationSource.SurveyDeclared;
        vm.OnPanelActivated();

        Assert.Equal(2, vm.Planes.Count);
        Assert.Equal(2.0, PlaneOf(vm, "/invented/old.th").Plane.DeclinationApplied, 9);
        Assert.Equal(5.0, PlaneOf(vm, "/invented/young.th").Plane.DeclinationApplied, 9);
    }

    [Fact]
    public void Project_scope_wmm_declination_is_evaluated_at_each_surveys_own_date()
    {
        using var cof = new TestGeoModel();
        var geo = GeoMagneticModelLoader.TryLoadDefault()!;
        double old = geo.Declination(FixLat, FixLon, 0, 1990.0), young = geo.Declination(FixLat, FixLon, 0, 2020.0);
        Assert.NotEqual(old, young);

        var vm = BuildProject();
        vm.DeclinationSource = DeclinationSource.WmmAuto;
        vm.OnPanelActivated();

        Assert.Equal(old, PlaneOf(vm, "/invented/old.th").Plane.DeclinationApplied, 6);
        Assert.Equal(young, PlaneOf(vm, "/invented/young.th").Plane.DeclinationApplied, 6);
    }

    [Fact]
    public void A_plane_refit_after_unticking_a_row_keeps_its_own_declination()
    {
        var vm = BuildProject();
        vm.DeclinationSource = DeclinationSource.SurveyDeclared;
        vm.OnPanelActivated();

        var young = PlaneOf(vm, "/invented/young.th");
        young.Rows.First(r => !r.IsOrigin).Include = false;
        young.Rows.First(r => !r.IsOrigin).Include = true;   // two refits through the row checkbox

        Assert.Equal(5.0, PlaneOf(vm, "/invented/young.th").Plane.DeclinationApplied, 9);
    }

    /// <summary>Puts a synthetic WMM coefficient file where the loader looks, unless a model is already
    /// available, and removes it afterwards.</summary>
    private sealed class TestGeoModel : IDisposable
    {
        private readonly string? _written;

        public TestGeoModel()
        {
            if (GeoMagneticModelLoader.TryLoadDefault() is not null) return;
            var path = Path.Combine(AppContext.BaseDirectory, GeoMagneticModelLoader.CofFileName);
            File.WriteAllText(path, """
                2025.0 TESTWMM 11/01/2024
                1  0  -29350.0       0.0       12.0        0.0
                1  1   -1410.0    4545.0        9.7      -21.5
                9999999999999999999999999999999999999999999999
                """);
            _written = path;
        }

        public void Dispose()
        {
            if (_written is not null) try { File.Delete(_written); } catch { }
        }
    }

#pragma warning disable CS0067 // events the view-model never raises in these tests
    private sealed class FakeDocumentService : IDocumentService
    {
        public ObservableCollection<FileDocumentViewModel> Documents { get; } = new();
        public FileDocumentViewModel? Active => null;
        public event EventHandler? ActiveDocumentChanged;
        public event EventHandler<FileDocumentViewModel>? OpenInDockRequested;
        public void SetActive(FileDocumentViewModel? document) { }
        public FileDocumentViewModel OpenTextDocument(string displayPath, string text) => throw new NotSupportedException();
        public void CloseDocument(FileDocumentViewModel document) { }
        public bool HasRecentlyClosed => false;
        public Task<bool> ReopenLastClosedAsync(CancellationToken ct = default) => Task.FromResult(false);

        public string? CurrentPath { get; init; }
        public string CurrentText => "";
        public SemanticModel? CurrentSemantics { get; init; }
        public TherionFile? CurrentAst => null;
        public WorkspaceSemanticModel? Workspace { get; init; }
        public ImmutableArray<Diagnostic> CurrentDiagnostics => ImmutableArray<Diagnostic>.Empty;
        public ISymbolNavigationService? CurrentNavigation => null;
        public event EventHandler? DocumentChanged;
        public void RaiseDocumentChanged() => DocumentChanged?.Invoke(this, EventArgs.Empty);

        public Task OpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task ForceOpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<FileDocumentViewModel> OpenRecoveredAsync(string path, string text, CancellationToken ct = default) => throw new NotSupportedException();
        public IDisposable BeginBatchOpen() => throw new NotSupportedException();
        public bool IsBatchOpenActive => false;
        public Task OpenFolderAsync(string folderPath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ActivateThconfigAsync(string thconfigPath, ThconfigActivation options = default, CancellationToken ct = default) => Task.FromResult(false);
        public Task NavigateToSpanAsync(SourceSpan span, CancellationToken ct = default) => Task.CompletedTask;
        public Task WriteCurrentTextAsync(string newText, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> SaveDocumentAsync(FileDocumentViewModel document, CancellationToken ct = default) => Task.FromResult(true);
        public Task<FileDocumentViewModel?> SaveAsAsync(FileDocumentViewModel document, string newPath, CancellationToken ct = default) => Task.FromResult<FileDocumentViewModel?>(null);
        public event EventHandler<string>? DocumentSaved;
        public void Close() { }

        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public Task GoBackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken ct = default) => Task.CompletedTask;
        public event EventHandler? HistoryChanged;
        public void ReportCaret(SourceSpan span, bool isTermNavigation, bool fromPointer = false) { }
        public event EventHandler<SourceSpan>? CaretMoved;

        public void RequestRevealInWorkspace(SourceSpan target) { }
        public event EventHandler<SourceSpan>? RevealInWorkspaceRequested;
        public void RequestSelectFileInWorkspace(string filePath) { }
        public event EventHandler<string>? SelectFileInWorkspaceRequested;
        public void RequestFindReferences(string term) { }
        public event EventHandler<string>? FindReferencesRequested;
        public void RequestRenameSymbol(string raw, Therion.Processing.Abstractions.ReferenceKind kind) { }
        public event EventHandler<(string Raw, Therion.Processing.Abstractions.ReferenceKind Kind)>? RenameSymbolRequested;
        public void RequestFindAllReferences(string raw, Therion.Processing.Abstractions.ReferenceKind kind, string? filePath = null, int offset = -1) { }
        public event EventHandler<(string Raw, Therion.Processing.Abstractions.ReferenceKind Kind, string? FilePath, int Offset)>? FindAllReferencesRequested;
        public void RequestShowInModel3D(string fullName) { }
        public void RequestShowInModel3D(Model3DTarget target) { }
        public event EventHandler<Model3DTarget>? ShowInModel3DRequested;
    }
#pragma warning restore CS0067
}
