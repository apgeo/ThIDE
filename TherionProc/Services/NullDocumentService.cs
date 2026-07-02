// Designer-only / fallback IDocumentService used by view-model parameterless
// constructors so AXAML Design.DataContext can hydrate without a real workspace.

using System;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Therion.Semantics;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Services;

internal sealed class NullDocumentService : IDocumentService
{
    public ObservableCollection<FileDocumentViewModel> Documents { get; } = new();
    public FileDocumentViewModel? Active => null;

    public string? CurrentPath => null;
    public string CurrentText => string.Empty;
    public SemanticModel? CurrentSemantics => null;
    public Therion.Syntax.TherionFile? CurrentAst => null;
    public WorkspaceSemanticModel? Workspace => null;
    public ImmutableArray<Therion.Core.Diagnostic> CurrentDiagnostics =>
        ImmutableArray<Therion.Core.Diagnostic>.Empty;
    public Therion.Processing.Abstractions.ISymbolNavigationService? CurrentNavigation => null;
    public bool CanGoBack => false;
    public bool CanGoForward => false;
    public Task GoBackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task GoForwardAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void ReportCaret(Therion.Core.SourceSpan span, bool isTermNavigation, bool fromPointer = false) { }
    public void RequestRevealInWorkspace(Therion.Core.SourceSpan target) { }
    public void RequestSelectFileInWorkspace(string filePath) { }
    public void RequestFindReferences(string term) { }
    public void RequestRenameSymbol(string raw, Therion.Processing.Abstractions.ReferenceKind kind) { }
    public void RequestShowInModel3D(string fullName) { }
#pragma warning disable CS0067
    public event EventHandler? DocumentChanged;
    public event EventHandler? ActiveDocumentChanged;
    public event EventHandler<FileDocumentViewModel>? OpenInDockRequested;
    public event EventHandler? HistoryChanged;
    public event EventHandler<Therion.Core.SourceSpan>? RevealInWorkspaceRequested;
    public event EventHandler<string>? SelectFileInWorkspaceRequested;
    public event EventHandler<string>? FindReferencesRequested;
    public event EventHandler<Therion.Core.SourceSpan>? CaretMoved;
    public event EventHandler<(string Raw, Therion.Processing.Abstractions.ReferenceKind Kind)>? RenameSymbolRequested;
    public event EventHandler<string>? ShowInModel3DRequested;
#pragma warning restore CS0067

    public Task OpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
    public Task ForceOpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsBatchOpenActive => false;
    public IDisposable BeginBatchOpen() => NullScope.Instance;
    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
    public Task OpenFolderAsync(string folderPath, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> ActivateThconfigAsync(string thconfigPath, ThconfigActivation options = default, CancellationToken ct = default) => Task.FromResult(false);
    public Task NavigateToSpanAsync(Therion.Core.SourceSpan span, CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteCurrentTextAsync(string newText, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveDocumentAsync(FileDocumentViewModel document, CancellationToken ct = default) => Task.CompletedTask;
    public void SetActive(FileDocumentViewModel? document) { }
    public FileDocumentViewModel OpenTextDocument(string displayPath, string text) =>
        throw new NotSupportedException("Designer document service cannot open documents.");
    public void CloseDocument(FileDocumentViewModel document) { }
    public bool HasRecentlyClosed => false;
    public Task<bool> ReopenLastClosedAsync(CancellationToken ct = default) => Task.FromResult(false);
    public void Close() { }
}
