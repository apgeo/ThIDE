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
#pragma warning disable CS0067
    public event EventHandler? DocumentChanged;
    public event EventHandler? ActiveDocumentChanged;
    public event EventHandler<FileDocumentViewModel>? OpenInDockRequested;
    public event EventHandler? HistoryChanged;
#pragma warning restore CS0067

    public Task OpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
    public Task OpenFolderAsync(string folderPath, CancellationToken ct = default) => Task.CompletedTask;
    public Task NavigateToSpanAsync(Therion.Core.SourceSpan span, CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteCurrentTextAsync(string newText, CancellationToken ct = default) => Task.CompletedTask;
    public void SetActive(FileDocumentViewModel? document) { }
    public FileDocumentViewModel OpenTextDocument(string displayPath, string text) =>
        throw new NotSupportedException("Designer document service cannot open documents.");
    public void CloseDocument(FileDocumentViewModel document) { }
    public void Close() { }
}
