// Builds the object-relational graph for the "Relational Map" panel from a workspace
// semantic snapshot. Nodes are the logical Therion objects (surveys, maps, scraps) and,
// optionally, the host files (thconfig / .th / .th2). Edges are the relations between them:
//   * survey → child survey      (logical nesting)
//   * survey → map / scrap        (ownership, used when files are hidden)
//   * file   → file               (source / input / load inclusion)
//   * file   → survey / map / scrap (declaration site, used when files are shown)
//
// Stations / shots / measurements are intentionally excluded (far too many).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Therion.Core;
using Therion.Semantics;

namespace ThIDE.Services;

public enum RelationalNodeKind { Thconfig, ThFile, Th2File, OtherFile, Survey, Map, Scrap }

/// <summary>A graph node (a logical object or a host file).</summary>
public sealed record RelationalNodeData(
    string Id,
    string Label,
    string SubLabel,
    RelationalNodeKind Kind,
    SourceSpan? Declaration);

/// <summary>A directed relation. <see cref="LinkSpan"/> is where the link is written (for navigation).</summary>
public sealed record RelationalEdgeData(
    string FromId,
    string ToId,
    string LinkLabel,
    SourceSpan? LinkSpan);

public sealed record RelationalGraph(
    IReadOnlyList<RelationalNodeData> Nodes,
    IReadOnlyList<RelationalEdgeData> Edges)
{
    public static RelationalGraph Empty { get; } =
        new(Array.Empty<RelationalNodeData>(), Array.Empty<RelationalEdgeData>());
}

public static class RelationalGraphBuilder
{
    private const string FilePrefix   = "file:";
    private const string SurveyPrefix = "survey:";
    private const string MapPrefix    = "map:";
    private const string ScrapPrefix  = "scrap:";

    public static RelationalGraph Build(WorkspaceSemanticModel? model, string? activeThconfig, bool includeFiles)
    {
        if (model is null) return RelationalGraph.Empty;

        var nodes = new Dictionary<string, RelationalNodeData>(StringComparer.Ordinal);
        var edges = new List<RelationalEdgeData>();
        void AddNode(RelationalNodeData n) { nodes.TryAdd(n.Id, n); }

        // ---- surveys (deduped across files by full dotted name) -----------------
        var surveysByName = new Dictionary<string, SurveySymbol>(StringComparer.Ordinal);
        foreach (var perFile in model.PerFile.Values)
            foreach (var sv in perFile.Surveys.Values)
                surveysByName[sv.Name.ToString()] = sv;

        foreach (var (name, sv) in surveysByName)
        {
            var sub = string.IsNullOrWhiteSpace(sv.Title) ? string.Empty : sv.Title!;
            AddNode(new RelationalNodeData(SurveyPrefix + name, sv.Name.Last, sub,
                RelationalNodeKind.Survey, sv.DeclarationSpan));
        }

        // survey → child survey (logical nesting), shown in both modes.
        foreach (var (name, sv) in surveysByName)
        {
            if (sv.Parent is not { } parentQn) continue;
            var parentKey = SurveyPrefix + parentQn.ToString();
            if (!nodes.ContainsKey(parentKey)) continue;
            edges.Add(new RelationalEdgeData(parentKey, SurveyPrefix + name,
                "survey " + sv.Name.Last, sv.DeclarationSpan));
        }

        // ---- maps ----------------------------------------------------------------
        foreach (var (id, map) in model.MapsById)
        {
            var sub = string.IsNullOrWhiteSpace(map.Title) ? string.Empty : map.Title!;
            AddNode(new RelationalNodeData(MapPrefix + id, id, sub, RelationalNodeKind.Map, map.DeclarationSpan));
        }

        // ---- scraps --------------------------------------------------------------
        foreach (var (id, scrap) in model.ScrapsById)
            AddNode(new RelationalNodeData(ScrapPrefix + id, id, string.Empty,
                RelationalNodeKind.Scrap, scrap.DeclarationSpan));

        if (includeFiles)
            AddFileLayer(model, activeThconfig, surveysByName, nodes, edges, AddNode);
        else
            AddContractedLogicalEdges(model, surveysByName, nodes, edges);

        return new RelationalGraph(nodes.Values.ToList(), edges);
    }

    // ---- files-included mode -------------------------------------------------

    private static void AddFileLayer(
        WorkspaceSemanticModel model,
        string? activeThconfig,
        Dictionary<string, SurveySymbol> surveysByName,
        Dictionary<string, RelationalNodeData> nodes,
        List<RelationalEdgeData> edges,
        Action<RelationalNodeData> addNode)
    {
        // Gather every file that participates: graph edges, per-file models, the active
        // thconfig, and any object declaration site.
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in model.FileGraphEdges) { files.Add(from); files.Add(to); }
        foreach (var p in model.PerFile.Keys) files.Add(p);
        if (!string.IsNullOrEmpty(activeThconfig)) files.Add(Full(activeThconfig));
        foreach (var sv in surveysByName.Values) AddFileOf(files, sv.DeclarationSpan);
        foreach (var m in model.MapsById.Values) AddFileOf(files, m.DeclarationSpan);
        foreach (var s in model.ScrapsById.Values) AddFileOf(files, s.DeclarationSpan);

        foreach (var f in files)
            addNode(new RelationalNodeData(FilePrefix + f, Path.GetFileName(f), DirLabel(f), FileKind(f), FileSpan(f)));

        // file → file (inclusion). The link span targets the included file (line 1) so
        // clicking it opens that file.
        foreach (var (from, to) in model.FileGraphEdges)
        {
            var fromId = FilePrefix + from;
            var toId = FilePrefix + to;
            if (!nodes.ContainsKey(fromId) || !nodes.ContainsKey(toId)) continue;
            edges.Add(new RelationalEdgeData(fromId, toId,
                $"{Path.GetFileName(from)} → {Path.GetFileName(to)}", FileSpan(to)));
        }

        // file → object (declaration site).
        foreach (var sv in surveysByName.Values)
            LinkFileToObject(nodes, edges, sv.DeclarationSpan, SurveyPrefix + sv.Name.ToString(),
                "declares survey " + sv.Name.Last, sv.DeclarationSpan);
        foreach (var (id, m) in model.MapsById)
            LinkFileToObject(nodes, edges, m.DeclarationSpan, MapPrefix + id, "declares map " + id, m.DeclarationSpan);
        foreach (var (id, s) in model.ScrapsById)
            LinkFileToObject(nodes, edges, s.DeclarationSpan, ScrapPrefix + id, "declares scrap " + id, s.DeclarationSpan);
    }

    private static void LinkFileToObject(
        Dictionary<string, RelationalNodeData> nodes, List<RelationalEdgeData> edges,
        SourceSpan decl, string objectId, string label, SourceSpan linkSpan)
    {
        if (string.IsNullOrEmpty(decl.FilePath)) return;
        var fileId = FilePrefix + Full(decl.FilePath);
        if (!nodes.ContainsKey(fileId) || !nodes.ContainsKey(objectId)) return;
        edges.Add(new RelationalEdgeData(fileId, objectId, label, linkSpan));
    }

    // ---- logical-only mode (no file nodes) -----------------------------------
    // Files are only *hosts* — the relations they carry (inclusion + declaration) are
    // re-linked directly between the logical objects so hiding files never disconnects the
    // graph. This "contracts" each file node onto the object(s) it contributes.

    private static void AddContractedLogicalEdges(
        WorkspaceSemanticModel model,
        Dictionary<string, SurveySymbol> surveysByName,
        Dictionary<string, RelationalNodeData> nodes,
        List<RelationalEdgeData> edges)
    {
        var surveysByFile = surveysByName.Values
            .Where(s => !string.IsNullOrEmpty(s.DeclarationSpan.FilePath))
            .GroupBy(s => Full(s.DeclarationSpan.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var mapsByFile = model.MapsById
            .Where(kv => !string.IsNullOrEmpty(kv.Value.DeclarationSpan.FilePath))
            .GroupBy(kv => Full(kv.Value.DeclarationSpan.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var scrapsByFile = model.ScrapsById
            .Where(kv => !string.IsNullOrEmpty(kv.Value.DeclarationSpan.FilePath))
            .GroupBy(kv => Full(kv.Value.DeclarationSpan.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // The "entry" objects a file contributes — what an inclusion of the file logically
        // points at: its root surveys, else any survey, else maps, else scraps.
        List<string> Rep(string file)
        {
            if (surveysByFile.TryGetValue(file, out var svs) && svs.Count > 0)
            {
                var roots = svs.Where(s => s.Parent is null).ToList();
                var chosen = roots.Count > 0 ? roots : svs;
                return chosen.Select(s => SurveyPrefix + s.Name.ToString()).ToList();
            }
            if (mapsByFile.TryGetValue(file, out var ms) && ms.Count > 0)
                return ms.Select(kv => MapPrefix + kv.Key).ToList();
            if (scrapsByFile.TryGetValue(file, out var scs) && scs.Count > 0)
                return scs.Select(kv => ScrapPrefix + kv.Key).ToList();
            return new List<string>();
        }

        string? SurveyRepOf(string file)
            => Rep(file).FirstOrDefault(id => id.StartsWith(SurveyPrefix, StringComparison.Ordinal));

        // Maps / scraps belong to a survey declared in the same file (its host survey).
        foreach (var (file, ms) in mapsByFile)
        {
            var parent = SurveyRepOf(file);
            if (parent is null) continue;
            foreach (var kv in ms)
                AddEdge(nodes, edges, parent, MapPrefix + kv.Key, "map " + kv.Key, kv.Value.DeclarationSpan);
        }
        foreach (var (file, scs) in scrapsByFile)
        {
            var parent = SurveyRepOf(file);
            if (parent is null) continue;
            foreach (var kv in scs)
                AddEdge(nodes, edges, parent, ScrapPrefix + kv.Key, "scrap " + kv.Key, kv.Value.DeclarationSpan);
        }

        // File inclusion becomes a relation between the host objects: rep(parent) → rep(child).
        foreach (var (from, to) in model.FileGraphEdges)
        {
            var parent = Rep(Full(from)).FirstOrDefault();
            if (parent is null) continue; // e.g. a thconfig hosts nothing → child reps become roots
            foreach (var childId in Rep(Full(to)))
            {
                if (parent == childId) continue;
                AddEdge(nodes, edges, parent, childId, LabelFor(nodes, childId), DeclOf(nodes, childId));
            }
        }
    }

    private static void AddEdge(
        Dictionary<string, RelationalNodeData> nodes, List<RelationalEdgeData> edges,
        string fromId, string toId, string label, SourceSpan? span)
    {
        if (nodes.ContainsKey(fromId) && nodes.ContainsKey(toId))
            edges.Add(new RelationalEdgeData(fromId, toId, label, span));
    }

    private static string LabelFor(Dictionary<string, RelationalNodeData> nodes, string id)
    {
        if (!nodes.TryGetValue(id, out var n)) return "link";
        var word = n.Kind switch
        {
            RelationalNodeKind.Survey => "survey ",
            RelationalNodeKind.Map    => "map ",
            RelationalNodeKind.Scrap  => "scrap ",
            _ => "",
        };
        return word + n.Label;
    }

    private static SourceSpan? DeclOf(Dictionary<string, RelationalNodeData> nodes, string id)
        => nodes.TryGetValue(id, out var n) ? n.Declaration : null;

    // ---- helpers -------------------------------------------------------------

    private static void AddFileOf(HashSet<string> files, SourceSpan span)
    {
        if (!string.IsNullOrEmpty(span.FilePath)) files.Add(Full(span.FilePath));
    }

    private static RelationalNodeKind FileKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".thconfig" or ".thc" => RelationalNodeKind.Thconfig,
        ".th2"                => RelationalNodeKind.Th2File,
        ".th"                 => RelationalNodeKind.ThFile,
        _                     => RelationalNodeKind.OtherFile,
    };

    private static SourceSpan FileSpan(string path) =>
        new(path, new SourceLocation(1, 1), new SourceLocation(1, 1), 0, 0);

    private static string DirLabel(string path)
    {
        try { return Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty); }
        catch { return string.Empty; }
    }

    private static string Full(string p) { try { return Path.GetFullPath(p); } catch { return p; } }
}
