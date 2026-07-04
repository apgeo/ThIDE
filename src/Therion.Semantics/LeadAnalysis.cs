// exploration leads register.
//
// Mines every unexplored "lead" the project expresses, from these sources:
//   • station `continuation` flags                  (LeadKind.ContinuationFlag)
//   • station `dig` / `air-draught` flags            (LeadKind.StationFlag)
//   • station comment conventions (# QM / lead / ?)  (LeadKind.CommentMarker)
//   • `.th2` continuation / question points          (LeadKind.Th2Point)
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
    /// <summary>Station carrying a <c>dig</c> / <c>air-draught</c> flag (a positively-set station flag).</summary>
    StationFlag = 4,
}

/// <summary>One unexplored lead: its location, source kind and a short description.</summary>
public sealed record Lead(string Location, LeadKind Kind, string Description, SourceSpan Span)
{
    /// <summary>
    /// For station-flag leads, the exact flag word(s) shown in the Kind column
    /// (e.g. <c>continuation</c>, <c>dig</c>, <c>continuation, air-draught</c>); null otherwise.
    /// </summary>
    public string? FlagLabel { get; init; }

    /// <summary>True for explicit station-flag leads (continuation / dig / air-draught) — always shown;
    /// heuristic leads (comment / sketch point / dead-end) are opt-in behind the "show all" switch.</summary>
    public bool IsStationFlag => Kind is LeadKind.ContinuationFlag or LeadKind.StationFlag;

    public string KindLabel => FlagLabel ?? Kind switch
    {
        LeadKind.ContinuationFlag => "continuation flag",
        LeadKind.CommentMarker    => "comment",
        LeadKind.Th2Point         => "sketch point",
        LeadKind.DeadEnd          => "dead-end (unmarked)",
        LeadKind.StationFlag      => "station flag",
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

        // 1 & 2 — station lead flags (continuation / dig / air-draught) and comment markers.
        foreach (var model in workspace.PerFile.Values)
        {
            foreach (var st in model.Stations.Values)
            {
                var name = st.Name.ToString();
                var flagLabel = StationLeadFlags(st);
                if (flagLabel is not null)
                {
                    // continuation keeps its own kind (colour/back-compat); dig / air-draught map to StationFlag.
                    var kind = flagLabel.Contains("continuation", StringComparison.OrdinalIgnoreCase)
                        ? LeadKind.ContinuationFlag : LeadKind.StationFlag;
                    leads.Add(new Lead(name, kind,
                        string.IsNullOrWhiteSpace(st.Comment) ? flagLabel : st.Comment!, st.DeclarationSpan)
                        { FlagLabel = flagLabel });
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

    // Station flags that mark an unexplored lead, in the order shown in the Kind column.
    private static readonly string[] TargetFlags = { "continuation", "dig", "air-draught" };

    /// <summary>
    /// The lead-flag word(s) positively set on a station (honouring <c>not</c> toggles and the
    /// <c>air-draught:winter</c> qualifier), joined for display — or null if none are set.
    /// </summary>
    private static string? StationLeadFlags(StationSymbol st)
    {
        if (st.Flags.IsDefaultOrEmpty) return null;
        var active = FoldFlags(st.Flags);
        var hits = TargetFlags.Where(active.Contains).ToArray();
        return hits.Length == 0 ? null : string.Join(", ", hits);
    }

    /// <summary>
    /// Folds a raw <c>station … &lt;flags&gt;</c> token list into the set of positively-set flag heads.
    /// <c>not</c> negates the following flag; <c>attr</c>/<c>explored</c> begin a free-value tail;
    /// an <c>air-draught:winter</c> qualifier collapses to its <c>air-draught</c> head.
    /// </summary>
    private static HashSet<string> FoldFlags(ImmutableArray<string> flags)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool negate = false;
        foreach (var raw in flags)
        {
            if (string.Equals(raw, "not", StringComparison.OrdinalIgnoreCase)) { negate = true; continue; }
            if (string.Equals(raw, "attr", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(raw, "explored", StringComparison.OrdinalIgnoreCase)) break;
            int colon = raw.IndexOf(':');
            var head = colon < 0 ? raw : raw[..colon];
            if (negate) active.Remove(head); else active.Add(head);
            negate = false;
        }
        return active;
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
