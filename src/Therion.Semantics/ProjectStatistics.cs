// PROJ-03 / PROJ-07 / PROJ-02 — project-wide analytics over a WorkspaceSemanticModel.
// Pure aggregation (no UI / no disk) so it is unit-testable and reused by the survey-tree,
// dashboard and audit tools. Lengths/depths are "preview-quality": computed from our own model
// (shot length / clino), not from a full Therion/Survex adjustment.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;

namespace Therion.Semantics;

/// <summary>A node in the logical survey hierarchy (<c>a.b.c</c>) with rolled-up subtree counts.</summary>
public sealed record SurveyTreeNode(string Name, string FullName, SourceSpan Declaration)
{
    public string? Title { get; init; }
    /// <summary>Distinct stations in this survey and its descendants.</summary>
    public int Stations { get; init; }
    /// <summary>Non-splay legs in this survey and its descendants.</summary>
    public int Shots { get; init; }
    /// <summary>Surveyed length (metres, splays/duplicates excluded) in this subtree.</summary>
    public double Length { get; init; }
    public List<SurveyTreeNode> Children { get; } = new();
}

/// <summary>Headline project metrics for the dashboard (PROJ-07).</summary>
public sealed record ProjectTotals(
    int SurveyCount,
    int StationCount,
    int ShotCount,
    double TotalLength,
    double VerticalRange,
    int EntranceCount,
    int FixedCount);

/// <summary>Pure project-wide analytics computed from a <see cref="WorkspaceSemanticModel"/>.</summary>
public static class ProjectStatistics
{
    /// <summary>Builds the logical survey hierarchy with rolled-up station/shot/length counts.</summary>
    public static IReadOnlyList<SurveyTreeNode> BuildSurveyTree(WorkspaceSemanticModel model)
    {
        var nodeNames = new HashSet<string>(model.SurveysByFullName.Keys, StringComparer.Ordinal);
        if (nodeNames.Count == 0) return Array.Empty<SurveyTreeNode>();

        var directStations = new Dictionary<string, int>(StringComparer.Ordinal);
        var directShots = new Dictionary<string, int>(StringComparer.Ordinal);
        var directLength = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var st in model.StationsByQn.Values)
        {
            var sv = SurveyOf(st.Name, nodeNames);
            if (sv is not null) directStations[sv] = directStations.GetValueOrDefault(sv) + 1;
        }

        foreach (var perFile in model.PerFile.Values)
            foreach (var shot in perFile.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                var sv = SurveyOf(shot.From, nodeNames);
                if (sv is null) continue;
                directShots[sv] = directShots.GetValueOrDefault(sv) + 1;
                if (shot.Length is { } len && (shot.Flags & ShotFlags.Duplicate) == 0)
                    directLength[sv] = directLength.GetValueOrDefault(sv) + len;
            }

        // Parent/child links from the dotted hierarchy.
        var children = nodeNames.ToDictionary(n => n, _ => new List<string>(), StringComparer.Ordinal);
        var roots = new List<string>();
        foreach (var sym in model.SurveysByFullName.Values)
        {
            var full = sym.Name.ToString();
            var parent = sym.Name.HasParent ? sym.Name.Parent().ToString() : null;
            if (parent is not null && nodeNames.Contains(parent)) children[parent].Add(full);
            else roots.Add(full);
        }

        SurveyTreeNode Build(string full)
        {
            var childNodes = children[full]
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Select(Build)
                .ToList();
            var sym = model.SurveysByFullName.GetValueOrDefault(full);
            int stations = directStations.GetValueOrDefault(full) + childNodes.Sum(c => c.Stations);
            int shots = directShots.GetValueOrDefault(full) + childNodes.Sum(c => c.Shots);
            double length = directLength.GetValueOrDefault(full) + childNodes.Sum(c => c.Length);
            var node = new SurveyTreeNode(
                sym?.Name.Last ?? full, full, sym?.DeclarationSpan ?? SourceSpan.None)
            {
                Title = sym?.Title,
                Stations = stations,
                Shots = shots,
                Length = length,
            };
            foreach (var c in childNodes) node.Children.Add(c);
            return node;
        }

        return roots
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .Select(Build)
            .ToList();
    }

    /// <summary>Computes headline totals (length, vertical range, counts) for the whole project.</summary>
    public static ProjectTotals ComputeTotals(WorkspaceSemanticModel model)
    {
        int entrances = 0, fixedPts = 0;
        foreach (var st in model.StationsByQn.Values)
        {
            if (st.IsEntrance) entrances++;
            if (st.Kind == StationDeclarationKind.Fix) fixedPts++;
        }

        int shotCount = 0;
        double totalLength = 0;
        // Undirected graph (station-qn → neighbours with signed dz) for a relative depth estimate.
        var adjacency = new Dictionary<string, List<(string To, double Dz)>>(StringComparer.Ordinal);
        void Link(string a, string b, double dz)
        {
            (adjacency.TryGetValue(a, out var la) ? la : adjacency[a] = new()).Add((b, dz));
            (adjacency.TryGetValue(b, out var lb) ? lb : adjacency[b] = new()).Add((a, -dz));
        }

        foreach (var perFile in model.PerFile.Values)
            foreach (var shot in perFile.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                shotCount++;
                if (shot.Length is { } len)
                {
                    if ((shot.Flags & ShotFlags.Duplicate) == 0) totalLength += len;
                    double dz = shot.Clino is { } clino ? len * Math.Sin(clino * Math.PI / 180.0) : 0;
                    Link(shot.From.ToString(), shot.To.ToString(), dz);
                }
            }

        return new ProjectTotals(
            model.SurveysByFullName.Count,
            model.StationsByQn.Count,
            shotCount,
            totalLength,
            VerticalRange(adjacency),
            entrances,
            fixedPts);
    }

    /// <summary>Scraps (.th2) whose id is not composed by any <c>map</c> body (PROJ-02 / LANG-08).</summary>
    public static IReadOnlyList<string> UnreferencedScraps(WorkspaceSemanticModel model)
    {
        if (model.ScrapsById.Count == 0) return Array.Empty<string>();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in model.MapsById.Values)
            foreach (var member in map.Members)
                referenced.Add(member);
        return model.ScrapsById.Keys
            .Where(id => !referenced.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The known survey a station belongs to: its parent path, walked up to the nearest node.</summary>
    private static string? SurveyOf(QualifiedName station, HashSet<string> nodeNames)
    {
        var qn = station;
        while (qn.HasParent)
        {
            qn = qn.Parent();
            var name = qn.ToString();
            if (nodeNames.Contains(name)) return name;
        }
        return null;
    }

    // Relative depth: BFS each component from an arbitrary root (z=0), accumulate dz, span = max−min.
    private static double VerticalRange(Dictionary<string, List<(string To, double Dz)>> adjacency)
    {
        if (adjacency.Count == 0) return 0;
        var z = new Dictionary<string, double>(StringComparer.Ordinal);
        double globalMin = 0, globalMax = 0;
        foreach (var start in adjacency.Keys)
        {
            if (z.ContainsKey(start)) continue;
            z[start] = 0;
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                double zu = z[u];
                globalMin = Math.Min(globalMin, zu);
                globalMax = Math.Max(globalMax, zu);
                foreach (var (v, dz) in adjacency[u])
                    if (!z.ContainsKey(v)) { z[v] = zu + dz; queue.Enqueue(v); }
            }
        }
        return globalMax - globalMin;
    }
}
