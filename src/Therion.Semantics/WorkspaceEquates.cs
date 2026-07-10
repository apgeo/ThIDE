// Workspace-wide equate classes: which stations Therion considers the same point, across the whole
// project rather than one file at a time.
//
// A cave stitched together with `equate 0@thispart 14@mainpassage` is one connected cave. Any
// analysis that unions only the per-file equate classes sees it as two disconnected pieces and says
// so — which is exactly the false positive TH_SEM_015 exists to avoid. Both the disconnection
// diagnostic and the MCP `survey_graph` tool build their node identities here, so they cannot
// disagree about what "the same station" means.

using System;
using Therion.Processing.Abstractions;

namespace Therion.Semantics;

public static class WorkspaceEquates
{
    /// <summary>
    /// Union-find over every station name in the workspace, merging both the per-file equate classes
    /// and the cross-file <c>@</c>-equates resolved through <paramref name="workspace"/>.
    /// </summary>
    public static EquateGraph Build(WorkspaceSemanticModel workspace)
    {
        var equates = new EquateGraph();

        foreach (var model in workspace.PerFile.Values)
            foreach (var group in model.Equates.Groups())
                for (int i = 1; i < group.Length; i++)
                    equates.Union(group[0], group[i]);

        foreach (var model in workspace.PerFile.Values)
            foreach (var record in model.EquateRecords)
            {
                QualifiedName? first = null;
                foreach (var raw in record.Stations)
                {
                    if (ResolveMember(workspace, model, raw) is not { } qn) continue;
                    if (first is null) first = qn;
                    else equates.Union(first.Value, qn);
                }
            }

        return equates;
    }

    /// <summary>
    /// Resolves one <c>equate</c> member token (<c>0</c>, <c>0@part</c>, <c>cave.part.0</c>) to the
    /// station it names, or <c>null</c> when it names nothing resolvable.
    /// </summary>
    public static QualifiedName? ResolveMember(WorkspaceSemanticModel workspace, SemanticModel model, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (workspace.ResolveStationSymbol(raw) is { } sym) return sym.Name;

        var r = StationRef.Parse(raw);
        if (!r.HasSurvey)
        {
            var direct = QualifiedName.Of(r.Point);
            if (model.Stations.ContainsKey(direct)) return direct;
        }

        // A unique last-name match inside this one file (station names are effectively unique per
        // survey; requiring uniqueness avoids guessing across surveys that reuse a name).
        QualifiedName? unique = null;
        foreach (var name in model.Stations.Keys)
            if (string.Equals(name.Last, r.Point, StringComparison.Ordinal))
            {
                if (unique is not null) return null;
                unique = name;
            }
        return unique;
    }
}
