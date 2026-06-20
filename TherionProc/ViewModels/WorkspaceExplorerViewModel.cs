// Implementation Plan §7.3 — Workspace Explorer ViewModel.
// Renders the FileGraph of the active workspace as a flat tree-like
// projection: entry-point first, then files grouped by extension, then
// .xvi siblings with image-existence badges.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    bool Exists);

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
        if (ws is null || _documents.CurrentPath is null)
        {
            Nodes = Array.Empty<WorkspaceNode>();
            RootPath = string.Empty;
            return;
        }

        RootPath = _documents.CurrentPath;
        var nodes = new List<WorkspaceNode>();

        // Entry-point.
        nodes.Add(new WorkspaceNode(Path.GetFileName(RootPath), RootPath, KindFor(RootPath), 0, File.Exists(RootPath)));

        // All other files reached through PerFile / FileGraph.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RootPath };

        foreach (var path in ws.PerFile.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(path)) continue;
            nodes.Add(new WorkspaceNode(Path.GetFileName(path), path, KindFor(path), 1, File.Exists(path)));
        }

        foreach (var xvi in ws.Xvi.ByPath.Values.OrderBy(x => x.ResolvedXviPath, StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(xvi.ResolvedXviPath)) continue;
            nodes.Add(new WorkspaceNode(Path.GetFileName(xvi.ResolvedXviPath), xvi.ResolvedXviPath, "xvi", 1, File.Exists(xvi.ResolvedXviPath)));
            if (!string.IsNullOrEmpty(xvi.ResolvedImagePath))
            {
                nodes.Add(new WorkspaceNode(
                    Path.GetFileName(xvi.ResolvedImagePath),
                    xvi.ResolvedImagePath,
                    xvi.ImageExists ? "image" : "missing",
                    2,
                    xvi.ImageExists));
            }
        }

        Nodes = nodes;
    }

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
