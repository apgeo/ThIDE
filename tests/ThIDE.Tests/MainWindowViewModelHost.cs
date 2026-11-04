// Builds a MainWindowViewModel the way the designer constructor does, but with a document service
// and task runner the test can drive (raise events, publish diagnostics).

#pragma warning disable CS0067   // events the fakes never raise

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Localization;
using Therion.Build.Automation;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using ThIDE.Docking;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

internal static class MainWindowViewModelHost
{
    public static MainWindowViewModel Create(IDocumentService documents, ITaskRunnerService? tasks = null,
        ICrashRecoveryService? crashRecovery = null, IWorkspaceUndoService? workspaceUndo = null) => new(
        new KeyLocalizer(),
        new LanguageService(),
        documents,
        NewFactory(),
        new WelcomeToolViewModel(new WelcomeViewModel()),
        new WorkspaceExplorerToolViewModel(new WorkspaceExplorerViewModel()),
        new ObjectBrowserToolViewModel(new ObjectBrowserViewModel()),
        new DiagnosticsToolViewModel(new DiagnosticsViewModel()),
        new CompilerOutputToolViewModel(new BuildViewModel()),
        new GeneratedFilesToolViewModel(new BuildViewModel()),
        new XviToolViewModel(new XviReferencesViewModel()),
        new OutlineToolViewModel(new OutlineViewModel()),
        new ProjectToolViewModel(new ProjectDashboardViewModel(), new SurveyTreeViewModel(), new ProjectAuditViewModel(), new DataAnalyticsViewModel(), new LeadsViewModel(), new TodoScanViewModel(), new ProjectMetadataViewModel(), new MediaManagerViewModel()),
        new LogToolViewModel(new LogViewModel()),
        new LivePreviewToolViewModel(new LivePreviewViewModel()),
        new MapViewerToolViewModel(new MapViewerViewModel()),
        new Model3DViewerToolViewModel(new Model3DViewerViewModel()),
        new StructuralGeologyToolViewModel(new StructuralGeologyViewModel()),
        new StructuralPlotToolViewModel(new StructuralGeologyViewModel()),
        new StructuralStereonetToolViewModel(new StructuralGeologyViewModel()),
        new AssistantToolViewModel(),
        new BlenderAnimationToolViewModel(new BlenderAnimationViewModel()),
        TestProfile.SettingsTool(),
        crashRecovery: crashRecovery,
        tasks: tasks,
        workspaceUndo: workspaceUndo);

    private static DockFactory NewFactory() => new(
        new WelcomeToolViewModel(new WelcomeViewModel()),
        new WorkspaceExplorerToolViewModel(new WorkspaceExplorerViewModel()),
        new ObjectBrowserToolViewModel(new ObjectBrowserViewModel()),
        new DiagnosticsToolViewModel(new DiagnosticsViewModel()),
        new CompilerOutputToolViewModel(new BuildViewModel()),
        new GeneratedFilesToolViewModel(new BuildViewModel()),
        new XviToolViewModel(new XviReferencesViewModel()),
        new OutlineToolViewModel(new OutlineViewModel()),
        new ProjectToolViewModel(new ProjectDashboardViewModel(), new SurveyTreeViewModel(), new ProjectAuditViewModel(), new DataAnalyticsViewModel(), new LeadsViewModel(), new TodoScanViewModel(), new ProjectMetadataViewModel(), new MediaManagerViewModel()),
        new LogToolViewModel(new LogViewModel()),
        new LivePreviewToolViewModel(new LivePreviewViewModel()),
        new MapViewerToolViewModel(new MapViewerViewModel()),
        new Model3DViewerToolViewModel(new Model3DViewerViewModel()),
        new StructuralGeologyToolViewModel(new StructuralGeologyViewModel()),
        new StructuralPlotToolViewModel(new StructuralGeologyViewModel()),
        new StructuralStereonetToolViewModel(new StructuralGeologyViewModel()),
        new AssistantToolViewModel(),
        new BlenderAnimationToolViewModel(new BlenderAnimationViewModel()),
        TestProfile.SettingsTool(),
        layoutPath: TestProfile.NewPath("dock-layout.json"));

    private sealed class KeyLocalizer : IStringLocalizer<Strings>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);
        public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: true);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }
}

/// <summary>
/// Runs a test body on a thread that Avalonia's dispatcher regards as its loop thread, so the view
/// model's "marshal to the UI thread" helpers execute inline instead of queueing work that never
/// runs. Without a platform, <c>Dispatcher.UIThread</c> binds to whichever test thread touched it
/// first, so the binding is moved to the test's own thread (serialised across tests).
/// </summary>
internal static class UiThread
{
    private static readonly object Gate = new();

    public static void Run(Action body)
    {
        lock (Gate)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { BindLoopThread(); body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.Start();
            thread.Join();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void BindLoopThread()
    {
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;
        if (dispatcher.CheckAccess()) return;
        var impl = typeof(Avalonia.Threading.Dispatcher)
            .GetField("_impl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(dispatcher)!;
        var loopThread = impl.GetType()
            .GetField("_loopThread", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{impl.GetType().Name} has no loop-thread field to bind.");
        loopThread.SetValue(impl, Thread.CurrentThread);
        // The dispatcher caches the thread it was created on and checks access against that copy.
        typeof(Avalonia.Threading.Dispatcher)
            .GetField("_thread", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(dispatcher, Thread.CurrentThread);
        if (!dispatcher.CheckAccess()) throw new InvalidOperationException("The dispatcher could not be bound to the test thread.");
    }
}

/// <summary>A task runner that only exposes the shell-set callbacks.</summary>
internal sealed class StubTaskRunner : ITaskRunnerService
{
    public IReadOnlyList<TaskDefinition> Tasks() => Array.Empty<TaskDefinition>();
    public Task<bool> RunAsync(string id, CancellationToken ct = default, HookContext? context = null) => Task.FromResult(false);
    public Func<IReadOnlyList<string>?, bool, Task>? RunBuild { get; set; }
    public Func<string, Task>? RunMacro { get; set; }
    public Action<ImmutableArray<Diagnostic>>? PublishDiagnostics { get; set; }
}

/// <summary>An in-memory document service whose events the test raises itself.</summary>
internal sealed class ScriptedDocumentService : IDocumentService
{
    public ObservableCollection<FileDocumentViewModel> Documents { get; } = new();
    public FileDocumentViewModel? Active { get; private set; }
    public event EventHandler? ActiveDocumentChanged;
    public event EventHandler<FileDocumentViewModel>? OpenInDockRequested;
    public event EventHandler? DocumentChanged;

    public void SetActive(FileDocumentViewModel? document)
    {
        Active = document;
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What a tab activation, a navigation or a re-parse does: the per-document tools refresh.</summary>
    public void RaiseDocumentChanged() => DocumentChanged?.Invoke(this, EventArgs.Empty);

    public FileDocumentViewModel OpenTextDocument(string displayPath, string text)
    {
        var doc = new FileDocumentViewModel(displayPath, text, new MeasurementsViewModel());
        Documents.Add(doc);
        SetActive(doc);
        return doc;
    }

    public void CloseDocument(FileDocumentViewModel document)
    {
        if (!Documents.Remove(document)) return;
        document.Dispose();
        if (ReferenceEquals(Active, document)) SetActive(Documents.LastOrDefault());
    }

    public bool HasRecentlyClosed => false;
    public Task<bool> ReopenLastClosedAsync(CancellationToken ct = default) => Task.FromResult(false);

    public string? CurrentPath => Active?.FilePath;
    public string CurrentText => Active?.DocumentText ?? string.Empty;
    public SemanticModel? CurrentSemantics => Active?.Semantics;
    public TherionFile? CurrentAst => Active?.Ast;
    public WorkspaceSemanticModel? Workspace { get; set; }
    public ImmutableArray<Diagnostic> CurrentDiagnostics => Active?.Diagnostics ?? ImmutableArray<Diagnostic>.Empty;
    public ISymbolNavigationService? CurrentNavigation => null;

    public Task OpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
    public Task ForceOpenFileAsync(string absolutePath, CancellationToken ct = default) => Task.CompletedTask;
    public Task<FileDocumentViewModel> OpenRecoveredAsync(string path, string text, CancellationToken ct = default)
    {
        var doc = Documents.FirstOrDefault(d => d.FilePath == path) ?? OpenTextDocument(path, string.Empty);
        doc.DocumentText = text;
        return Task.FromResult(doc);
    }
    public IDisposable BeginBatchOpen() => new NoopScope();
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

    private sealed class NoopScope : IDisposable { public void Dispose() { } }
}
