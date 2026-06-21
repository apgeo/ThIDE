// Implementation Plan �7.3 � Workspace Explorer ViewModel.
// Renders the project's file-inclusion graph as a nested tree: the entry point
// (.thconfig) → its `source` files → their `input` files (recursively) → .th2
// sketches → .xvi backgrounds → images. Depth drives indentation in the view.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Semantics;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public sealed record WorkspaceNode(
    string DisplayName,
    string FullPath,
    string Kind,             // "thconfig" | "th" | "th2" | "xvi" | "image" | "missing"
    int Depth,
    bool Exists)
{
    /// <summary>Left indentation reflecting the node's depth in the inclusion tree.</summary>
    public Thickness Indent => new(Depth * 14, 0, 0, 0);
}

public partial class WorkspaceExplorerViewModel : ViewModelBase
{
    private readonly IDocumentService _documents;

    [ObservableProperty] private IReadOnlyList<WorkspaceNode> _nodes = Array.Empty<WorkspaceNode>();
    [ObservableProperty] private string _rootPath = string.Empty;
    [ObservableProperty] private WorkspaceNode? _selected;

    public event EventHandler<WorkspaceNode>? OpenRequested;

    public WorkspaceExplorerViewModel() : this(new NullDocumentService()) { }

    // Public constructor for DI.
    public WorkspaceExplorerViewModel(IDocumentService documents)
    {
        _documents = documents;
        _documents.DocumentChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var ws = _documents.Workspace;
        if (ws is null)
        {
            Nodes = Array.Empty<WorkspaceNode>();
            RootPath = string.Empty;
            return;
        }

        // Adjacency + roots from the file-inclusion graph.
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

        // image-per-xvi lookup for the leaf rows.
        var imageByXvi = new Dictionary<string, (string Path, bool Exists)>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in ws.Xvi.ByPath.Values)
            if (!string.IsNullOrEmpty(x.ResolvedImagePath))
                imageByXvi[x.ResolvedXviPath] = (x.ResolvedImagePath, x.ImageExists);

        // Roots = nodes with no incoming edge (the entry .thconfig / a lone .th).
        var roots = allNodes.Where(n => !hasIncoming.Contains(n))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .ToList();
        if (roots.Count == 0 && _documents.CurrentPath is { } cp) roots.Add(cp);

        var nodes = new List<WorkspaceNode>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string path, int depth)
        {
            if (!visited.Add(path)) return; // already placed (DAG diamond / cycle guard)
            nodes.Add(MakeNode(path, depth));

            if (imageByXvi.TryGetValue(path, out var img))
                nodes.Add(new WorkspaceNode(Path.GetFileName(img.Path), img.Path,
                    img.Exists ? "image" : "missing", depth + 1, img.Exists));

            if (children.TryGetValue(path, out var kids))
                foreach (var k in kids.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    Visit(k, depth + 1);
        }

        foreach (var r in roots) Visit(r, 0);
        // Any file reached by binding but disconnected from the graph (rare).
        foreach (var p in ws.PerFile.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            if (!visited.Contains(p)) Visit(p, 0);

        RootPath = roots.Count > 0 ? roots[0] : (_documents.CurrentPath ?? string.Empty);
        Nodes = nodes;
    }

    private static WorkspaceNode MakeNode(string path, int depth) =>
        new(Path.GetFileName(path), path, KindFor(path), depth, File.Exists(path));

    [RelayCommand]
    private void Open(WorkspaceNode? node)
    {
        if (node is null) return;
        OpenRequested?.Invoke(this, node);
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
