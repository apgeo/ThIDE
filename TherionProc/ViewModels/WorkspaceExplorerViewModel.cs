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
    /// <summary>Declaration span for object nodes (double-click navigates to it).</summary>
    public SourceSpan? Target { get; init; }
    public string Kind { get; init; } = string.Empty;
    public ObservableCollection<WorkspaceTreeNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;

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
        _          => Color.FromRgb(0x75, 0x75, 0x75),
    });
}

public partial class WorkspaceExplorerViewModel : ViewModelBase
{
    private readonly IDocumentService _documents;

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

    /// <summary>Raised to open a file node.</summary>
    public event EventHandler<WorkspaceTreeNode>? OpenRequested;
    /// <summary>Raised to navigate to an object node's declaration.</summary>
    public event EventHandler<SourceSpan>? NavigateRequested;

    // Only rebuild the tree when the workspace (or the object-model toggle) actually
    // changes, so navigating/reparsing doesn't reset expansion state (#5).
    private WorkspaceSemanticModel? _builtWorkspace;
    private bool _builtShowObjectModel;
    private string? _lastRevealedActive;

    private readonly IAppSettingsService? _settings;
    private bool _loadingSettings;

    public WorkspaceExplorerViewModel() : this(new NullDocumentService()) { }

    public WorkspaceExplorerViewModel(IDocumentService documents, IAppSettingsService? settings = null)
    {
        _documents = documents;
        _settings = settings;

        if (settings is not null)
        {
            _loadingSettings = true;
            var s = settings.Current;
            ShowObjectModel = s.WorkspaceShowObjectModel;
            RevealOnHover = s.WorkspaceRevealOnHover;
            RevealOnTabSwitch = s.WorkspaceRevealOnTabSwitch;
            _loadingSettings = false;
        }

        _documents.DocumentChanged += (_, _) => { Refresh(); MaybeRevealActive(); };
        _documents.RevealInWorkspaceRequested += (_, target) => { if (RevealOnHover) Reveal(target); };
    }

    private void SaveSettings()
    {
        if (_loadingSettings || _settings is null) return;
        _settings.Save(_settings.Current with
        {
            WorkspaceShowObjectModel = ShowObjectModel,
            WorkspaceRevealOnHover = RevealOnHover,
            WorkspaceRevealOnTabSwitch = RevealOnTabSwitch,
        });
    }

    public void Refresh()
    {
        var ws = _documents.Workspace;
        if (ws is null)
        {
            Roots = new ObservableCollection<WorkspaceTreeNode>();
            RootPath = string.Empty;
            _builtWorkspace = null;
            return;
        }

        // Skip the rebuild when nothing structural changed (preserves expansion, #5).
        if (ReferenceEquals(ws, _builtWorkspace) && _builtShowObjectModel == ShowObjectModel)
            return;
        _builtWorkspace = ws;
        _builtShowObjectModel = ShowObjectModel;

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

        var imageByXvi = new Dictionary<string, (string Path, bool Exists)>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in ws.Xvi.ByPath.Values)
            if (!string.IsNullOrEmpty(x.ResolvedImagePath))
                imageByXvi[x.ResolvedXviPath] = (x.ResolvedImagePath, x.ImageExists);

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

            if (ShowObjectModel) AddObjectModel(node, ws, path);

            if (imageByXvi.TryGetValue(path, out var img))
                node.Children.Add(new WorkspaceTreeNode
                {
                    Name = Path.GetFileName(img.Path),
                    FullPath = img.Path,
                    Kind = img.Exists ? "image" : "missing",
                });

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

    [RelayCommand]
    private void Activate(WorkspaceTreeNode? node)
    {
        if (node is null) return;
        if (node.FullPath is not null) OpenRequested?.Invoke(this, node);
        else if (node.Target is { } span && !span.IsEmpty) NavigateRequested?.Invoke(this, span);
    }

    // ----- reveal / highlight (#8 hover, #9 tab switch) -------------------

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
        if (target.IsEmpty && string.IsNullOrEmpty(target.FilePath)) return;
        var path = new List<WorkspaceTreeNode>();
        if (TryFindPath(Roots, n => n.Target is { } t && SameSpan(t, target), path) ||
            TryFindPath(Roots, n => string.Equals(n.FullPath, target.FilePath, StringComparison.OrdinalIgnoreCase), path))
            ApplyReveal(path);
    }

    public void RevealFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        var path = new List<WorkspaceTreeNode>();
        if (TryFindPath(Roots, n => string.Equals(n.FullPath, filePath, StringComparison.OrdinalIgnoreCase), path))
            ApplyReveal(path);
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
