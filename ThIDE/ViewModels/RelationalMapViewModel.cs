// View model for the "Relational Map" window — a draggable node/edge diagram of the
// current project's object-relational tree (surveys / maps / scraps, optionally the host
// files). Data comes from RelationalGraphBuilder; this VM lays the nodes out in left-to-right
// layers and exposes them as observable, draggable items.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Core;
using ThIDE.Services;

namespace ThIDE.ViewModels;

/// <summary>Available arrangements for the relational diagram.</summary>
// Note: 'Nested' (the containment/treemap layout) is disabled — it does not display correctly
// (boxes overlap and often render blank at real project sizes). The enum value is kept only so the
// commented-out NestedLayout implementation below still refers to a valid name; it is no longer
// offered in LayoutOptions.
public enum RelationalLayoutKind { TreeVertical, TreeHorizontal, LayeredColumns, Nested }

/// <summary>A selectable layout option (display name + kind) for the toolbar combo.</summary>
public sealed record RelationalLayoutOption(string Name, RelationalLayoutKind Kind)
{
    public override string ToString() => Name;
}

/// <summary>A draggable diagram node. X/Y are the top-left on the canvas.</summary>
public sealed partial class RelationalNode : ObservableObject
{
    public string Id { get; }
    public string Label { get; }
    public string SubLabel { get; }
    public string KindLabel { get; }
    public RelationalNodeKind Kind { get; }
    public SourceSpan? Declaration { get; }

    public IBrush Fill { get; }
    public IBrush Stroke { get; }

    /// <summary>The node's natural height (with/without a sublabel) before any level sizing.</summary>
    public double BaseHeight { get; }
    /// <summary>Layout depth (0 = root); drives level-based sizing and nested z-order (#2).</summary>
    public int Depth { get; set; }

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width = 168;
    [ObservableProperty] private double _height = 44;
    /// <summary>True in the nested-containment layout (changes label alignment in the template, #2).</summary>
    [ObservableProperty] private bool _nested;

    public bool HasSubLabel => !string.IsNullOrEmpty(SubLabel);

    public RelationalNode(RelationalNodeData data)
    {
        Id = data.Id;
        Label = data.Label;
        SubLabel = data.SubLabel;
        Kind = data.Kind;
        KindLabel = KindToLabel(data.Kind);
        Declaration = data.Declaration;
        BaseHeight = string.IsNullOrEmpty(data.SubLabel) ? 44 : 56;
        _height = BaseHeight;
        _width = 168;
        (Fill, Stroke) = Palette(data.Kind);
    }

    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    private static string KindToLabel(RelationalNodeKind k) => k switch
    {
        RelationalNodeKind.Thconfig => "thconfig",
        RelationalNodeKind.ThFile   => ".th",
        RelationalNodeKind.Th2File  => ".th2",
        RelationalNodeKind.OtherFile => "file",
        RelationalNodeKind.Survey   => "survey",
        RelationalNodeKind.Map      => "map",
        RelationalNodeKind.Scrap    => "scrap",
        _ => "",
    };

    private static (IBrush Fill, IBrush Stroke) Palette(RelationalNodeKind k)
    {
        Color c = k switch
        {
            RelationalNodeKind.Thconfig => Color.FromRgb(0x2E, 0x7D, 0x32),
            RelationalNodeKind.ThFile   => Color.FromRgb(0xE6, 0x51, 0x00),
            RelationalNodeKind.Th2File  => Color.FromRgb(0x6A, 0x1B, 0x9A),
            RelationalNodeKind.OtherFile => Color.FromRgb(0x60, 0x60, 0x60),
            RelationalNodeKind.Survey   => Color.FromRgb(0x15, 0x65, 0xC0),
            RelationalNodeKind.Map      => Color.FromRgb(0x00, 0x89, 0x7B),
            RelationalNodeKind.Scrap    => Color.FromRgb(0xAD, 0x14, 0x57),
            _ => Color.FromRgb(0x42, 0x42, 0x42),
        };
        var fill = new ImmutableSolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B));
        var stroke = new ImmutableSolidColorBrush(c);
        return (fill, stroke);
    }
}

/// <summary>A diagram edge referencing live node instances so it tracks node movement.</summary>
public sealed class RelationalEdge
{
    public RelationalNode From { get; }
    public RelationalNode To { get; }
    public string LinkLabel { get; }
    public SourceSpan? LinkSpan { get; }

    public RelationalEdge(RelationalNode from, RelationalNode to, string label, SourceSpan? span)
    {
        From = from; To = to; LinkLabel = label; LinkSpan = span;
    }
}

public partial class RelationalMapViewModel : ViewModelBase
{
    private readonly Services.IDocumentService? _docs;
    private readonly Services.IWorkspaceSession? _session;

    public ObservableCollection<RelationalNode> Nodes { get; } = new();
    public IReadOnlyList<RelationalEdge> Edges { get; private set; } = Array.Empty<RelationalEdge>();

    [ObservableProperty] private bool _includeFiles = true;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private double _canvasWidth = 800;
    [ObservableProperty] private double _canvasHeight = 600;

    /// <summary>Diagram zoom factor (#2). Bound to a LayoutTransform so scrollbars track the scaled size.</summary>
    [ObservableProperty] private double _zoom = 1.0;

    /// <summary>Scale node sizes by depth — bigger near the root, smaller leaves — so a big project fits (#2).</summary>
    [ObservableProperty] private bool _sizeByLevel;

    /// <summary>Edges are hidden in the nested-containment layout (containment already shows hierarchy, #2).</summary>
    [ObservableProperty] private bool _showEdges = true;

    public IReadOnlyList<RelationalLayoutOption> LayoutOptions { get; } = new[]
    {
        new RelationalLayoutOption("Tree ▽ (branches by area)", RelationalLayoutKind.TreeVertical),
        new RelationalLayoutOption("Tree ▷ (left-to-right)",     RelationalLayoutKind.TreeHorizontal),
        new RelationalLayoutOption("Layered columns",            RelationalLayoutKind.LayeredColumns),
        // "Nested (containment)" removed — the treemap layout does not display properly (see NestedLayout).
    };

    [ObservableProperty] private RelationalLayoutOption _selectedLayout;

    partial void OnSelectedLayoutChanged(RelationalLayoutOption value) => Relayout();
    partial void OnSizeByLevelChanged(bool value) => Relayout();

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(4.0, Math.Round(Zoom * 1.2, 3));
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(0.15, Math.Round(Zoom / 1.2, 3));
    [RelayCommand] private void ZoomReset() => Zoom = 1.0;

    /// <summary>Raised after a rebuild or relayout so the view re-renders the edge layer.</summary>
    public event EventHandler? GraphChanged;

    public RelationalMapViewModel() // design-time
    {
        _selectedLayout = LayoutOptions[0];
    }

    public RelationalMapViewModel(Services.IDocumentService docs, Services.IWorkspaceSession? session)
    {
        _selectedLayout = LayoutOptions[0];
        _docs = docs;
        _session = session;
        if (_docs is not null) _docs.DocumentChanged += (_, _) => OnUi(Rebuild);
        if (_session is not null) _session.Changed += (_, _) => OnUi(Rebuild);
        Rebuild();
    }

    partial void OnIncludeFilesChanged(bool value) => Rebuild();

    [RelayCommand]
    private void Refresh() => Rebuild();

    public void Rebuild()
    {
        var model = _docs?.Workspace;
        var active = _session?.ActiveThconfig?.FullPath;
        var graph = RelationalGraphBuilder.Build(model, active, IncludeFiles);

        var byId = new Dictionary<string, RelationalNode>(StringComparer.Ordinal);
        var nodes = new List<RelationalNode>(graph.Nodes.Count);
        foreach (var nd in graph.Nodes)
        {
            var node = new RelationalNode(nd);
            byId[nd.Id] = node;
            nodes.Add(node);
        }

        var edges = new List<RelationalEdge>(graph.Edges.Count);
        foreach (var e in graph.Edges)
            if (byId.TryGetValue(e.FromId, out var f) && byId.TryGetValue(e.ToId, out var t))
                edges.Add(new RelationalEdge(f, t, e.LinkLabel, e.LinkSpan));

        ApplyLayout(nodes, edges);

        Nodes.Clear();
        foreach (var n in nodes) Nodes.Add(n);
        Edges = edges;

        Status = nodes.Count == 0
            ? "No project objects to show. Open a .thconfig / .th project first."
            : $"{nodes.Count} nodes · {edges.Count} links";

        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-applies the selected layout to the existing nodes (no graph rebuild).</summary>
    public void Relayout()
    {
        if (Nodes.Count == 0) return;
        ApplyLayout(Nodes.ToList(), Edges);
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NavigateNode(RelationalNode? node)
    {
        if (node?.Declaration is { } span) Navigate(span);
    }

    public void Navigate(SourceSpan span)
    {
        if (span.IsEmpty || string.IsNullOrEmpty(span.FilePath)) return;
        _ = _docs?.NavigateToSpanAsync(span);
    }

    // ---- layout ---------------------------------------------------------------

    private const double Margin = 30;

    private void ApplyLayout(List<RelationalNode> nodes, IReadOnlyList<RelationalEdge> edges)
    {
        if (nodes.Count == 0) { CanvasWidth = 400; CanvasHeight = 300; ShowEdges = true; return; }
        var kind = SelectedLayout?.Kind ?? RelationalLayoutKind.TreeVertical;
        ShowEdges = true;   // the only edge-less layout (Nested) is disabled

        // Reset to the natural size before each layout (level sizing may have changed it).
        foreach (var n in nodes) { n.Nested = false; n.Width = 168; n.Height = n.BaseHeight; }

        // Nested (containment) layout is disabled — it does not display properly. See NestedLayout below.
        // if (kind == RelationalLayoutKind.Nested) { NestedLayout(nodes, edges); FitCanvas(nodes); return; }

        ComputeDepths(nodes, edges);
        if (SizeByLevel) ApplyLevelSizing(nodes);
        switch (kind)
        {
            case RelationalLayoutKind.TreeHorizontal: TreeLayout(nodes, edges, vertical: false); break;
            case RelationalLayoutKind.LayeredColumns: LayeredLayout(nodes, edges); break;
            default: TreeLayout(nodes, edges, vertical: true); break;
        }
        FitCanvas(nodes);
    }

    // Fills node.Depth via a spanning-forest BFS (roots = in-degree-0), independent of the layout.
    private static void ComputeDepths(List<RelationalNode> nodes, IReadOnlyList<RelationalEdge> edges)
    {
        var outAdj = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var indeg = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var e in edges)
            if (outAdj.ContainsKey(e.From.Id) && indeg.ContainsKey(e.To.Id)) { outAdj[e.From.Id].Add(e.To.Id); indeg[e.To.Id]++; }

        var depth = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Bfs(string s)
        {
            var q = new Queue<string>(); q.Enqueue(s); visited.Add(s);
            while (q.Count > 0)
            {
                var u = q.Dequeue();
                foreach (var v in outAdj[u]) if (visited.Add(v)) { depth[v] = depth[u] + 1; q.Enqueue(v); }
            }
        }
        foreach (var n in nodes) if (indeg[n.Id] == 0 && !visited.Contains(n.Id)) Bfs(n.Id);
        foreach (var n in nodes) if (!visited.Contains(n.Id)) Bfs(n.Id);
        foreach (var n in nodes) n.Depth = depth[n.Id];
    }

    // Bigger nodes near the root, smaller leaves — so a large project reads better when fit to screen.
    private static void ApplyLevelSizing(List<RelationalNode> nodes)
    {
        foreach (var n in nodes)
        {
            double scale = Math.Clamp(1.7 - 0.18 * n.Depth, 0.55, 1.7);
            n.Width = Math.Round(168 * scale);
            n.Height = Math.Round(n.BaseHeight * scale);
        }
    }

    // Nested-containment ("embedded") layout: the root box contains its children, which contain
    // theirs, recursively — a slice-and-dice treemap weighted by subtree size.
    //
    // DISABLED (#2): this layout does not display properly — the containment boxes overlap and the
    // diagram frequently renders blank at real project sizes. It is excluded from LayoutOptions and
    // its call site in ApplyLayout is commented out. The implementation is kept (compiled out) below
    // so it can be revisited/fixed later rather than lost.
#if false
    private void NestedLayout(List<RelationalNode> nodes, IReadOnlyList<RelationalEdge> edges)
    {
        var outAdj = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var indeg = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var e in edges)
            if (outAdj.ContainsKey(e.From.Id) && indeg.ContainsKey(e.To.Id)) { outAdj[e.From.Id].Add(e.To.Id); indeg[e.To.Id]++; }
        var byId = nodes.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var children = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var roots = new List<string>();
        void Bfs(string s)
        {
            var q = new Queue<string>(); q.Enqueue(s); visited.Add(s);
            while (q.Count > 0)
            {
                var u = q.Dequeue();
                foreach (var v in outAdj[u]) if (visited.Add(v)) { children[u].Add(v); q.Enqueue(v); }
            }
        }
        foreach (var n in nodes.Where(n => indeg[n.Id] == 0).OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase))
            if (!visited.Contains(n.Id)) { roots.Add(n.Id); Bfs(n.Id); }
        foreach (var n in nodes) if (!visited.Contains(n.Id)) { roots.Add(n.Id); Bfs(n.Id); }

        var weight = new Dictionary<string, double>(StringComparer.Ordinal);
        double Weight(string id)
        {
            if (weight.TryGetValue(id, out var w)) return w;
            double sum = 1;
            foreach (var c in children[id]) sum += Weight(c);
            return weight[id] = sum;
        }
        foreach (var r in roots) Weight(r);

        void Nest(string id, double x, double y, double w, double h, int depth)
        {
            var n = byId[id];
            n.Nested = true; n.Depth = depth;
            n.X = x; n.Y = y; n.Width = Math.Max(40, w); n.Height = Math.Max(26, h);
            var ch = children[id];
            if (ch.Count == 0) return;

            const double headerH = 24, p = 6;
            double ix = x + p, iy = y + headerH, iw = w - 2 * p, ih = h - headerH - p;
            if (iw < 24 || ih < 24) return;

            bool horizontal = depth % 2 == 0;
            double total = ch.Sum(c => weight[c]);
            double cursor = horizontal ? ix : iy;
            foreach (var c in ch)
            {
                double frac = weight[c] / total;
                if (horizontal) { double cw = iw * frac; Nest(c, cursor, iy, cw - 4, ih, depth + 1); cursor += cw; }
                else            { double chh = ih * frac; Nest(c, ix, cursor, iw, chh - 4, depth + 1); cursor += chh; }
            }
        }

        double totalW = roots.Sum(r => weight[r]);
        double canvasW = Math.Max(960, totalW * 24);
        double canvasH = 700;
        const double pad = 8;
        double off = pad;
        foreach (var r in roots)
        {
            double w = (canvasW - 2 * pad) * (weight[r] / Math.Max(1, totalW)) - pad;
            Nest(r, off, pad, Math.Max(80, w), canvasH - 2 * pad, 0);
            off += w + pad;
        }
    }
#endif

    // Spanning-tree layout: each branch gets its own contiguous band (parent centred over its
    // children) so sibling subtrees don't overlap and cross-links are minimised.
    private void TreeLayout(List<RelationalNode> nodes, IReadOnlyList<RelationalEdge> edges, bool vertical)
    {
        var byId = nodes.ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);
        var outAdj = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var indeg = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (!outAdj.ContainsKey(e.From.Id) || !byId.ContainsKey(e.To.Id)) continue;
            outAdj[e.From.Id].Add(e.To.Id);
            indeg[e.To.Id]++;
        }
        foreach (var list in outAdj.Values)
            list.Sort((a, b) => string.Compare(byId[a].Label, byId[b].Label, StringComparison.OrdinalIgnoreCase));

        // Build a spanning forest: roots are in-degree-0 nodes (then any unvisited leftovers).
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var children = nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var rootOrder = new List<string>();

        void Bfs(string start)
        {
            var q = new Queue<string>();
            q.Enqueue(start); visited.Add(start);
            while (q.Count > 0)
            {
                var u = q.Dequeue();
                foreach (var v in outAdj[u])
                    if (visited.Add(v)) { children[u].Add(v); q.Enqueue(v); }
            }
        }

        foreach (var n in nodes.Where(n => indeg[n.Id] == 0)
                               .OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase))
            if (!visited.Contains(n.Id)) { rootOrder.Add(n.Id); Bfs(n.Id); }
        foreach (var n in nodes)
            if (!visited.Contains(n.Id)) { rootOrder.Add(n.Id); Bfs(n.Id); }

        double nodeW = nodes.Max(n => n.Width);   // max so level-sized nodes never overlap (#2)
        double nodeH = nodes.Max(n => n.Height);
        double primaryGap = vertical ? nodeW + 34 : nodeH + 36;   // sibling spacing
        double depthGap   = vertical ? nodeH + 60 : nodeW + 70;   // per-level spacing

        var primary = new Dictionary<string, double>(StringComparer.Ordinal);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        double cursor = 0;

        void Place(string id, int d)
        {
            depth[id] = d;
            var ch = children[id];
            if (ch.Count == 0) { primary[id] = cursor; cursor += primaryGap; return; }
            foreach (var c in ch) Place(c, d + 1);
            primary[id] = (primary[ch[0]] + primary[ch[^1]]) / 2;
        }

        foreach (var r in rootOrder) { Place(r, 0); cursor += primaryGap; }

        foreach (var n in nodes)
        {
            var p = primary.TryGetValue(n.Id, out var pv) ? pv : 0;
            var d = depth.TryGetValue(n.Id, out var dv) ? dv : 0;
            if (vertical) { n.X = Margin + p; n.Y = Margin + d * depthGap; }
            else          { n.X = Margin + d * depthGap; n.Y = Margin + p; }
        }
    }

    // Columns by longest-path depth (compact, but cross-links can overlap; offered as an option).
    private void LayeredLayout(List<RelationalNode> nodes, IReadOnlyList<RelationalEdge> edges)
    {
        const double columnWidth = 230, rowHeight = 78;
        var level = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var indeg = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var e in edges) if (indeg.ContainsKey(e.To.Id)) indeg[e.To.Id]++;

        for (int iter = 0; iter < nodes.Count + 2; iter++)
        {
            bool changed = false;
            foreach (var e in edges)
            {
                if (!level.ContainsKey(e.From.Id) || !level.ContainsKey(e.To.Id)) continue;
                if (level[e.To.Id] < level[e.From.Id] + 1) { level[e.To.Id] = level[e.From.Id] + 1; changed = true; }
            }
            if (!changed) break;
        }
        foreach (var n in nodes) if (indeg[n.Id] == 0) level[n.Id] = 0;

        foreach (var group in nodes.GroupBy(n => level[n.Id]).OrderBy(g => g.Key))
        {
            double y = Margin;
            foreach (var n in group.OrderBy(n => n.Label, StringComparer.OrdinalIgnoreCase))
            {
                n.X = Margin + group.Key * columnWidth;
                n.Y = y;
                y += rowHeight;
            }
        }
    }

    private void FitCanvas(List<RelationalNode> nodes)
    {
        double maxX = 0, maxY = 0;
        foreach (var n in nodes)
        {
            maxX = Math.Max(maxX, n.X + n.Width);
            maxY = Math.Max(maxY, n.Y + n.Height);
        }
        CanvasWidth = Math.Max(400, maxX + Margin);
        CanvasHeight = Math.Max(300, maxY + Margin);
    }

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}
