// Implementation Plan �7.3 � Workspace Explorer ViewModel.
// Renders the project as a Visual-Studio-style tree: the file-inclusion hierarchy
// (entry .thconfig -> source -> input -> .th2 -> .xvi -> image) with per-type icons,
// and � when enabled (#16) � each file's object model (surveys / maps / scraps,
// shown with id + title) nested underneath, each navigable to its declaration.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using Therion.Semantics;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>A node in the workspace tree: either a file (openable) or a model object (navigable).</summary>
public sealed partial class WorkspaceTreeNode : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    /// <summary>Secondary text (e.g. a survey/map title), shown dimmed after the name.</summary>
    public string? Detail { get; init; }
    /// <summary>File path for file nodes (double-click opens it).</summary>
    public string? FullPath { get; init; }

    /// <summary>True when this file is part of the active thconfig's object graph (#2).</summary>
    public bool InActiveGraph { get; init; }
    /// <summary>File-name brush: blue for files in the active graph, default otherwise (#2).</summary>
    public IBrush NameBrush => InActiveGraph
        ? new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0))
        : Brushes.Black;
    /// <summary>Declaration span for object nodes (double-click navigates to it).</summary>
    public SourceSpan? Target { get; init; }
    public string Kind { get; init; } = string.Empty;
    public ObservableCollection<WorkspaceTreeNode> Children { get; } = new();

    /// <summary>Native OS shell icon (file-explorer view); null ⇒ fall back to the kind glyph.</summary>
    [ObservableProperty] private IImage? _shellIcon;

    /// <summary>Cached "Modified: …" line for the hover tooltip (computed once at build, #2).</summary>
    public string? LastModifiedText { get; init; }
    /// <summary>Hover tooltip heading: the full path for file nodes, else the display name (#2).</summary>
    public string TooltipTitle => FullPath ?? Name;

    /// <summary>Highlighted (not selected) by a reveal-on-hover, drawn yellow so it isn't
    /// confused with the active selection (#4).</summary>
    [ObservableProperty] private bool _isLinkHighlighted;
    partial void OnIsLinkHighlightedChanged(bool value) => OnPropertyChanged(nameof(RowBackground));
    public IBrush RowBackground => IsLinkHighlighted
        ? new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xEB, 0x3B)) // semi-opaque amber
        : Brushes.Transparent;

    /// <summary>Lazy child population for file-explorer folder nodes (loaded on first expand).</summary>
    public Action<WorkspaceTreeNode>? LoadChildren { get; init; }
    private bool _childrenLoaded;

    [ObservableProperty] private bool _isExpanded;
    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_childrenLoaded && LoadChildren is { } load)
        {
            _childrenLoaded = true;
            try { load(this); } catch { /* unreadable folder — leave empty */ }
        }
    }

    /// <summary>Glyph icon for the node's kind (BMP symbols render in Segoe UI Symbol).</summary>
    public string Glyph => Kind switch
    {
        "thconfig" => "⚙", // gear
        "th"       => "▤", // data rows
        "th2"      => "✎", // pencil
        "xvi"      => "▦", // grid
        "image"    => "▩", // filled grid
        "survey"   => "◈", // diamond-in-square
        "map"      => "▣", // square-in-square
        "scrap"    => "◇", // diamond
        "missing"  => "⚠", // warning
        "folder"   => "▸", // folder
        "file"     => "▢", // file
        _          => "•", // bullet
    };

    public IBrush IconBrush => new SolidColorBrush(Kind switch
    {
        "thconfig" => Color.FromRgb(0x75, 0x75, 0x75),
        "th"       => Color.FromRgb(0x15, 0x65, 0xC0),
        "th2"      => Color.FromRgb(0x2E, 0x7D, 0x32),
        "xvi"      => Color.FromRgb(0x6A, 0x1B, 0x9A),
        "image"    => Color.FromRgb(0x00, 0x83, 0x8F),
        "survey"   => Color.FromRgb(0x19, 0x76, 0xD2),
        "map"      => Color.FromRgb(0xEF, 0x6C, 0x00),
        "scrap"    => Color.FromRgb(0x2E, 0x7D, 0x32),
        "missing"  => Color.FromRgb(0xC6, 0x28, 0x28),
        "folder"   => Color.FromRgb(0xF9, 0xA8, 0x25),
        "file"     => Color.FromRgb(0x75, 0x75, 0x75),
        _          => Color.FromRgb(0x75, 0x75, 0x75),
    });
}

public partial class WorkspaceExplorerViewModel : ViewModelBase
{
    private readonly IDocumentService _documents;
    private readonly IWorkspaceSession? _session;
    private readonly IFileIconProvider? _iconProvider;

    [ObservableProperty] private ObservableCollection<WorkspaceTreeNode> _roots = new();
    [ObservableProperty] private string _rootPath = string.Empty;
    [ObservableProperty] private WorkspaceTreeNode? _selected;

    /// <summary>Show the object model (surveys/maps/scraps) under each file (#16, default on).</summary>
    [ObservableProperty] private bool _showObjectModel = true;
    partial void OnShowObjectModelChanged(bool value) { Refresh(); SaveSettings(); }

    /// <summary>Reveal/highlight the workspace item when hovering a hyperlink in the editor (#8).</summary>
    [ObservableProperty] private bool _revealOnHover;
    partial void OnRevealOnHoverChanged(bool value) => SaveSettings();
    /// <summary>Reveal/highlight the active file in the workspace when switching tabs (#9).</summary>
    [ObservableProperty] private bool _revealOnTabSwitch;
    partial void OnRevealOnTabSwitchChanged(bool value) => SaveSettings();

    // ----- workspace re-org (#3/#5/#8) ---------------------------------------

    /// <summary>Relational (object graph) vs. plain file-explorer presentation (#5).</summary>
    [ObservableProperty] private WorkspaceViewMode _viewMode;
    partial void OnViewModeChanged(WorkspaceViewMode value)
    {
        OnPropertyChanged(nameof(IsRelationalView));
        OnPropertyChanged(nameof(IsFileExplorerView));
        Invalidate(); Refresh(); SaveSettings();
    }
    public bool IsRelationalView => ViewMode == WorkspaceViewMode.Relational;
    public bool IsFileExplorerView => ViewMode == WorkspaceViewMode.FileExplorer;

    /// <summary>Show file nodes in the relational view; off ⇒ logical objects only (#5a).</summary>
    [ObservableProperty] private bool _showFilesInModel = true;
    partial void OnShowFilesInModelChanged(bool value) { Invalidate(); Refresh(); SaveSettings(); }

    // ----- file-explorer sort (#15) ------------------------------------------

    /// <summary>File-explorer sort key (Windows-Explorer-style), applied within each folder (#15).</summary>
    [ObservableProperty] private WorkspaceSortMode _sortMode = WorkspaceSortMode.Name;
    partial void OnSortModeChanged(WorkspaceSortMode value)
    {
        OnPropertyChanged(nameof(SortModeLabel));
        if (IsFileExplorerView) { Invalidate(); Refresh(); }
        SaveSettings();
    }

    /// <summary>Sort direction; true = ascending (#15).</summary>
    [ObservableProperty] private bool _sortAscending = true;
    partial void OnSortAscendingChanged(bool value)
    {
        if (IsFileExplorerView) { Invalidate(); Refresh(); }
        SaveSettings();
    }

    /// <summary>Short label for the active sort key, shown on the sort dropdown button (#15).</summary>
    public string SortModeLabel => SortMode switch
    {
        WorkspaceSortMode.Name => "Name",
        WorkspaceSortMode.Modified => "Date modified",
        WorkspaceSortMode.Size => "Size",
        WorkspaceSortMode.Type => "Type",
        WorkspaceSortMode.Created => "Date created",
        _ => "Name",
    };

    [RelayCommand] private void SortByName() => SortMode = WorkspaceSortMode.Name;
    [RelayCommand] private void SortByModified() => SortMode = WorkspaceSortMode.Modified;
    [RelayCommand] private void SortBySize() => SortMode = WorkspaceSortMode.Size;
    [RelayCommand] private void SortByType() => SortMode = WorkspaceSortMode.Type;
    [RelayCommand] private void SortByCreated() => SortMode = WorkspaceSortMode.Created;
    [RelayCommand] private void SortAscendingCmd() => SortAscending = true;
    [RelayCommand] private void SortDescendingCmd() => SortAscending = false;

    /// <summary>The detected/known thconfig files, shown in the active-config dropdown (#3).</summary>
    [ObservableProperty] private ObservableCollection<ThconfigCandidate> _thconfigCandidates = new();
    /// <summary>The active thconfig (drives the whole object graph, #2).</summary>
    [ObservableProperty] private ThconfigCandidate? _selectedThconfig;
    partial void OnSelectedThconfigChanged(ThconfigCandidate? value)
    {
        // Ignore selection writes we make ourselves while mirroring the session (_syncingThconfig),
        // so this only fires for a real user pick. Route through the one activation entry point.
        if (_syncingThconfig || value is null) return;
        _ = _documents.ActivateThconfigAsync(value.FullPath);
    }

    /// <summary>Formatted path of the active thconfig, shown in the panel header (#8).</summary>
    [ObservableProperty] private string _activeThconfigDisplay = string.Empty;

    /// <summary>Dismissable banner: a tracked file was modified outside the editor (#5b).</summary>
    [ObservableProperty] private string? _workspaceBanner;
    [RelayCommand] private void DismissWorkspaceBanner() => WorkspaceBanner = null;

    private bool _syncingThconfig;

    /// <summary>Raised to open a file node.</summary>
    public event EventHandler<WorkspaceTreeNode>? OpenRequested;
    /// <summary>Raised to navigate to an object node's declaration.</summary>
    public event EventHandler<SourceSpan>? NavigateRequested;

    // Only rebuild the tree when the workspace (or a toggle) actually changes, so
    // navigating/reparsing doesn't reset expansion state (#5).
    private WorkspaceSemanticModel? _builtWorkspace;
    private bool _builtShowObjectModel;
    private bool _builtShowFilesInModel;
    private WorkspaceViewMode _builtViewMode;
    private string? _lastRevealedActive;

    private readonly IAppSettingsService? _settings;
    private bool _loadingSettings;

    public WorkspaceExplorerViewModel() : this(new NullDocumentService()) { }

    public WorkspaceExplorerViewModel(IDocumentService documents, IAppSettingsService? settings = null,
        IWorkspaceSession? session = null, IFileIconProvider? iconProvider = null)
    {
        _documents = documents;
        _settings = settings;
        _session = session;
        _iconProvider = iconProvider;

        if (settings is not null)
        {
            _loadingSettings = true;
            var s = settings.Current;
            ShowObjectModel = s.WorkspaceShowObjectModel;
            RevealOnHover = s.WorkspaceRevealOnHover;
            RevealOnTabSwitch = s.WorkspaceRevealOnTabSwitch;
            ViewMode = s.WorkspaceViewMode;
            ShowFilesInModel = s.WorkspaceShowFilesInModel;
            SortMode = s.WorkspaceSortMode;
            SortAscending = s.WorkspaceSortAscending;
            _loadingSettings = false;
        }

        // Marshal to the UI thread: DocumentChanged can be raised on a background thread (it fires
        // through DocumentService's session-changed handler, which runs after SetActiveThconfigAsync's
        // off-thread continuation). Refresh() rebuilds the UI-bound tree, so running it off-thread would
        // throw and abort the rest of the change propagation — leaving the dropdown/selection stale.
        _documents.DocumentChanged += (_, _) => OnUi(() => { Refresh(); MaybeRevealActive(); });
        _documents.RevealInWorkspaceRequested += (_, target) =>
            { if (RevealOnHover) OnUi(() => HighlightTarget(target)); };
        // Editor "reveal in workspace" button (#1): force file view and select the node.
        _documents.SelectFileInWorkspaceRequested += (_, path) => OnUi(() => SelectFile(path));

        if (_session is not null)
        {
            _session.CandidatesChanged += (_, _) => OnUi(SyncCandidates);
            _session.RootChanged += (_, _) => OnUi(() => { SyncCandidates(); Invalidate(); Refresh(); });
            _session.Changed += (_, _) => OnUi(() => { SyncCandidates(); Refresh(); RevealActiveThconfigIfChanged(); });
            _session.FileSystemChanged += (_, _) => OnUi(() => { if (IsFileExplorerView) { Invalidate(); Refresh(); } });
            _session.ExternalFileChanged += (_, e) => OnUi(() =>
                WorkspaceBanner =
                    $"{System.IO.Path.GetFileName(e.Path)} modified outside the editor ({e.TimeUtc.ToLocalTime():HH:mm}).");
            SyncCandidates();
        }
    }

    private static void OnUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    /// <summary>Mirrors the session's candidate list + active selection into UI-bound state.</summary>
    private void SyncCandidates()
    {
        if (_session is null) return;
        _syncingThconfig = true;
        try
        {
            // Only replace the collection when the candidate set actually changed. Assigning a new
            // ObservableCollection makes the bound ComboBox drop its selection (it resets SelectedItem
            // to null asynchronously and writes that back), so an externally-set active thconfig — from
            // the file button, context menu or command palette — wouldn't show up in the dropdown. When
            // the list is unchanged (the common "same candidates, different active" case) we keep the
            // collection and just move the selection, which the ComboBox honours reliably.
            var incoming = _session.Candidates;
            if (!SameCandidatePaths(ThconfigCandidates, incoming))
                ThconfigCandidates = new ObservableCollection<ThconfigCandidate>(incoming);

            var active = _session.ActiveThconfig;
            SelectedThconfig = active is null
                ? null
                : ThconfigCandidates.FirstOrDefault(c =>
                      string.Equals(c.FullPath, active.FullPath, StringComparison.OrdinalIgnoreCase));
            ActiveThconfigDisplay = active?.DisplayPath ?? string.Empty;
        }
        finally { _syncingThconfig = false; }
    }

    private static bool SameCandidatePaths(IReadOnlyList<ThconfigCandidate> a, IReadOnlyList<ThconfigCandidate> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i].FullPath, b[i].FullPath, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // When the active thconfig changes (via any path), select its node in the tree so the explorer
    // visibly tracks "the current project configuration". Guarded so a mere graph rebuild (e.g. an
    // edit) doesn't keep re-selecting/expanding while the user browses elsewhere.
    private string? _lastRevealedActiveThconfig;
    private void RevealActiveThconfigIfChanged()
    {
        var active = _session?.ActiveThconfig?.FullPath;
        if (string.IsNullOrEmpty(active)) return;
        if (string.Equals(active, _lastRevealedActiveThconfig, StringComparison.OrdinalIgnoreCase)) return;
        _lastRevealedActiveThconfig = active;
        RevealFile(active);
    }

    /// <summary>Forces the next <see cref="Refresh"/> to rebuild (after a toggle/mode change).</summary>
    private void Invalidate() => _builtWorkspace = null;

    private void SaveSettings()
    {
        if (_loadingSettings || _settings is null) return;
        _settings.Save(_settings.Current with
        {
            WorkspaceShowObjectModel = ShowObjectModel,
            WorkspaceRevealOnHover = RevealOnHover,
            WorkspaceRevealOnTabSwitch = RevealOnTabSwitch,
            WorkspaceViewMode = ViewMode,
            WorkspaceShowFilesInModel = ShowFilesInModel,
            WorkspaceSortMode = SortMode,
            WorkspaceSortAscending = SortAscending,
        });
    }

    [RelayCommand] private void SwitchToRelational() => ViewMode = WorkspaceViewMode.Relational;
    [RelayCommand] private void SwitchToFileExplorer() => ViewMode = WorkspaceViewMode.FileExplorer;

    /// <summary>Opens the currently-selected thconfig in the editor, revealing it in file view (#6).</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task OpenSelectedThconfig()
    {
        if (SelectedThconfig?.FullPath is not { } path || string.IsNullOrEmpty(path)) return;
        await _documents.OpenFileAsync(path).ConfigureAwait(true);
        if (IsFileExplorerView) RevealFile(path);
    }

    public void Refresh()
    {
        if (ViewMode == WorkspaceViewMode.FileExplorer) { RefreshFileExplorer(); return; }

        var ws = _documents.Workspace;
        if (ws is null)
        {
            Roots = new ObservableCollection<WorkspaceTreeNode>();
            RootPath = _session?.RootPath ?? string.Empty;
            _builtWorkspace = null;
            return;
        }

        // Skip the rebuild when nothing structural changed (preserves expansion, #5).
        if (ReferenceEquals(ws, _builtWorkspace) && _builtShowObjectModel == ShowObjectModel
            && _builtShowFilesInModel == ShowFilesInModel && _builtViewMode == ViewMode)
            return;
        _builtWorkspace = ws;
        _builtShowObjectModel = ShowObjectModel;
        _builtShowFilesInModel = ShowFilesInModel;
        _builtViewMode = ViewMode;
        HighlightedNode = null; // tree rebuilt — drop any stale hover highlight

        // Logical-only view (#5a): just the object hierarchy, root survey at the top.
        if (!ShowFilesInModel) { Roots = BuildLogicalTree(ws); RootPath = _session?.RootPath ?? string.Empty; return; }

        // Build adjacency + roots from the file-inclusion graph.
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var allNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasIncoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in ws.FileGraphEdges)
        {
            allNodes.Add(from);
            allNodes.Add(to);
            if (!children.TryGetValue(from, out var list)) children[from] = list = new();
            if (!list.Any(x => string.Equals(x, to, StringComparison.OrdinalIgnoreCase))) list.Add(to);
            hasIncoming.Add(to);
        }
        foreach (var p in ws.PerFile.Keys) allNodes.Add(p);

        var roots = allNodes.Where(n => !hasIncoming.Contains(n))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        if (roots.Count == 0 && _documents.CurrentPath is { } cp) roots.Add(cp);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new ObservableCollection<WorkspaceTreeNode>();

        WorkspaceTreeNode BuildFile(string path, int depth)
        {
            var node = new WorkspaceTreeNode
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                Kind = KindFor(path),
                IsExpanded = depth < 2,
            };

            // Single switch (#5): when file nodes are shown they always carry their object
            // model nested underneath; the logical-only tree is the "off" state instead.
            AddObjectModel(node, ws, path);

            if (children.TryGetValue(path, out var kids))
                foreach (var k in kids.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    if (visited.Add(k))
                        node.Children.Add(BuildFile(k, depth + 1));
            return node;
        }

        foreach (var r in roots)
            if (visited.Add(r))
                result.Add(BuildFile(r, 0));
        foreach (var p in ws.PerFile.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            if (visited.Add(p))
                result.Add(BuildFile(p, 0));

        RootPath = roots.Count > 0 ? roots[0] : (_documents.CurrentPath ?? string.Empty);
        Roots = result;
    }

    /// <summary>Adds the surveys/maps (.th) or scraps (.th2) declared in <paramref name="path"/>.</summary>
    private static void AddObjectModel(WorkspaceTreeNode fileNode, WorkspaceSemanticModel ws, string path)
    {
        if (ws.PerFile.TryGetValue(path, out var model))
        {
            // Surveys, nested by parent/child relationship.
            var nodes = model.Surveys.Values.ToDictionary(
                s => s.Name,
                s => new WorkspaceTreeNode
                {
                    Name = s.Name.Last,
                    Detail = s.Title,
                    Kind = "survey",
                    Target = s.DeclarationSpan,
                });
            foreach (var sv in model.Surveys.Values)
            {
                var node = nodes[sv.Name];
                if (sv.Parent is { } parent && nodes.TryGetValue(parent, out var parentNode))
                    parentNode.Children.Add(node);
                else
                    fileNode.Children.Add(node);
            }

            foreach (var m in model.Maps.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
                fileNode.Children.Add(new WorkspaceTreeNode
                {
                    Name = m.Id,
                    Detail = m.Title,
                    Kind = "map",
                    Target = m.DeclarationSpan,
                });
        }

        // Scraps declared in this .th2 file.
        foreach (var sc in ws.ScrapsById.Values
                     .Where(s => string.Equals(s.DeclarationSpan.FilePath, path, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(s => s.Id, StringComparer.Ordinal))
            fileNode.Children.Add(new WorkspaceTreeNode
            {
                Name = sc.Id,
                Kind = "scrap",
                Target = sc.DeclarationSpan,
            });
    }

    // ----- logical-only view (#5a) -------------------------------------------

    /// <summary>Builds the object hierarchy with no file nodes: root survey(s) → sub-surveys → maps/scraps.</summary>
    private static ObservableCollection<WorkspaceTreeNode> BuildLogicalTree(WorkspaceSemanticModel ws)
    {
        var nodes = new Dictionary<Therion.Semantics.QualifiedName, WorkspaceTreeNode>();
        var surveys = new Dictionary<Therion.Semantics.QualifiedName, Therion.Semantics.SurveySymbol>();
        foreach (var model in ws.PerFile.Values)
            foreach (var sv in model.Surveys.Values)
            {
                surveys[sv.Name] = sv;
                nodes[sv.Name] = new WorkspaceTreeNode
                {
                    Name = sv.Name.Last, Detail = sv.Title, Kind = "survey",
                    Target = sv.DeclarationSpan, IsExpanded = true,
                };
            }

        var rootSurveys = new List<WorkspaceTreeNode>();
        foreach (var sv in surveys.Values)
        {
            var node = nodes[sv.Name];
            if (sv.Parent is { } parent && nodes.TryGetValue(parent, out var parentNode))
                parentNode.Children.Add(node);
            else
                rootSurveys.Add(node);
        }

        var extras = new List<WorkspaceTreeNode>();
        foreach (var model in ws.PerFile.Values)
            foreach (var m in model.Maps.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
                extras.Add(new WorkspaceTreeNode { Name = m.Id, Detail = m.Title, Kind = "map", Target = m.DeclarationSpan });
        foreach (var sc in ws.ScrapsById.Values.OrderBy(s => s.Id, StringComparer.Ordinal))
            extras.Add(new WorkspaceTreeNode { Name = sc.Id, Kind = "scrap", Target = sc.DeclarationSpan });

        var roots = new ObservableCollection<WorkspaceTreeNode>();
        if (rootSurveys.Count == 1)
        {
            foreach (var e in extras) rootSurveys[0].Children.Add(e);
            roots.Add(rootSurveys[0]);
        }
        else
        {
            foreach (var r in rootSurveys.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) roots.Add(r);
            foreach (var e in extras) roots.Add(e);
        }
        return roots;
    }

    // ----- file-explorer view (#5b) ------------------------------------------

    private void RefreshFileExplorer()
    {
        var root = _session?.RootPath ?? RootPath;
        RootPath = root ?? string.Empty;
        _builtWorkspace = null; // force a relational rebuild when we switch back

        // Preserve which folders are expanded + the current selection across the rebuild.
        // Opening a file fires DocumentChanged → Refresh, so without this the whole subtree
        // would collapse and the selected file would be lost every time a file is opened (#1).
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectExpandedFolders(Roots, expanded);
        var selectedPath = Selected?.FullPath;

        HighlightedNode = null; // tree rebuilt — drop any stale hover highlight

        var result = new ObservableCollection<WorkspaceTreeNode>();
        // Show the workspace root directory itself as the top node (#1), expanded.
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            result.Add(BuildFolderNode(root, expandEager: true));
        Roots = result;

        if (expanded.Count > 0) RestoreExpansion(result, expanded);
        if (selectedPath is not null && ExpandToFile(selectedPath) is { } sel) Selected = sel;
    }

    /// <summary>Records the FullPath of every currently-expanded folder node (for #1's rebuild restore).</summary>
    private static void CollectExpandedFolders(IEnumerable<WorkspaceTreeNode> nodes, HashSet<string> into)
    {
        foreach (var n in nodes)
        {
            if (n.Kind == "folder" && n.IsExpanded && n.FullPath is { } p) into.Add(p);
            CollectExpandedFolders(n.Children, into);
        }
    }

    /// <summary>Re-expands the folders that were open before a rebuild, lazily loading each level (#1).</summary>
    private static void RestoreExpansion(IEnumerable<WorkspaceTreeNode> nodes, HashSet<string> expanded)
    {
        foreach (var n in nodes)
            if (n.Kind == "folder" && n.FullPath is { } p && expanded.Contains(p))
            {
                n.IsExpanded = true; // triggers the lazy child load
                RestoreExpansion(n.Children, expanded);
            }
    }

    private WorkspaceTreeNode BuildFolderNode(string path, bool expandEager)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path; // drive roots like "C:\"

        var folder = new WorkspaceTreeNode
        {
            Name = name,
            FullPath = path,
            Kind = "folder",
            ShellIcon = _iconProvider?.GetIcon(path, isDirectory: true),
            LastModifiedText = LastModified(path),
            LoadChildren = node =>
            {
                node.Children.Clear();
                foreach (var c in BuildDirChildren(path)) node.Children.Add(c);
            },
        };
        folder.Children.Add(new WorkspaceTreeNode { Kind = "file" }); // placeholder so the expander shows
        if (expandEager) folder.IsExpanded = true; // triggers lazy load now
        return folder;
    }

    private List<WorkspaceTreeNode> BuildDirChildren(string dir)
    {
        var list = new List<WorkspaceTreeNode>();
        string[] dirs, files;
        try { dirs = Directory.GetDirectories(dir); } catch { dirs = Array.Empty<string>(); }
        try { files = Directory.GetFiles(dir); } catch { files = Array.Empty<string>(); }

        // Folders always sort above files (Windows Explorer behaviour), each group ordered by
        // the active sort key + direction (#15).
        foreach (var d in SortPaths(dirs, isDirectory: true))
            list.Add(BuildFolderNode(d, expandEager: false));

        foreach (var f in SortPaths(files, isDirectory: false))
            list.Add(new WorkspaceTreeNode
            {
                Name = Path.GetFileName(f),
                FullPath = f,
                Kind = FileKind(f),
                ShellIcon = _iconProvider?.GetIcon(f, isDirectory: false),
                LastModifiedText = LastModified(f),
                InActiveGraph = _session?.Covers(f) ?? false, // blue name when in the graph (#2)
            });
        return list;
    }

    /// <summary>Orders a set of file/folder paths by the active <see cref="SortMode"/> + direction (#15).</summary>
    private IEnumerable<string> SortPaths(string[] paths, bool isDirectory)
    {
        // Name and Type always break ties (and Type's secondary key) by name, so the order is
        // deterministic. Size/dates fall back to name when equal.
        IOrderedEnumerable<string> ordered = SortMode switch
        {
            WorkspaceSortMode.Modified => SortAscending
                ? paths.OrderBy(SafeLastWrite)
                : paths.OrderByDescending(SafeLastWrite),
            WorkspaceSortMode.Created => SortAscending
                ? paths.OrderBy(SafeCreated)
                : paths.OrderByDescending(SafeCreated),
            WorkspaceSortMode.Size => SortAscending
                ? paths.OrderBy(p => SafeSize(p, isDirectory))
                : paths.OrderByDescending(p => SafeSize(p, isDirectory)),
            WorkspaceSortMode.Type => SortAscending
                ? paths.OrderBy(p => Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)
                : paths.OrderByDescending(p => Path.GetExtension(p), StringComparer.OrdinalIgnoreCase),
            _ => SortAscending
                ? paths.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                : paths.OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase),
        };
        return ordered.ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
    }

    private static DateTime SafeLastWrite(string p) { try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; } }
    private static DateTime SafeCreated(string p) { try { return File.GetCreationTimeUtc(p); } catch { return DateTime.MinValue; } }
    private static long SafeSize(string p, bool isDirectory)
    {
        if (isDirectory) return -1; // folders have no intrinsic size; keep them grouped together
        try { return new FileInfo(p).Length; } catch { return 0; }
    }

    /// <summary>Cached last-write time for the hover tooltip; computed once so hover never hits disk (#2).</summary>
    private static string? LastModified(string path)
    {
        try
        {
            var t = Directory.Exists(path) ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);
            return "Modified: " + t.ToString("yyyy-MM-dd HH:mm");
        }
        catch { return null; }
    }

    private static string FileKind(string path)
    {
        var k = KindFor(path);
        return k == "other" ? "file" : k;
    }

    [RelayCommand]
    private void Activate(WorkspaceTreeNode? node)
    {
        if (node is null) return;
        if (node.Kind == "folder") { node.IsExpanded = !node.IsExpanded; return; }
        if (node.FullPath is not null) OpenRequested?.Invoke(this, node);
        else if (node.Target is { } span && !span.IsEmpty) NavigateRequested?.Invoke(this, span);
    }

    // ----- reveal / highlight (#8 hover, #9 tab switch) -------------------

    /// <summary>The node currently highlighted (yellow) by a reveal-on-hover; not the selection.</summary>
    [ObservableProperty] private WorkspaceTreeNode? _highlightedNode;
    partial void OnHighlightedNodeChanged(WorkspaceTreeNode? oldValue, WorkspaceTreeNode? newValue)
    {
        if (oldValue is not null) oldValue.IsLinkHighlighted = false;
        if (newValue is not null) newValue.IsLinkHighlighted = true;
    }

    private void MaybeRevealActive()
    {
        if (!RevealOnTabSwitch) return;
        var path = _documents.CurrentPath;
        if (string.Equals(path, _lastRevealedActive, StringComparison.OrdinalIgnoreCase)) return;
        _lastRevealedActive = path;
        RevealFile(path);
    }

    /// <summary>Selects and scrolls to the tree node for a target object (or its file), expanding ancestors.</summary>
    public void Reveal(SourceSpan target)
    {
        if (FindAndExpand(target) is { } node) Selected = node;
    }

    /// <summary>Highlights (yellow, not selected) the node for a hovered link, in either view mode (#4).</summary>
    public void HighlightTarget(SourceSpan target)
    {
        var node = IsFileExplorerView
            ? (string.IsNullOrEmpty(target.FilePath) ? null : ExpandToFile(target.FilePath))
            : FindAndExpand(target);
        HighlightedNode = node; // clears the previous highlight, sets the new one
    }

    /// <summary>Switches to file-explorer view (if needed) and selects/expands to the file (#1).</summary>
    public void SelectFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        if (!IsFileExplorerView)
        {
            ViewMode = WorkspaceViewMode.FileExplorer; // synchronously rebuilds the tree
        }
        if (ExpandToFile(filePath) is { } node) Selected = node;
    }

    /// <summary>
    /// Switches to file-explorer view and selects/expands to the folder (or file) at
    /// <paramref name="path"/> — used by the status-bar path breadcrumb (#1). No-op when the path
    /// is outside the current workspace root.
    /// </summary>
    public void RevealPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!IsFileExplorerView) ViewMode = WorkspaceViewMode.FileExplorer; // rebuilds the tree
        if (ExpandToFile(path) is { } node) Selected = node;
    }

    /// <summary>Selects the file's node, expanding to it; works in both view modes (#4/#9).</summary>
    public void RevealFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        if (IsFileExplorerView)
        {
            if (ExpandToFile(filePath) is { } node) Selected = node;
            return;
        }
        var path = new List<WorkspaceTreeNode>();
        if (TryFindPath(Roots, n => string.Equals(n.FullPath, filePath, StringComparison.OrdinalIgnoreCase), path))
            ApplyReveal(path);
    }

    /// <summary>Finds a relational node (by object span, else by file path) and expands its ancestors.</summary>
    private WorkspaceTreeNode? FindAndExpand(SourceSpan target)
    {
        if (target.IsEmpty && string.IsNullOrEmpty(target.FilePath)) return null;
        var path = new List<WorkspaceTreeNode>();
        if (TryFindPath(Roots, n => n.Target is { } t && SameSpan(t, target), path) ||
            TryFindPath(Roots, n => string.Equals(n.FullPath, target.FilePath, StringComparison.OrdinalIgnoreCase), path))
        {
            for (int i = 0; i < path.Count - 1; i++) path[i].IsExpanded = true;
            return path[^1];
        }
        return null;
    }

    /// <summary>
    /// Descends the file-explorer tree from the root node to <paramref name="filePath"/>,
    /// expanding (and lazily loading) each folder on the way, and returns the matched node.
    /// </summary>
    private WorkspaceTreeNode? ExpandToFile(string filePath)
    {
        var root = _session?.RootPath;
        if (string.IsNullOrEmpty(root) || Roots.Count == 0) return null;

        string rel;
        try { rel = Path.GetRelativePath(root, Path.GetFullPath(filePath)); }
        catch { return null; }
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)) return null;

        var node = Roots[0];          // the root directory node
        node.IsExpanded = true;       // ensure children are loaded
        if (rel is "." or "") return node;

        foreach (var seg in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (seg.Length == 0) continue;
            node.IsExpanded = true;   // load this level before searching it
            var child = node.Children.FirstOrDefault(
                c => string.Equals(c.Name, seg, StringComparison.OrdinalIgnoreCase));
            if (child is null) return null;
            node = child;
        }
        return node;
    }

    private void ApplyReveal(List<WorkspaceTreeNode> path)
    {
        for (int i = 0; i < path.Count - 1; i++) path[i].IsExpanded = true; // expand ancestors
        Selected = path[^1];
    }

    private static bool SameSpan(SourceSpan a, SourceSpan b) =>
        string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase) && a.StartOffset == b.StartOffset;

    private static bool TryFindPath(
        IEnumerable<WorkspaceTreeNode> nodes, Func<WorkspaceTreeNode, bool> match, List<WorkspaceTreeNode> path)
    {
        foreach (var n in nodes)
        {
            path.Add(n);
            if (match(n) || TryFindPath(n.Children, match, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    private static string KindFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".th"       => "th",
            ".th2"      => "th2",
            ".thconfig" => "thconfig",
            ".thc"      => "thconfig",
            ".xvi"      => "xvi",
            ""          => "thconfig", // Therion convention: bare "thconfig" file
            _           => "other",
        };
    }
}
