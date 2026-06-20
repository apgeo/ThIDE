// Designer-only / fallback IDocumentService used by view-model parameterless
// constructors so AXAML Design.DataContext can hydrate without a real workspace.

using System;
using System.Threading;
using System.Threading.Tasks;
using Therion.Semantics;

namespace TherionProc.Services;

internal sealed class NullDocumentService : IDocumentService
{
    public string? CurrentPath => null;
    public string CurrentText => string.Empty;
    public SemanticModel? CurrentSemantics => null;
    public Therion.Syntax.TherionFile? CurrentAst => null;
    public WorkspaceSemanticModel? Workspace => null;
    public System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> CurrentDiagnostics =>
        System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic>.Empty;
    public Therion.Processing.Abstractions.ISymbolNavigationService? CurrentNavigation => null;
#pragma warning disable CS0067
    public event EventHandler? DocumentChanged;
#pragma warning restore CS0067
    public Task OpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
    public Task OpenFolderAsync(string folderPath, CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteCurrentTextAsync(string newText, CancellationToken ct = default) => Task.CompletedTask;
    public void Close() { }
}
