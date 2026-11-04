// Every view-model the app makes per window, dialog or tab is made here over the long-lived services
// it is given in the app (settings, workspace session, document service, workspace undo and edit
// services, all real and over a temp project), closed the way the app closes it (Dispose, Detach, or
// just dropped), and must then be collectable: no singleton event, timer or static keeps it, with its
// models, alive and rebuilding after its window is gone.
//
// The guard below makes every *ViewModel type of the app take a side: a singleton registered in
// AppServices (lives as long as the app), the main window's (its own lifetime test), one of the rows
// of the theory, or a value holder that is given nothing long-lived to subscribe to. A new view-model
// fails the guard until it is placed.

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace.Templates;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public sealed class ViewModelLifetimeTheory : IAsyncLifetime
{
    private readonly TempDir _project = new();
    private FakeSettings _settings = null!;
    private WorkspaceSessionService _session = null!;
    private DocumentService _documents = null!;
    private WorkspaceUndoService _history = null!;
    private WorkspaceEditService _editor = null!;

    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    public async Task InitializeAsync()
    {
        _project.Write("a.th", "survey a\n  centreline\n    1 2 5 0 0\n    2 3 4 90 0\n  endcentreline\nendsurvey\n");
        _project.Write("b.th", "survey b\n  centreline\n    1 2 3 180 0\n  endcentreline\nendsurvey\n");
        var cfg = _project.Write("cave.thconfig", "encoding utf-8\nsource a.th\nsource b.th\nexport model -o cave.lox\n");
        _settings = new FakeSettings();
        _session = new WorkspaceSessionService(new StubSniffer(), _settings);
        _documents = new DocumentService(new StubResolver(), _session, _settings);
        _history = new WorkspaceUndoService(_documents, _session);
        _editor = new WorkspaceEditService(_documents, _session);
        await _session.SetRootAsync(_project.Path);
        Assert.True(await _session.SetActiveThconfigAsync(cfg));
    }

    public async Task DisposeAsync()
    {
        await _session.DisposeAsync();
        _project.Dispose();
    }

    private string P(string name) => Path.Combine(_project.Path, name);
    private string? ReadText(string path) { try { return File.ReadAllText(path); } catch (IOException) { return null; } }

    /// <summary>A view-model as the app opens it, and how the app closes it (null: the window just drops it).</summary>
    private sealed record Opened(object ViewModel, Action? Close, object? KeptAlive = null);

    private static readonly Dictionary<string, Func<ViewModelLifetimeTheory, Opened>> Rows = new()
    {
        ["BreadcrumbViewModel"] = t => new(new BreadcrumbViewModel(t._documents), null),
        ["RelationalMapViewModel"] = t => { var vm = new RelationalMapViewModel(t._documents, t._session); return new(vm, vm.Dispose); },
        ["PreferencesViewModel"] = t => new(new PreferencesViewModel(t._settings), null),
        ["FileDocumentViewModel"] = t =>
        {
            var vm = new FileDocumentViewModel(t.P("a.th"), File.ReadAllText(t.P("a.th")), new MeasurementsViewModel(), settings: t._settings);
            return new(vm, vm.Dispose);
        },
        ["BatchBuildViewModel"] = t => new(new BatchBuildViewModel(new BatchBuildEnvironment([t.P("cave.thconfig")], t._project.Path,
            (_, _, _) => Task.FromResult<Therion.Build.Automation.BuildOutcome?>(null), () => { }, null, null,
            p => t._documents.SaveDocumentAsync(t._documents.Documents.First(d => d.FilePath == p)))), null),
        ["LinkSurveyViewModel"] = t => new(t.NewLink(), null),
        ["PreviewWindowViewModel"] = t =>
        {
            // The link dialog outlives the full preview window it opens.
            var link = t.NewLink();
            var vm = PreviewWindowViewModel.ForLink(link);
            return new(vm, vm.Detach, KeptAlive: link);
        },
        ["NewFileViewModel"] = t => new(new NewFileViewModel(new NewFileEnvironment(FileTemplateCatalog.BuiltIns, t._project.Path, t._project.Path,
            [t.P("a.th"), t.P("b.th")], new HashSet<string>(), t.ReadText, File.Exists, _ => null, t._editor,
            (_, _) => Task.CompletedTask, null)), null),
        ["NewTripViewModel"] = t => new(new NewTripViewModel(new NewTripEnvironment(t._session.Model!, t._project.Path, t.ReadText, File.Exists,
            t._editor, (_, _) => Task.CompletedTask, new DateTime(2026, 11, 5), t._history)), null),
        ["RenumberStationsViewModel"] = t => new(new RenumberStationsViewModel(t._session.Model!, t.ReadText), null),
        ["SuggestEquatesViewModel"] = t => new(new SuggestEquatesViewModel(t._session.Model!, t._project.Path), null),
        ["SyncMapsViewModel"] = t => new(new SyncMapsViewModel(t._session.Model!, t._project.Path), null),
        ["QuickExportViewModel"] = _ => new(new QuickExportViewModel(), null),
        ["ScaffoldProjectViewModel"] = _ => new(new ScaffoldProjectViewModel(), null),
        ["NormalizeProjectViewModel"] = _ => new(new NormalizeProjectViewModel(), null),
        ["DocumentSwitcherViewModel"] = _ => new(new DocumentSwitcherViewModel(), null),
        ["RecoveryBannerViewModel"] = _ => new(new RecoveryBannerViewModel(), null),
        ["StationSelectorViewModel"] = _ => new(new ViewModels.Pickers.StationSelectorViewModel("Source"), null),
    };

    private LinkSurveyViewModel NewLink() =>
        new(new LinkSurveyEnvironment(_session.Model!, _project.Path, ReadText, _ => [], (_, _) => false, _editor,
            P("a.th"), 0, new DateOnly(2026, 11, 5), _history));

    // Given only values (text, names, parsed data, callbacks), so they have nothing long-lived to stay
    // subscribed to; each is owned by a view-model above or by a singleton.
    private static readonly HashSet<string> ValueHolders =
    [
        "EditPreviewViewModel", "ItemPickerViewModel", "StationPickerViewModel", "SurveyPickerViewModel",
        "LogFieldViewModel", "LogSectionViewModel", "LogSummaryViewModel", "NavigableSymbolViewModel",
        "MeasurementsViewModel", "NewProjectViewModel", "PasteShotsViewModel", "QuickPickViewModel",
    ];

    // One per app; MainWindowMruLifetimeTests covers what it keeps of closed documents.
    private const string Shell = "MainWindowViewModel";

    public static IEnumerable<object[]> RowNames() => Rows.Keys.Select(k => new object[] { k });

    // Rows whose view-model posts work back to the UI thread as soon as it opens (a document parses and
    // posts the result). They run on a thread bound to the UI dispatcher, pumped as the app's loop would:
    // a posted job holds its view-model until it has run, and nothing pumps the dispatcher otherwise.
    private static readonly HashSet<string> PostsToTheUiThread = ["FileDocumentViewModel"];

    [Theory]
    [MemberData(nameof(RowNames))]
    public void A_closed_view_model_is_collectable(string name)
    {
        if (PostsToTheUiThread.Contains(name)) UiThread.Run(() => OpenCloseAndCollect(name, pump: true));
        else OpenCloseAndCollect(name, pump: false);
    }

    private void OpenCloseAndCollect(string name, bool pump)
    {
        var (weak, keptAlive) = OpenAndClose(name, pump);

        // Work the view-model started may hold it for a moment after it is closed.
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            if (pump) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!weak.IsAlive) break;
            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < until);

        Assert.False(weak.IsAlive, $"a closed {name} is still reachable (a subscription to a long-lived service, a timer or a static keeps it)");
        GC.KeepAlive(keptAlive);
        GC.KeepAlive(_session);
        GC.KeepAlive(_documents);
        GC.KeepAlive(_history);
        GC.KeepAlive(_editor);
        GC.KeepAlive(_settings);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference Weak, object? KeptAlive) OpenAndClose(string name, bool pump)
    {
        var opened = Rows[name](this);
        Assert.Equal(name, opened.ViewModel.GetType().Name);
        // Open for a moment, as a window is, so what it started has run before it closes.
        for (int k = 0; k < 4; k++)
        {
            if (pump) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }
        opened.Close?.Invoke();
        return (new WeakReference(opened.ViewModel), opened.KeptAlive);
    }

    [Fact]
    public void Every_view_model_of_the_app_is_placed()
    {
        var types = typeof(MainWindowViewModel).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("ViewModel", StringComparison.Ordinal)
                        && t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Select(t => t.Name)
            .ToHashSet();
        var singletons = Singletons();

        var unplaced = types.Where(n => !singletons.Contains(n) && !Rows.ContainsKey(n) && !ValueHolders.Contains(n) && n != Shell).Order().ToList();
        Assert.True(unplaced.Count == 0,
            "view-models with no lifetime row: " + string.Join(", ", unplaced) + " — add a row (made over the long-lived services and closed as the app closes it), or list it as a value holder");

        var twice = Rows.Keys.Concat(ValueHolders).Where(singletons.Contains).ToList();
        Assert.True(twice.Count == 0, "registered as singletons, so not per window: " + string.Join(", ", twice));
        var stale = Rows.Keys.Concat(ValueHolders).Where(n => !types.Contains(n)).ToList();
        Assert.True(stale.Count == 0, "listed here but not in the app any more: " + string.Join(", ", stale));
    }

    // The view-models AppServices registers as singletons, read from its source.
    private static HashSet<string> Singletons()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir.FullName, "ThIDE", "AppServices.cs"));
        var names = Regex.Matches(source, @"AddSingleton<(?:\w+\.)*(\w+ViewModel)>\(")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Contains("ReplaceInFilesViewModel", names);   // the scan reads what it should
        return names;
    }
}
