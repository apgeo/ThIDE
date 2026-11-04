// Structural Geology panel: the user's per-row choices must stay with their own plane when the
// survey returns to a structural station after measuring from another one (two planes from the
// same from-station). Runs headless over an in-memory IDocumentService; no settings service.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class StructuralGeologyReturningStationTests
{
    // geo1 splays, a leg to geo2, geo2 legs, then geo1 splays again: three planes, two from geo1.
    private const string SurveyText = """
        survey s
          centreline
            data normal from to length compass clino
            1 geo1 5 10 -5
            geo1 - 2 0 0
            geo1 - 2 90 0
            geo1 - 2 180 -30
            geo1 geo2 5 45 0
            geo2 d 2 0 0
            geo2 e 2 90 0
            geo2 f 2 180 -30
            geo1 - 2 30 10
            geo1 - 2 120 0
            geo1 - 2 200 -20
          endcentreline
        endsurvey
        """;

    private static (StubDocuments Docs, StructuralGeologyViewModel Vm) Build()
    {
        var path = "/invented/s.th";
        var model = new SemanticBinder().Bind(new ThParser().Parse(path, SurveyText).Value!);
        var perFile = new Dictionary<string, SemanticModel> { [path] = model }
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        var workspace = new WorkspaceSemanticModel(perFile, XviIndex.Empty,
            ImmutableArray<FileGraphEdge>.Empty, ImmutableArray<Diagnostic>.Empty);
        var docs = new StubDocuments { CurrentSemantics = model, Workspace = workspace, CurrentPath = path };
        var vm = new StructuralGeologyViewModel(docs);
        vm.OnPanelActivated();
        return (docs, vm);
    }

    [Fact]
    public void Hiding_the_first_plane_of_a_revisited_station_survives_a_document_change()
    {
        var (docs, vm) = Build();
        Assert.Equal(3, vm.Planes.Count);
        var first = vm.Planes[0];

        first.Visible = false;
        first.Type = "Fault";
        docs.RaiseDocumentChanged();   // a tab switch or a re-parse of the active file

        Assert.False(vm.Planes[0].Visible, "the plane the user hid must stay hidden");
        Assert.Equal("Fault", vm.Planes[0].Type);
        Assert.True(vm.Planes[2].Visible, "the other plane from the same station was not touched");
    }

    [Fact]
    public void Unticked_splay_in_the_first_plane_of_a_revisited_station_stays_unticked()
    {
        var (docs, vm) = Build();
        Assert.Equal(3, vm.Planes.Count);
        vm.Planes[0].Rows.First(r => !r.IsOrigin).Include = false;

        docs.RaiseDocumentChanged();

        Assert.False(vm.Planes[0].Rows.First(r => !r.IsOrigin).Include,
            "the user's exclusion in the first plane must survive a document change");
        Assert.True(vm.Planes[2].Rows.First(r => !r.IsOrigin).Include,
            "the other plane from the same station was not touched");
    }

#pragma warning disable CS0067 // events the view-model never raises in these tests
    private sealed class StubDocuments : IDocumentService
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
        public void RequestRenameSymbol(string raw, ReferenceKind kind) { }
        public event EventHandler<(string Raw, ReferenceKind Kind)>? RenameSymbolRequested;
        public void RequestFindAllReferences(string raw, ReferenceKind kind) { }
        public event EventHandler<(string Raw, ReferenceKind Kind)>? FindAllReferencesRequested;
        public void RequestShowInModel3D(string fullName) { }
        public void RequestShowInModel3D(Model3DTarget target) { }
        public event EventHandler<Model3DTarget>? ShowInModel3DRequested;
    }
#pragma warning restore CS0067
}
