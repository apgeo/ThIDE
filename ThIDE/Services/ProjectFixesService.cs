// The project-wide fixes behind the Diagnostics panel (Fix, Fix all) and the Tools menu (Format all,
// Normalise project, Renumber stations, Add unreferenced scraps to maps) and the path-separator banner,
// kept out of the main window so they run without one. The Therion.Workspace fixers plan over the open
// buffers and the files on disk; this class shows the plan through a preview callback, applies the
// ticked edits as one workspace undo step and reports the outcome. The window only shows its dialogs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Semantics;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;
using ThIDE.Resources;
using ThIDE.ViewModels;

namespace ThIDE.Services;

public sealed class ProjectFixesService
{
    private readonly IDocumentService? _documents;
    private readonly IWorkspaceSession? _session;
    private readonly IWorkspaceEditService? _editor;
    private readonly INotificationService? _notifications;
    private readonly IWorkspaceUndoService? _history;
    private readonly IAppSettingsService? _settings;
    private readonly Func<EditPreviewViewModel, Task<bool>> _showPreview;

    /// <param name="showPreview">Shows the edit preview (modal); true when the user chose to apply.</param>
    public ProjectFixesService(IDocumentService? documents, IWorkspaceSession? session, IWorkspaceEditService? editor,
        INotificationService? notifications, IWorkspaceUndoService? history, IAppSettingsService? settings,
        Func<EditPreviewViewModel, Task<bool>> showPreview)
    {
        _documents = documents;
        _session = session;
        _editor = editor;
        _notifications = notifications;
        _history = history;
        _settings = settings;
        _showPreview = showPreview;
    }

    /// <summary>The loaded project's model, or null when no workspace is open.</summary>
    public WorkspaceSemanticModel? Workspace => _documents?.Workspace;

    /// <summary>A file's current text: the editor buffer when open, else the file by its encoding.</summary>
    public string? CurrentText(string path)
    {
        var doc = _documents?.Documents.FirstOrDefault(d => FilePaths.Same(d.FilePath, path));
        if (doc is not null) return doc.DocumentText;
        return File.Exists(path) ? Therion.Syntax.EncodingResolver.ReadAllText(path) : null;
    }

    /// <summary>Applies the first fix for one diagnostic (Diagnostics panel ▸ Fix).</summary>
    public async Task FixOneAsync(Diagnostic d)
    {
        if (d.Span.FilePath is not { Length: > 0 } path || CurrentText(path) is not { } text) return;
        // This diagnostic's own fix only (an unrelated fix on the same line is not "fixing" it).
        var fix = CodeFixes.ForDiagnostic(text, d)
                  ?? CodeFixes.ForLine(text, d.Span.Start.Line, [d]).FirstOrDefault(f => f.DiagnosticCode == d.Code.Value);
        if (fix is null)
        {
            _notifications?.Info(Tr.Get("Diag_Fix"), Tr.Get("Diag_NoFix"));
            return;
        }
        var batch = new EditBatch([new(path, fix.Edits)], [], []);
        // A fix that cannot tell where it belongs (an end inserted after a flat block body) is shown first.
        if (!fix.SafeForBatch)
        {
            await PreviewAndApplyAsync(Tr.Get("Diag_Fix"), Tr.Get("Diag_FixCheck"), batch).ConfigureAwait(true);
            return;
        }
        if (_editor is not { } editor) return;
        var outcome = await editor.ApplyAsync(batch).ConfigureAwait(true);
        if (!outcome.Success) _notifications?.Warning(Tr.Get("Diag_Fix"), outcome.Describe());
    }

    /// <summary>Diagnostics panel ▸ Fix all '<paramref name="code"/>': every safe fix for that code, previewed, then applied.</summary>
    public async Task FixAllAsync(string code, IReadOnlyList<Diagnostic> diagnostics)
    {
        // The diagnostics' line numbers are from the last check: an open file edited since may have
        // moved them, so such files must be saved (and re-checked) first.
        var dirty = diagnostics.Where(d => d.Code.Value == code && d.Span.FilePath is { } f
                && _documents?.Documents.Any(x => x.IsDirty && FilePaths.Same(x.FilePath, f)) == true)
            .Select(d => Path.GetFileName(d.Span.FilePath)).Distinct().ToList();
        if (dirty.Count > 0)
        {
            _notifications?.Warning(string.Format(Tr.Get("Diag_FixAll"), code), string.Format(Tr.Get("Diag_FixAllSaveFirst"), string.Join(", ", dirty)));
            return;
        }
        var (batch, fixedCount, skipped) = FixAll.Plan(code, diagnostics, CurrentText);
        if (fixedCount == 0)
        {
            _notifications?.Info(string.Format(Tr.Get("Diag_FixAll"), code), Tr.Get("Diag_FixAllNone"));
            return;
        }
        await PreviewAndApplyAsync(string.Format(Tr.Get("Diag_FixAll"), code),
            string.Format(Tr.Get("Diag_FixAllSummary"), Tr.Plural("Count_Fixes", fixedCount), Tr.Plural("Rename_Files", batch.Edits.Count), skipped),
            batch, reload: true).ConfigureAwait(true);
    }

    /// <summary>Shows a preview of a batch and applies the ticked edits (one undo step per open file).</summary>
    public async Task<bool> PreviewAndApplyAsync(string title, string summary, EditBatch batch,
        IReadOnlyList<EditPreviewRow>? rows = null, bool reload = false, bool allOrNothing = false)
    {
        rows ??= FixAll.Preview(batch, CurrentText);
        var preview = new EditPreviewViewModel(title, summary, batch, rows, _session?.RootPath, allOrNothing);
        bool confirmed = await _showPreview(preview).ConfigureAwait(true);
        if (!confirmed || preview.IsEmpty || _editor is not { } editor) return false;
        var outcome = await editor.ApplyAsync(preview.Chosen()).ConfigureAwait(true);
        // Closed files change on disk without a re-parse: reload when the diagnostics must follow. Not
        // through the batch's own reload flag, which also refuses files with unsaved changes — right for
        // a batch that adds an input, wrong for one that only edits text.
        if (outcome.Success && reload && _session is { ActiveThconfig: { } active } session)
            await session.SetActiveThconfigAsync(active.FullPath).ConfigureAwait(true);
        if (outcome.Success)
        {
            var done = Tr.Plural("Diag_FixAllDone", preview.CheckedCount);
            // One undo step for every file written; the record's notification offers it.
            if (_history is { } history && outcome.Changes is { Count: > 0 } changes)
                history.Push(new WorkspaceUndoRecord(WorkspaceUndoRecord.OperationName(title), done, changes));
            else _notifications?.Success(title, done);
        }
        else _notifications?.Warning(title, outcome.Describe());
        return outcome.Success;
    }

    /// <summary>Tools ▸ Format all files: every project file through the formatter, previewed first.</summary>
    public async Task FormatAllAsync()
    {
        if (Workspace is not { } ws) return;
        var files = ProjectSourceFiles(ws);
        // The editor's Format Document: a re-indent that never reorders or drops anything.
        var unit = IndentUnit();
        var (batch, rows, skipped) = FormatAll.PlanShowingChanges(files, CurrentText, LineEndings.Keep,
            t => Therion.Workspace.Formatting.TherionReindenter.Format(t, unit), EditPreviewViewModel.DescribeFormatChange);
        var title = Tr.Get("Menu_Tools_FormatAll");
        // An empty plan still opens the preview, which then says "Nothing to change in N files".
        var summary = batch.IsEmpty
            ? EditPreviewViewModel.NothingToChange(CountFiles(files) - skipped.Count, skipped.Count)
            : EditPreviewViewModel.ChangeSummary("FormatAll_Summary", rows.Count, skipped.Count);
        await PreviewAndApplyAsync(title, summary, batch, rows, reload: true).ConfigureAwait(true);
    }

    /// <summary>
    /// The path-separator banner's workspace button: every include path in the workspace written with
    /// Windows '\' separators that '/' turns into an existing file, previewed, then rewritten.
    /// </summary>
    public async Task FixPathSeparatorsAsync(string requestingFile)
    {
        var files = new List<string> { requestingFile };
        if (Workspace is { } ws) files.AddRange(ProjectSourceFiles(ws));
        // Files outside the project graph too: a broken include may be what cut them off from it.
        files.AddRange(TherionFilesUnder(_session?.RootPath));
        var (batch, rows) = PathSeparatorFixes.Plan(files, CurrentText, File.Exists);
        var title = Tr.Get("PathSep_Title");
        if (batch.IsEmpty) { _notifications?.Info(title, Tr.Get("PathSep_None")); return; }
        await PreviewAndApplyAsync(title, string.Format(Tr.Get("PathSep_Summary"), Tr.Plural("Count_IncludePaths", rows.Count), Tr.Plural("Rename_Files", batch.Edits.Count)),
            batch, rows, reload: true).ConfigureAwait(true);
    }

    /// <summary>Tools ▸ Normalise project, once its options are chosen: Format all plus line endings, UTF-8 and sorted inputs, previewed first.</summary>
    public async Task NormalizeAsync(WorkspaceSemanticModel ws, NormalizeOptions options)
    {
        var title = Tr.Get("Menu_Tools_Normalize");
        if (options.IsEmpty) return;
        var files = ProjectSourceFiles(ws);
        var (batch, rows, skipped) = ProjectNormalize.PlanShowingChanges(files, CurrentText, options,
            EditPreviewViewModel.DescribeFormatChange);
        var summary = batch.IsEmpty
            ? EditPreviewViewModel.NothingToChange(CountFiles(files) - skipped.Count, skipped.Count)
            : EditPreviewViewModel.ChangeSummary("Normalize_Summary", rows.Count, skipped.Count);
        await PreviewAndApplyAsync(title, summary, batch, rows, reload: true).ConfigureAwait(true);
    }

    /// <summary>Tools ▸ Renumber stations: the dialog's model, or null (and a note) when the project has no surveys.</summary>
    public RenumberStationsViewModel? PrepareRenumber()
    {
        if (Workspace is not { SurveysByFullName.Count: > 0 } ws)
        {
            _notifications?.Info(Tr.Get("Menu_Tools_Renumber"), Tr.Get("Renumber_NoSurveys"));
            return null;
        }
        return new RenumberStationsViewModel(ws, CurrentText);
    }

    /// <summary>Tools ▸ Renumber stations, once a plan is chosen: previewed, all or nothing.</summary>
    public async Task RenumberAsync(RenumberPlan plan, string summary)
    {
        // One row per changed line, showing the line as it will read (a shot line renames two stations).
        var rows = FixAll.PreviewByLine(plan.Batch, CurrentText);
        await PreviewAndApplyAsync(Tr.Get("Menu_Tools_Renumber"), summary, plan.Batch, rows, reload: true, allOrNothing: true).ConfigureAwait(true);
    }

    /// <summary>Tools ▸ Add unreferenced scraps to maps: the dialog's model, or null (and a note) when there is nothing to choose.</summary>
    public SyncMapsViewModel? PrepareSyncMaps(WorkspaceSemanticModel ws)
    {
        var model = new SyncMapsViewModel(ws, _session?.RootPath);
        if (model.Rows.Count == 0 || !model.HasMaps)
        {
            _notifications?.Info(Tr.Get("Menu_Tools_SyncMaps"), Tr.Get(model.Rows.Count == 0 ? "SyncMaps_Nothing" : "SyncMaps_NoMaps"));
            return null;
        }
        return model;
    }

    /// <summary>Tools ▸ Add unreferenced scraps to maps, once the maps are chosen.</summary>
    public async Task SyncMapsAsync(WorkspaceSemanticModel ws, SyncMapsViewModel model)
    {
        var title = Tr.Get("Menu_Tools_SyncMaps");
        var cache = new Dictionary<string, Therion.Syntax.TherionFile?>(FilePaths.Comparer);
        Therion.Syntax.TherionFile? Ast(string p) => cache.TryGetValue(p, out var a) ? a
            : cache[p] = p.EndsWith(".th", StringComparison.OrdinalIgnoreCase) && CurrentText(p) is { } t ? new Therion.Syntax.ThParser().Parse(p, t).Value : null;
        var scopes = Therion.Semantics.Linking.SurveyScopeResolver.Build(ws, Ast);
        var (batch, problems) = MapSync.Plan(ws, scopes, model.Assignments(), CurrentText, Ast);
        var summary = problems.Count == 0 ? Tr.Get("SyncMaps_Summary")
            : Tr.Get("SyncMaps_Summary") + " " + string.Format(Tr.Get("SyncMaps_Problems"),
                string.Join(", ", problems.Select(p => $"{p.Map} ({Tr.Get("SyncMaps_" + p.Problem)})")));
        if (batch.IsEmpty) { _notifications?.Warning(title, summary); return; }
        await PreviewAndApplyAsync(title, summary, batch, reload: true).ConfigureAwait(true);
    }

    /// <summary>The editor's indent unit (Settings ▸ Editor), for the formatter.</summary>
    public string IndentUnit()
    {
        var settings = _settings?.Current;
        return Therion.Workspace.Formatting.TherionReindenter.IndentUnit(settings is { ConvertTabsToSpaces: false }, settings?.IndentationSize ?? 2);
    }

    private static int CountFiles(IEnumerable<string> files) => files.Distinct(FilePaths.Comparer).Count();

    /// <summary>The project's Therion sources: every parsed file and every file the include graph names.</summary>
    public static List<string> ProjectSourceFiles(WorkspaceSemanticModel ws) =>
        ws.PerFile.Keys
            .Concat(ws.FileGraphEdges.SelectMany(e => new[] { e.From, e.To }))
            .Where(f => f.EndsWith(".th", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".th2", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".thconfig", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Every .th / .th2 / thconfig file below <paramref name="root"/> (none when it is unset).</summary>
    public static IEnumerable<string> TherionFilesUnder(string? root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return [];
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        try
        {
            return Directory.EnumerateFiles(root, "*", opts).Where(f =>
                Path.GetExtension(f).ToLowerInvariant() is ".th" or ".th2" or ".thconfig" or ".thc"
                || string.Equals(Path.GetFileName(f), "thconfig", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
