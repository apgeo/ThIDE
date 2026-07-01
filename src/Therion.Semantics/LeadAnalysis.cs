// exploration leads register.
//
// Mines every unexplored "lead" the project expresses, from four sources:
//   • station `continuation` flags                 (LeadKind.ContinuationFlag)
//   • station comment conventions (# QM / lead / ?) (LeadKind.CommentMarker)
//   • `.th2` continuation / question points         (LeadKind.Th2Point)
// • topological dead-ends not otherwise marked (LeadKind.DeadEnd)
//
// Pure analysis over a WorkspaceSemanticModel — the UI (Leads tab) and any map overlay consume the
// resulting list, and lifecycle status is layered on top by the app.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;

namespace Therion.Semantics;

/// <summary>Where a lead came from.</summary>
public enum LeadKind
{
    ContinuationFlag = 0,
    CommentMarker = 1,
    Th2Point = 2,
    DeadEnd = 3,
}

/// <summary>One unexplored lead: its location, source kind and a short description.</summary>
public sealed record Lead(string Location, LeadKind Kind, string Description, SourceSpan Span)
{
    public string KindLabel => Kind switch
    {
        LeadKind.ContinuationFlag => "continuation flag",
        LeadKind.CommentMarker    => "comment",
        LeadKind.Th2Point         => "sketch point",
        LeadKind.DeadEnd          => "dead-end (unmarked)",
        _                         => "lead",
    };
}

public static class LeadAnalysis
{
    // Comment conventions that flag a lead (case-insensitive substring match).
    private static readonly string[] CommentMarkers = { "qm", "lead", "continues", "dig", "?" };

    public static ImmutableArray<Lead> Analyze(WorkspaceSemanticModel? workspace)
    {
        if (workspace is null) return ImmutableArray<Lead>.Empty;

        var leads = ImmutableArray.CreateBuilder<Lead>();
        var flagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // stations already a lead

        // 1 & 2 — station continuation flags and comment markers.
        foreach (var model in workspace.PerFile.Values)
        {
            foreach (var st in model.Stations.Values)
            {
                var name = st.Name.ToString();
                if (st.IsContinuation)
                {
                    leads.Add(new Lead(name, LeadKind.ContinuationFlag,
                        string.IsNullOrWhiteSpace(st.Comment) ? "continuation" : st.Comment!, st.DeclarationSpan));
                    flagged.Add(name);
                }
                else if (st.Comment is { Length: > 0 } c && IsLeadComment(c))
                {
                    leads.Add(new Lead(name, LeadKind.CommentMarker, c.Trim(), st.DeclarationSpan));
                    flagged.Add(name);
                }
            }
        }

        // 3 — .th2 continuation / question points.
        foreach (var o in workspace.Th2Objects)
        {
            if (!string.Equals(o.Kind, "point", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsLeadPointType(o.Type)) continue;
            leads.Add(new Lead(o.ScrapId, LeadKind.Th2Point, o.Type, o.Span));
        }

        // 4 — : topological dead-ends the surveyor didn't mark as a continuation.
        try
        {
            var graph = ConnectivityGraph.Build(workspace);
            foreach (var node in graph.DeadEnds)
            {
                var key = node.ToString();
                if (flagged.Contains(key)) continue;   // already a lead via flag/comment
                if (!workspace.StationsByQn.TryGetValue(key, out var st)) continue;
                leads.Add(new Lead(key, LeadKind.DeadEnd, "leaf node (no continuation flag)", st.DeclarationSpan));
            }
        }
        catch { /* graph build is best-effort; flag/comment/point leads still stand */ }

        return leads
            .OrderBy(l => l.Kind)
            .ThenBy(l => l.Location, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool IsLeadComment(string comment)
    {
        foreach (var m in CommentMarkers)
            if (comment.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsLeadPointType(string type)
    {
        // Type may be "continuation", "u:question", "question", etc.
        return type.Contains("continuation", StringComparison.OrdinalIgnoreCase)
            || type.Contains("question", StringComparison.OrdinalIgnoreCase);
    }
}
