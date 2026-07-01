// (survey tree), (dashboard) and (orphan/dead-file audit) content
// view-models. All read the shared WorkspaceSemanticModel (via IWorkspaceSession) and refresh
// when the object graph is rebuilt; click-to-source goes through IDocumentService.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Services;

namespace TherionProc.ViewModels;

internal static class ProjectFormat
{
    /// <summary>Human length: metres under 1 km, kilometres above, both 1-dp.</summary>
    public static string Length(double metres) =>
        metres >= 1000 ? $"{metres / 1000.0:0.0##} km" : $"{metres:0.0} m";

    public static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}

// ───────────────────────────── — survey tree ─────────────────────────────

/// <summary>One node in the logical survey-hierarchy tree (with rolled-up counts).</summary>
public sealed class SurveyTreeItem
{
    public string Name { get; }
    public string Counts { get; }
    public SourceSpan Declaration { get; }
    public ObservableCollection<SurveyTreeItem> Children { get; } = new();

    public SurveyTreeItem(SurveyTreeNode node)
    {
        Name = string.IsNullOrEmpty(node.Title) ? node.Name : $"{node.Name}  ·  {node.Title}";
        Counts = $"{node.Stations} st · {node.Shots} legs · {ProjectFormat.Length(node.Length)}";
        Declaration = node.Declaration;
        foreach (var c in node.Children) Children.Add(new SurveyTreeItem(c));
    }
}

public sealed partial class SurveyTreeViewModel : ObservableObject
{
    private readonly IWorkspaceSession? _session;
    private readonly IDocumentService? _documents;

    public ObservableCollection<SurveyTreeItem> Roots { get; } = new();
    [ObservableProperty] private bool _isEmpty = true;

    private SurveyTreeItem? _selected;
    public SurveyTreeItem? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value) && value is not null) Navigate(value.Declaration); }
    }

    public SurveyTreeViewModel() { } // design-time

    public SurveyTreeViewModel(IWorkspaceSession session, IDocumentService documents)
    {
        _session = session;
        _documents = documents;
        _session.Changed += (_, _) => ProjectFormat.OnUi(Rebuild);
        _documents.DocumentChanged += (_, _) => ProjectFormat.OnUi(Rebuild);
        Rebuild();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    private void Rebuild()
    {
        Roots.Clear();
        if (_documents?.Workspace is { } model)
            foreach (var n in ProjectStatistics.BuildSurveyTree(model))
                Roots.Add(new SurveyTreeItem(n));
        IsEmpty = Roots.Count == 0;
    }

    private void Navigate(SourceSpan span)
    {
        if (!span.IsEmpty && !string.IsNullOrEmpty(span.FilePath))
            _ = _documents?.NavigateToSpanAsync(span);
    }
}

// ───────────────────────────── — dashboard ─────────────────────────────

public sealed partial class ProjectDashboardViewModel : ObservableObject
{
    private readonly IWorkspaceSession? _session;
    private readonly IDocumentService? _documents;

    public BuildViewModel? Build { get; }

    [ObservableProperty] private string _projectName = "(no project open)";
    [ObservableProperty] private string _entryConfig = "—";
    [ObservableProperty] private string _fileSummary = "—";
    [ObservableProperty] private string _surveySummary = "—";
    [ObservableProperty] private string _lengthText = "—";
    [ObservableProperty] private string _depthText = "—";
    [ObservableProperty] private string _stationsText = "—";
    [ObservableProperty] private string _entrancesText = "—";
    [ObservableProperty] private string _buildStatus = "Never built this session";
    [ObservableProperty] private string _diagnosticsText = "—";

    public ProjectDashboardViewModel() { } // design-time

    public ProjectDashboardViewModel(IWorkspaceSession session, IDocumentService documents, BuildViewModel build)
    {
        _session = session;
        _documents = documents;
        Build = build;
        _session.Changed += (_, _) => ProjectFormat.OnUi(Rebuild);
        _session.RootChanged += (_, _) => ProjectFormat.OnUi(Rebuild);
        build.PropertyChanged += (_, _) => ProjectFormat.OnUi(UpdateBuildStatus);
        Rebuild();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    private void Rebuild()
    {
        var root = _session?.RootPath;
        ProjectName = string.IsNullOrEmpty(root) ? "(no project open)"
            : new DirectoryInfo(root).Name;
        EntryConfig = _session?.ActiveThconfig?.DisplayPath ?? "—";
        FileSummary = CountFiles(root);

        if (_documents?.Workspace is { } model)
        {
            var t = ProjectStatistics.ComputeTotals(model);
            SurveySummary = $"{t.SurveyCount} surveys";
            StationsText = $"{t.StationCount} stations · {t.ShotCount} legs";
            LengthText = ProjectFormat.Length(t.TotalLength);
            DepthText = $"{ProjectFormat.Length(t.VerticalRange)} vertical range";
            EntrancesText = $"{t.EntranceCount} entrances · {t.FixedCount} fixed points";
            int errors = model.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            int warnings = model.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            DiagnosticsText = errors == 0 && warnings == 0 ? "No diagnostics" : $"{errors} errors · {warnings} warnings";
        }
        else
        {
            SurveySummary = StationsText = LengthText = DepthText = EntrancesText = DiagnosticsText = "—";
        }
        UpdateBuildStatus();
    }

    private void UpdateBuildStatus()
    {
        if (Build is not { HasBuildResult: true })
        {
            BuildStatus = "Never built this session";
            return;
        }
        BuildStatus = Build.LastBuildSucceeded
            ? Build.LastBuildHasWarnings ? $"Last build succeeded ({Build.LastBuildWarningCount} warnings)" : "Last build succeeded"
            : "Last build failed";
    }

    private static string CountFiles(string? root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return "—";
        try
        {
            int th = 0, th2 = 0, cfg = 0;
            var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(root, "*.*", opts))
            {
                switch (Path.GetExtension(f).ToLowerInvariant())
                {
                    case ".th": th++; break;
                    case ".th2": th2++; break;
                    case ".thconfig": case ".thc": cfg++; break;
                }
            }
            return $"{th} .th · {th2} .th2 · {cfg} .thconfig";
        }
        catch { return "—"; }
    }
}

// ───────────────────────────── — orphan / dead-file audit ─────────────────────────────

/// <summary>One audit finding (orphan file / unreferenced scrap / unexported map).</summary>
public sealed class AuditItem
{
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public SourceSpan? Span { get; init; }
    public string? FilePath { get; init; }
}

public sealed partial class ProjectAuditViewModel : ObservableObject
{
    private readonly IWorkspaceSession? _session;
    private readonly IDocumentService? _documents;

    public ObservableCollection<AuditItem> OrphanFiles { get; } = new();
    public ObservableCollection<AuditItem> UnreferencedScraps { get; } = new();
    public ObservableCollection<AuditItem> UnexportedMaps { get; } = new();

    [ObservableProperty] private bool _isClean;
    [ObservableProperty] private string _summary = "—";

    public ProjectAuditViewModel() { } // design-time

    public ProjectAuditViewModel(IWorkspaceSession session, IDocumentService documents)
    {
        _session = session;
        _documents = documents;
        _session.Changed += (_, _) => ProjectFormat.OnUi(Rebuild);
        _session.FileSystemChanged += (_, _) => ProjectFormat.OnUi(Rebuild);
        Rebuild();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    [RelayCommand]
    private void Open(AuditItem? item)
    {
        if (item is null) return;
        if (item.Span is { IsEmpty: false } span) _ = _documents?.NavigateToSpanAsync(span);
        else if (!string.IsNullOrEmpty(item.FilePath)) _ = _documents?.OpenFileAsync(item.FilePath);
    }

    private void Rebuild()
    {
        OrphanFiles.Clear();
        UnreferencedScraps.Clear();
        UnexportedMaps.Clear();

        var model = _documents?.Workspace;
        FindOrphanFiles(model);
        if (model is not null)
        {
            foreach (var id in ProjectStatistics.UnreferencedScraps(model))
                UnreferencedScraps.Add(new AuditItem
                {
                    Title = id,
                    Detail = "scrap not composed by any map",
                    Span = model.ScrapsById.TryGetValue(id, out var s) ? s.DeclarationSpan : null,
                });
            FindUnexportedMaps(model);
        }

        int total = OrphanFiles.Count + UnreferencedScraps.Count + UnexportedMaps.Count;
        IsClean = total == 0;
        Summary = IsClean
            ? "No orphan files, unreferenced scraps or unexported maps."
            : $"{OrphanFiles.Count} orphan file(s) · {UnreferencedScraps.Count} unreferenced scrap(s) · {UnexportedMaps.Count} unexported map(s)";
    }

    // Therion files on disk under the root not reachable from the active entry point's include graph.
    private void FindOrphanFiles(WorkspaceSemanticModel? model)
    {
        var root = _session?.RootPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _session!.Candidates) reachable.Add(Path.GetFullPath(c.FullPath));
        if (model is not null)
            foreach (var (from, to) in model.FileGraphEdges) { reachable.Add(from); reachable.Add(to); }

        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(root, "*.*", opts))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".th" or ".th2")) continue;
                var full = Path.GetFullPath(f);
                if (reachable.Contains(full)) continue;
                OrphanFiles.Add(new AuditItem
                {
                    Title = Path.GetFileName(f),
                    Detail = Path.GetDirectoryName(f) ?? string.Empty,
                    FilePath = full,
                });
                if (OrphanFiles.Count >= 500) break; // guard pathological trees
            }
        }
        catch { /* best effort */ }
    }

    // Declared maps not picked by any `select` in the active thconfig (only when selects exist).
    private void FindUnexportedMaps(WorkspaceSemanticModel model)
    {
        if (model.MapsById.Count == 0) return;
        var entry = _session?.ActiveThconfig?.FullPath;
        if (string.IsNullOrEmpty(entry) || !File.Exists(entry)) return;

        var selected = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var ast = new ThconfigParser().Parse(entry, File.ReadAllText(entry)).Value;
            if (ast is not null)
                foreach (var node in ast.Children)
                    if (node is SelectCommand { IsUnselect: false } sel && !string.IsNullOrEmpty(sel.Object))
                        selected.Add(MapIdOf(sel.Object));
        }
        catch { return; }
        if (selected.Count == 0) return; // nothing selected explicitly → don't flag everything

        foreach (var (id, map) in model.MapsById)
            if (!selected.Contains(id))
                UnexportedMaps.Add(new AuditItem
                {
                    Title = id,
                    Detail = "map declared but not selected for export",
                    Span = map.DeclarationSpan,
                });
    }

    private static string MapIdOf(string selectObject)
    {
        var at = selectObject.IndexOf('@');
        return at > 0 ? selectObject[..at] : selectObject;
    }
}

// ───────────────────────────── — leads register ─────────────────────────────

/// <summary>One row in the Leads register: a lead plus its (sidecar) lifecycle status.</summary>
public sealed partial class LeadRow : ObservableObject
{
    public LeadRow(Lead lead, string status) { Lead = lead; _status = status; }

    public Lead Lead { get; }
    public string Location => Lead.Location;
    public string Kind => Lead.KindLabel;
    public string Description => Lead.Description;
    public string File => System.IO.Path.GetFileName(Lead.Span.FilePath ?? string.Empty);
    public int Line => Lead.Span.Start.Line;

    /// <summary>Lifecycle status: open / checked / pushed / dead.</summary>
    [ObservableProperty] private string _status;
}

/// <summary>the exploration-leads register, with lifecycle status.</summary>
public sealed partial class LeadsViewModel : ObservableObject
{
    private readonly IWorkspaceSession? _session;
    private readonly IDocumentService? _documents;
    private readonly ILeadStatusStore? _status;

    public ObservableCollection<LeadRow> Leads { get; } = new();

    [ObservableProperty] private bool _isClean;
    [ObservableProperty] private string _summary = "—";

    public LeadsViewModel() { } // design-time

    public LeadsViewModel(IWorkspaceSession session, IDocumentService documents, ILeadStatusStore status)
    {
        _session = session;
        _documents = documents;
        _status = status;
        _session.Changed += (_, _) => ProjectFormat.OnUi(Rebuild);
        Rebuild();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    [RelayCommand]
    private void Open(LeadRow? row)
    {
        if (row?.Lead.Span is { IsEmpty: false } span) _ = _documents?.NavigateToSpanAsync(span);
    }

    [RelayCommand] private void MarkOpen(LeadRow? row) => SetStatus(row, LeadStatusStore.Open);
    [RelayCommand] private void MarkChecked(LeadRow? row) => SetStatus(row, "checked");
    [RelayCommand] private void MarkPushed(LeadRow? row) => SetStatus(row, "pushed");
    [RelayCommand] private void MarkDead(LeadRow? row) => SetStatus(row, "dead");

    /// <summary>lite: copy the leads list as a Markdown table for a trip sheet.</summary>
    [RelayCommand]
    private void CopyAsMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Location | Kind | Status | Description | File | Line |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in Leads)
            sb.AppendLine($"| {r.Location} | {r.Kind} | {r.Status} | {r.Description} | {r.File} | {r.Line} |");
        ClipboardHelper.SetText(sb.ToString());
    }

    private void SetStatus(LeadRow? row, string status)
    {
        if (row is null) return;
        _status?.Set(_session?.RootPath, row.Location, status);
        row.Status = status;
        UpdateSummary();
    }

    private void Rebuild()
    {
        Leads.Clear();
        var model = _documents?.Workspace;
        foreach (var lead in LeadAnalysis.Analyze(model))
        {
            var st = _status?.Get(_session?.RootPath, lead.Location) ?? LeadStatusStore.Open;
            Leads.Add(new LeadRow(lead, st));
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        IsClean = Leads.Count == 0;
        int open = Leads.Count(l => string.Equals(l.Status, LeadStatusStore.Open, StringComparison.OrdinalIgnoreCase));
        Summary = IsClean
            ? "No leads detected."
            : $"{Leads.Count} lead(s) · {open} open";
    }
}

// ───────────────────────────── — TODO / FIXME / QM aggregator ─────────────────────────────

/// <summary>One scanned comment tag (TODO/FIXME/QM/…) with its location.</summary>
public sealed record TodoRow(string Tag, string Text, string File, int Line, SourceSpan Span);

/// <summary>aggregates TODO/FIXME/QM comment tags across all project files.</summary>
public sealed partial class TodoScanViewModel : ObservableObject
{
    private readonly IWorkspaceSession? _session;
    private readonly IDocumentService? _documents;
    private readonly IAppSettingsService? _settings;
    private const int MaxFiles = 3000;

    public ObservableCollection<TodoRow> Todos { get; } = new();

    [ObservableProperty] private string _summary = "—";

    public TodoScanViewModel() { } // design-time

    public TodoScanViewModel(IWorkspaceSession session, IDocumentService documents, IAppSettingsService settings)
    {
        _session = session;
        _documents = documents;
        _settings = settings;
        _session.Changed += (_, _) => ProjectFormat.OnUi(Rebuild);
        Rebuild();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    [RelayCommand]
    private void Open(TodoRow? row)
    {
        if (row?.Span is { IsEmpty: false } span) _ = _documents?.NavigateToSpanAsync(span);
    }

    /// <summary>Copy the TODO list as a Markdown table (for a write-up / issue tracker).</summary>
    [RelayCommand]
    private void CopyAsMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Tag | Text | File | Line |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var t in Todos) sb.AppendLine($"| {t.Tag} | {t.Text} | {t.File} | {t.Line} |");
        ClipboardHelper.SetText(sb.ToString());
    }

    private void Rebuild()
    {
        Todos.Clear();
        if (_settings is { Current.EnableTodoScan: false }) { Summary = "TODO scan disabled (Preferences ▸ Performance)."; return; }

        // Prefer unsaved editor text for open files; read the rest from disk. Snapshot the live
        // Documents collection — it can be mutated (open/close) while this scan runs.
        var openText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_documents is not null)
            foreach (var d in _documents.Documents.ToList())
                if (!string.IsNullOrEmpty(d.FilePath)) openText[d.FilePath] = d.DocumentText;

        int scanned = 0;
        foreach (var path in ReachableFiles())
        {
            if (scanned++ >= MaxFiles) break;
            string text;
            if (openText.TryGetValue(path, out var t)) text = t;
            else { try { text = Therion.Syntax.EncodingResolver.ReadAllText(path); } catch { continue; } }

            foreach (var item in TodoScanner.Scan(path, text))
                Todos.Add(new TodoRow(item.Tag, item.Text, Path.GetFileName(path), item.Span.Start.Line, item.Span));
        }

        Summary = Todos.Count == 0 ? "No TODO/FIXME/QM tags found." : $"{Todos.Count} tag(s) in {scanned} file(s).";
    }

    // Every distinct project file: graph members + open documents.
    private IEnumerable<string> ReachableFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_documents?.Workspace is { } ws)
        {
            foreach (var p in ws.PerFile.Keys) if (seen.Add(p)) yield return p;
            foreach (var (from, to) in ws.FileGraphEdges)
            {
                if (seen.Add(from)) yield return from;
                if (seen.Add(to)) yield return to;
            }
        }
        if (_documents is not null)
            foreach (var d in _documents.Documents.ToList())
                if (!string.IsNullOrEmpty(d.FilePath) && seen.Add(d.FilePath)) yield return d.FilePath;
    }
}

// ───────────────────────────── — project metadata editor ─────────────────────────────

/// <summary>a form for project-level metadata, persisted in a per-root sidecar.</summary>
public sealed partial class ProjectMetadataViewModel : ObservableObject
{
    private readonly IWorkspaceSession? _session;
    private readonly IProjectMetadataStore? _store;
    private bool _loading;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _region = string.Empty;
    [ObservableProperty] private string _crs = string.Empty;
    [ObservableProperty] private string _declinationSource = string.Empty;
    [ObservableProperty] private string _license = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    /// <summary>False until a workspace root exists (no project → nothing to store metadata against).</summary>
    [ObservableProperty] private bool _hasProject;

    public ProjectMetadataViewModel() { } // design-time

    public ProjectMetadataViewModel(IWorkspaceSession session, IProjectMetadataStore store)
    {
        _session = session;
        _store = store;
        _session.RootChanged += (_, _) => ProjectFormat.OnUi(Load);
        Load();
    }

    [RelayCommand]
    private void Save()
    {
        if (_store is null || _session?.RootPath is not { } root) { Status = "Open a project first."; return; }
        _store.Save(root, new ProjectMetadata
        {
            Name = Name, Region = Region, Crs = Crs,
            DeclinationSource = DeclinationSource, License = License, Notes = Notes,
        });
        Status = $"Saved ({DateTime.Now:HH:mm}).";
    }

    [RelayCommand] private void Reload() => Load();

    private void Load()
    {
        HasProject = _session?.RootPath is { Length: > 0 };
        var md = _store?.Load(_session?.RootPath) ?? ProjectMetadata.Empty;
        _loading = true;
        Name = md.Name; Region = md.Region; Crs = md.Crs;
        DeclinationSource = md.DeclinationSource; License = md.License; Notes = md.Notes;
        _loading = false;
        Status = HasProject ? string.Empty : "No project open.";
    }

    // Clear the "saved" hint as soon as the user edits a field.
    partial void OnNameChanged(string value) => Touch();
    partial void OnRegionChanged(string value) => Touch();
    partial void OnCrsChanged(string value) => Touch();
    partial void OnDeclinationSourceChanged(string value) => Touch();
    partial void OnLicenseChanged(string value) => Touch();
    partial void OnNotesChanged(string value) => Touch();
    private void Touch() { if (!_loading && HasProject) Status = "Unsaved changes."; }
}

// ───────────────────────────── — background-scan / media manager ─────────────────────────────

/// <summary>lists the project's scan assets (referenced .xvi + on-disk orphans) with
/// status (referenced / missing / orphan), resolution and referencing-scrap counts.</summary>
public sealed partial class MediaManagerViewModel : ObservableObject
{
    private readonly IWorkspaceSession? _session;
    private readonly IDocumentService? _documents;
    private readonly IAppSettingsService? _settings;

    public ObservableCollection<MediaItem> Media { get; } = new();

    [ObservableProperty] private string _summary = "—";

    public MediaManagerViewModel() { } // design-time

    public MediaManagerViewModel(IWorkspaceSession session, IDocumentService documents, IAppSettingsService settings)
    {
        _session = session;
        _documents = documents;
        _settings = settings;
        _session.Changed += (_, _) => ProjectFormat.OnUi(Rebuild);
        _session.FileSystemChanged += (_, _) => ProjectFormat.OnUi(Rebuild);
        Rebuild();
    }

    [RelayCommand] private void Refresh() => Rebuild();

    [RelayCommand]
    private void Reveal(MediaItem? item)
    {
        if (item is null || item.Status == MediaStatus.Missing) return;
        try { (TherionProc.AppServices.Provider.GetService(typeof(Therion.Build.IShellOpener)) as Therion.Build.IShellOpener)?.RevealInFileManager(item.Path); }
        catch { /* design-time / no container */ }
    }

    private void Rebuild()
    {
        Media.Clear();
        if (_settings is { Current.EnableMediaScan: false }) { Summary = "Media scan disabled (Preferences ▸ Performance)."; return; }

        foreach (var m in MediaScanner.ScanReferenced(_documents?.Workspace)) Media.Add(m);
        // asset health — present-but-unreferenced media on disk.
        foreach (var m in MediaScanner.ScanOrphans(_documents?.Workspace, _session?.RootPath)) Media.Add(m);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int missing = Media.Count(m => m.Status == MediaStatus.Missing);
        int orphan = Media.Count(m => m.Status == MediaStatus.Orphan);
        Summary = Media.Count == 0
            ? "No scans found."
            : $"{Media.Count} scan(s)" + (missing > 0 ? $" · {missing} missing" : "") + (orphan > 0 ? $" · {orphan} orphan" : "");
    }
}
