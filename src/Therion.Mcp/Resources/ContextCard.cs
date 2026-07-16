using System.Globalization;
using System.Text;
using Therion.Core;
using Therion.Mcp.Tools;
using Therion.Semantics;

namespace Therion.Mcp.Resources;

/// <summary>
/// The workspace "context card" and "pack" (CD-02): a small, stable, orienting digest the Assistant
/// pane injects as a second system message and an external host attaches as a resource — one
/// generator, two consumers, so the two can never drift. It is deliberately <em>not</em> the whole
/// semantic model: retrieval-by-tool beats scanning a JSON dump on the small-context local models we
/// target, and the conversation outlives the edits, so a pushed model goes stale by turn three. Hence
/// only totals, structure and diagnostics counts — plus an explicit "verify with tools" caveat, since
/// every figure here is a snapshot. Model-facing text is English on purpose (D-008: the rendered UI is
/// localized; text sent to the model is not).
/// </summary>
internal static class ContextCard
{
    // The Card shows the top levels of the survey tree; the Pack shows the whole thing.
    private const int CardTreeDepth = 2;
    // The Pack lists this many diagnostics (most severe first); get_diagnostics pages the rest.
    private const int PackTopDiagnostics = 20;

    /// <summary>The compact orienting card (~300–600 tokens): totals, top survey levels, diagnostics counts.</summary>
    public static string Card(WorkspaceSnapshot snapshot)
    {
        var model = snapshot.Model;
        var totals = ProjectStatistics.ComputeTotals(model);
        var (errors, warnings, infoHints) = DiagnosticCounts(snapshot);
        var tree = ProjectStatistics.BuildSurveyTree(model);

        var sb = new StringBuilder();
        sb.AppendLine("# ThIDE workspace context (approximate snapshot — verify with tools before acting)");
        sb.AppendLine();
        sb.Append("Workspace: ").AppendLine(Path.GetFileName(snapshot.Root.TrimEnd('/', '\\')));
        sb.Append("Entry point: ").AppendLine(Relative(snapshot.Root, snapshot.EntryPointPath));
        sb.AppendLine();
        sb.Append("Totals: ")
          .Append(Count(totals.SurveyCount)).Append(" surveys, ")
          .Append(Count(totals.StationCount)).Append(" stations, ")
          .Append(Count(totals.ShotCount)).Append(" shots, ")
          .Append(Metres(totals.TotalLength)).Append(" m surveyed, ")
          .Append(Metres(totals.VerticalRange)).Append(" m vertical range, ")
          .Append(Count(totals.EntranceCount)).Append(" entrances, ")
          .Append(Count(totals.FixedCount)).AppendLine(" fixed points.");
        sb.Append("Diagnostics: ")
          .Append(Count(errors)).Append(" errors, ")
          .Append(Count(warnings)).Append(" warnings, ")
          .Append(Count(infoHints)).AppendLine(" info/hints.");

        if (tree.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Survey tree (top levels, stations per survey):");
            AppendTree(sb, tree, CardTreeDepth);
        }

        sb.AppendLine();
        sb.Append(
            "Stations, legs, surveys, connectivity, diagnostics and file structure are all queryable — "
            + "prefer the tools (survey_stats, survey_graph, list_stations, query_legs, get_diagnostics) "
            + "over guessing. These figures are a snapshot and may be stale.");
        return sb.ToString();
    }

    /// <summary>The richer digest (~2–6 KB): the card plus the full survey tree, file list, top diagnostics, inventory.</summary>
    public static string Pack(WorkspaceSnapshot snapshot)
    {
        var model = snapshot.Model;
        var sb = new StringBuilder();
        sb.AppendLine(Card(snapshot));

        sb.AppendLine();
        sb.AppendLine("## Files");
        foreach (var file in snapshot.LoadedFiles.Select(f => Relative(snapshot.Root, f)).OrderBy(f => f, StringComparer.Ordinal))
            sb.Append("- ").AppendLine(file);

        var tree = ProjectStatistics.BuildSurveyTree(model);
        if (tree.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Survey tree (full)");
            AppendTree(sb, tree, int.MaxValue, withShotsAndLength: true);
        }

        var diagnostics = DiagnosticsTools.Collect(snapshot)
            .OrderByDescending(d => d.Severity)
            .Take(PackTopDiagnostics)
            .Select(d => DiagnosticDto.From(d, snapshot.Root))
            .ToList();
        if (diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.Append("## Diagnostics (top ").Append(Count(diagnostics.Count)).AppendLine(" by severity)");
            foreach (var d in diagnostics)
            {
                sb.Append("- ").Append(d.Severity).Append(' ').Append(d.Code).Append(": ").Append(d.Message);
                if (d.File is not null) sb.Append("  (").Append(d.File).Append(':').Append(d.Line.ToString(CultureInfo.InvariantCulture)).Append(')');
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Inventory");
        sb.Append("Maps: ").Append(Count(model.MapsById.Count)).Append(". Scraps: ").Append(Count(model.ScrapsById.Count)).AppendLine(".");
        return sb.ToString();
    }

    private static void AppendTree(StringBuilder sb, IReadOnlyList<SurveyTreeNode> nodes, int maxDepth, bool withShotsAndLength = false, int depth = 0)
    {
        if (depth >= maxDepth) return;
        foreach (var node in nodes.OrderBy(n => n.Name, StringComparer.Ordinal))
        {
            sb.Append(new string(' ', depth * 2)).Append("- ").Append(node.Name)
              .Append(" — ").Append(Count(node.Stations)).Append(" stations");
            if (withShotsAndLength)
                sb.Append(", ").Append(Count(node.Shots)).Append(" shots, ").Append(Metres(node.Length)).Append(" m");
            sb.AppendLine();
            if (node.Children.Count > 0)
                AppendTree(sb, node.Children, maxDepth, withShotsAndLength, depth + 1);
        }
    }

    private static (int Errors, int Warnings, int InfoHints) DiagnosticCounts(WorkspaceSnapshot snapshot)
    {
        var all = DiagnosticsTools.Collect(snapshot);
        int errors = all.Count(d => d.Severity == DiagnosticSeverity.Error);
        int warnings = all.Count(d => d.Severity == DiagnosticSeverity.Warning);
        return (errors, warnings, all.Count - errors - warnings);
    }

    private static string Relative(string root, string absolute) => WorkspacePaths.ToRelative(root, absolute);

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Metres(double m) => m.ToString("0.0", CultureInfo.InvariantCulture);
}
