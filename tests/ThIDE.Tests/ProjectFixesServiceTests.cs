// The project-wide fixes without a window: what Fix all refuses, what the preview is shown, what an
// answered preview writes, and how the outcome is reported (one undo step, or a warning).

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using Therion.Workspace.Editing;
using Therion.Workspace.Formatting;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class ProjectFixesServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private sealed class Notes : INotificationService
    {
        public List<AppNotification> Items { get; } = [];
        public event EventHandler<AppNotification>? Posted { add { } remove { } }
        public event EventHandler? UnreadChanged { add { } remove { } }
        public ObservableCollection<AppNotification> History { get; } = [];
        public int UnreadCount => 0;
        public void Post(AppNotification n) => Items.Add(n);
        public void Info(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Info, a, ac));
        public void Success(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Success, a, ac));
        public void Warning(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Warning, a, ac));
        public void Error(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Error, a, ac));
        public void MarkAllRead() { }
        public void Clear() { }
    }

    private sealed class History : IWorkspaceUndoService
    {
        public List<WorkspaceUndoRecord> Pushed { get; } = [];
        public event EventHandler? Changed { add { } remove { } }
        event EventHandler<WorkspaceUndoRecord>? IWorkspaceUndoService.Pushed { add { } remove { } }
        public WorkspaceUndoRecord? NextUndo => Pushed.LastOrDefault();
        public WorkspaceUndoRecord? NextRedo => null;
        public void Push(WorkspaceUndoRecord record) => Pushed.Add(record);
        public Task<WorkspaceUndoOutcome> UndoAsync(WorkspaceUndoRecord? record = null) => throw new NotSupportedException();
        public Task<WorkspaceUndoOutcome> RedoAsync() => throw new NotSupportedException();
    }

    /// <summary>A session that is not watching anything: every async member completes at once.</summary>
    private static IWorkspaceSession QuietSession() => WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m =>
        m.ReturnType == typeof(Task) ? Task.CompletedTask
        : m.ReturnType == typeof(Task<bool>) ? Task.FromResult(true)
        : null);

    /// <summary>The shell's document list stand-in, with a fixed project model.</summary>
    private static IDocumentService Documents(Therion.Semantics.WorkspaceSemanticModel? model, params FileDocumentViewModel[] open)
    {
        var docs = new ObservableCollection<FileDocumentViewModel>(open);
        return WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Documents" => docs,
            "get_Workspace" => model,
            _ => null,
        });
    }

    private sealed record Rig(ProjectFixesService Fixes, Notes Notes, History? History, List<EditPreviewViewModel> Shown);

    private static Rig NewRig(IDocumentService docs, bool apply, bool withHistory = true, IAppSettingsService? settings = null)
    {
        var notes = new Notes();
        var history = withHistory ? new History() : null;
        var shown = new List<EditPreviewViewModel>();
        var session = QuietSession();
        var fixes = new ProjectFixesService(docs, session, new WorkspaceEditService(docs, session), notes, history, settings,
            preview => { shown.Add(preview); return Task.FromResult(apply); });
        return new Rig(fixes, notes, history, shown);
    }

    private static EditBatch Replace(string path, string text, string what, string with) =>
        new([new FileTextEdits(path, [new TextEdit(text.IndexOf(what, StringComparison.Ordinal), what.Length, what, with)])], [], []);

    [Fact]
    public void The_current_text_is_the_open_buffer_else_the_file_by_its_encoding()
    {
        var open = _dir.Write("open.th", "survey open\nendsurvey\n");
        var closed = _dir.Write("closed.th", "survey closed\nendsurvey\n");
        var doc = new FileDocumentViewModel(open, "survey open\nendsurvey\n", new MeasurementsViewModel())
        {
            DocumentText = "survey open\n  # unsaved\nendsurvey\n",
        };
        var rig = NewRig(Documents(null, doc), apply: false);

        Assert.Equal(doc.DocumentText, rig.Fixes.CurrentText(open));
        Assert.Equal("survey closed\nendsurvey\n", rig.Fixes.CurrentText(closed));
        Assert.Null(rig.Fixes.CurrentText(Path.Combine(_dir.Path, "missing.th")));
    }

    [Fact]
    public async Task Fix_all_asks_to_save_first_when_a_file_with_that_code_has_unsaved_edits()
    {
        var path = _dir.Write("a.th", "survey a\nendsurvey\n");
        var doc = new FileDocumentViewModel(path, "survey a\nendsurvey\n", new MeasurementsViewModel()) { DocumentText = "survey a\n\nendsurvey\n" };
        var rig = NewRig(Documents(null, doc), apply: true);
        var d = Diagnostic.Create("X001", DiagnosticSeverity.Warning, "x", new SourceSpan(path, new SourceLocation(1, 1), new SourceLocation(1, 1), 0, 0));

        await rig.Fixes.FixAllAsync("X001", [d]);

        var note = Assert.Single(rig.Notes.Items);
        Assert.Equal(NotificationKind.Warning, note.Kind);
        Assert.Contains("a.th", note.Message);
        Assert.Empty(rig.Shown);
        Assert.Equal("survey a\nendsurvey\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task Fix_all_with_nothing_to_fix_says_so_without_a_preview()
    {
        var path = _dir.Write("a.th", "survey a\nendsurvey\n");
        var rig = NewRig(Documents(null), apply: true);
        var d = Diagnostic.Create("no-such-fix", DiagnosticSeverity.Warning, "x", new SourceSpan(path, new SourceLocation(1, 1), new SourceLocation(1, 1), 0, 0));

        await rig.Fixes.FixAllAsync("no-such-fix", [d]);

        Assert.Equal(NotificationKind.Info, Assert.Single(rig.Notes.Items).Kind);
        Assert.Empty(rig.Shown);
    }

    [Fact]
    public async Task A_declined_preview_writes_nothing_and_reports_nothing()
    {
        const string text = "survey a\n  # old\nendsurvey\n";
        var path = _dir.Write("a.th", text);
        var rig = NewRig(Documents(null), apply: false);

        Assert.False(await rig.Fixes.PreviewAndApplyAsync("Title", "summary", Replace(path, text, "old", "new")));

        Assert.Single(rig.Shown);
        Assert.Equal(text, File.ReadAllText(path));
        Assert.Empty(rig.Notes.Items);
        Assert.Empty(rig.History!.Pushed);
    }

    [Fact]
    public async Task An_applied_preview_writes_the_ticked_edits_as_one_undo_step()
    {
        const string text = "survey a\n  # old\nendsurvey\n";
        var path = _dir.Write("a.th", text);
        var rig = NewRig(Documents(null), apply: true);

        Assert.True(await rig.Fixes.PreviewAndApplyAsync("Title", "summary", Replace(path, text, "old", "new")));

        Assert.Equal("survey a\n  # new\nendsurvey\n", File.ReadAllText(path));
        var record = Assert.Single(rig.History!.Pushed);
        Assert.Equal(path, Assert.Single(record.Files).Path);
        Assert.Empty(rig.Notes.Items);   // the undo record's own notification says it
    }

    [Fact]
    public async Task Without_an_undo_history_an_applied_preview_reports_success()
    {
        const string text = "survey a\n  # old\nendsurvey\n";
        var path = _dir.Write("a.th", text);
        var rig = NewRig(Documents(null), apply: true, withHistory: false);

        Assert.True(await rig.Fixes.PreviewAndApplyAsync("Title", "summary", Replace(path, text, "old", "new")));

        Assert.Equal(NotificationKind.Success, Assert.Single(rig.Notes.Items).Kind);
    }

    [Fact]
    public async Task A_batch_planned_against_text_that_changed_since_warns_and_writes_nothing()
    {
        const string text = "survey a\n  # old\nendsurvey\n";
        var path = _dir.Write("a.th", text);
        var batch = Replace(path, text, "old", "new");
        File.WriteAllText(path, "survey a\n  # changed meanwhile\nendsurvey\n");
        var rig = NewRig(Documents(null), apply: true);

        Assert.False(await rig.Fixes.PreviewAndApplyAsync("Title", "summary", batch));

        Assert.Equal(NotificationKind.Warning, Assert.Single(rig.Notes.Items).Kind);
        Assert.Equal("survey a\n  # changed meanwhile\nendsurvey\n", File.ReadAllText(path));
        Assert.Empty(rig.History!.Pushed);
    }

    [Fact]
    public async Task Path_separator_fix_with_nothing_to_fix_says_so_without_a_preview()
    {
        var path = _dir.Write("a.th", "survey a\n  input b.th\nendsurvey\n");
        _dir.Write("b.th", "survey b\nendsurvey\n");
        var rig = NewRig(Documents(null), apply: true);

        await rig.Fixes.FixPathSeparatorsAsync(path);

        Assert.Equal(NotificationKind.Info, Assert.Single(rig.Notes.Items).Kind);
        Assert.Empty(rig.Shown);
    }

    [Fact]
    public async Task Format_all_with_nothing_to_change_still_opens_the_preview_and_writes_nothing()
    {
        var cfg = _dir.Write("thconfig", "source cave.th\n");
        var text = TherionReindenter.Format("survey cave\ncentreline\ndata normal from to length compass clino\n1 2 5 0 0\nendcentreline\nendsurvey\n", "  ");
        var cave = _dir.Write("cave.th", text);
        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);
        var model = ws.BuildSemanticModel();
        var rig = NewRig(Documents(model), apply: true);

        await rig.Fixes.FormatAllAsync();

        var preview = Assert.Single(rig.Shown);
        Assert.True(preview.IsEmpty);
        int files = ProjectFixesService.ProjectSourceFiles(model).Distinct(FilePaths.Comparer).Count();
        Assert.Equal(EditPreviewViewModel.NothingToChange(files, 0), preview.Summary);
        Assert.Equal(text, File.ReadAllText(cave));
        Assert.Empty(rig.History!.Pushed);
    }

    [Fact]
    public void Renumbering_without_a_project_says_so_and_opens_no_dialog()
    {
        var rig = NewRig(Documents(null), apply: true);

        Assert.Null(rig.Fixes.PrepareRenumber());
        Assert.Equal(NotificationKind.Info, Assert.Single(rig.Notes.Items).Kind);
    }

    [Theory]
    [InlineData(true, 2, "  ")]
    [InlineData(true, 4, "    ")]
    [InlineData(false, 4, "\t")]
    public void The_indent_unit_follows_the_editor_settings(bool spaces, int size, string expected)
    {
        var settings = new FakeSettings(AppSettings.Default with { ConvertTabsToSpaces = spaces, IndentationSize = size });
        var rig = NewRig(Documents(null), apply: false, settings: settings);

        Assert.Equal(expected, rig.Fixes.IndentUnit());
    }

    [Fact]
    public void Project_sources_are_the_parsed_files_and_the_include_graph_but_no_other_kinds()
    {
        _dir.Write("cave.th", "survey cave\nendsurvey\n");
        _dir.Write("cave.th2", "encoding utf-8\n");
        _dir.Write("notes.txt", "survey notes\n");
        _dir.Write("sub/deep.thconfig", "source ../cave.th\n");

        var under = ProjectFixesService.TherionFilesUnder(_dir.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        Assert.Equal(new[] { "cave.th", "cave.th2", "deep.thconfig" }, under);
        Assert.Empty(ProjectFixesService.TherionFilesUnder(null));
        Assert.Empty(ProjectFixesService.TherionFilesUnder(Path.Combine(_dir.Path, "missing")));
    }
}
