// (Phase 3) — maps a compiled-model station/survey label back to its `.th` source span.
//
// CaveView reports a picked station by its full top-down dotted path (outer.inner.point), which
// is exactly the form the semantic binder keys QualifiedName / StationsByQn by — so most picks
// resolve by an exact lookup. Survey-path prefixing and `point@survey` are handled by the shared
// WorkspaceSemanticModel.ResolveReference; equates / model-vs-source name drift fall back to a
// (usually-unique) bare last-name lookup. When nothing resolves uniquely we degrade to a status
// message instead of navigating (plan risk R3 — this mapping is preview-quality, best-effort).

using System;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;

namespace ThIDE.Services;

/// <summary>Outcome of resolving a 3D-model label to source. <see cref="Found"/> ⇒ navigate to <see cref="Span"/>.</summary>
public sealed record StationSourceResult(SourceSpan? Span, string? Kind, string Message)
{
    public bool Found => Span is { IsEmpty: false };

    public static StationSourceResult NotFound(string message) => new(null, null, message);
}

public interface IStationSourceResolver
{
    /// <summary>
    /// Resolves a model label (station or survey path) to a source declaration span. The active
    /// per-file model, when supplied, also matches freshly-typed (unsaved) identifiers.
    /// </summary>
    StationSourceResult Resolve(string label, WorkspaceSemanticModel? workspace, SemanticModel? activeFile = null);
}

public sealed class StationSourceResolver : IStationSourceResolver
{
    public StationSourceResult Resolve(string label, WorkspaceSemanticModel? workspace, SemanticModel? activeFile = null)
    {
        var clean = (label ?? string.Empty).Trim();
        if (clean.Length == 0) return StationSourceResult.NotFound("No station selected.");

        if (workspace is null)
            return StationSourceResult.NotFound("No project is loaded — open and build a project first.");

        // 1) Exact station: QN (outer.inner.point) or point@survey.
        if (workspace.ResolveReference(clean, ReferenceKind.Station) is { IsEmpty: false } station)
            return new StationSourceResult(station, "station", $"Station {clean}");

        // 2) The whole label is a survey scope (e.g. selecting a sub-survey box).
        if (workspace.ResolveReference(clean, ReferenceKind.Survey) is { IsEmpty: false } survey)
            return new StationSourceResult(survey, "survey", $"Survey {clean}");

        // 3) Bare last-name fallback — covers equates / name drift between model and source,
        //    where the model's dotted path differs from the binder's first-definition QN.
        var last = LastComponent(clean);
        if (workspace.StationsByLastName.TryGetValue(last, out var byName) && !byName.IsDefaultOrEmpty)
        {
            if (byName.Length == 1)
                return new StationSourceResult(byName[0].DeclarationSpan, "station", $"Station {last}");

            // Several stations share the point name: prefer one whose full QN is a suffix/extension
            // of the picked label (same trailing survey path), else report the ambiguity.
            foreach (var cand in byName)
            {
                var qn = cand.Name.ToString();
                if (qn.EndsWith(clean, StringComparison.Ordinal) || clean.EndsWith(qn, StringComparison.Ordinal))
                    return new StationSourceResult(cand.DeclarationSpan, "station", $"Station {qn}");
            }
            return StationSourceResult.NotFound($"“{last}” matches {byName.Length} stations — can’t pick one uniquely.");
        }

        // 4) Active file's fresh parse: catch identifiers added since the last save.
        if (activeFile is not null && activeFile.TryResolve(clean, out var fresh) && !fresh.IsEmpty)
            return new StationSourceResult(fresh, "station", $"Station {clean}");

        return StationSourceResult.NotFound($"No source declaration found for “{clean}”.");
    }

    private static string LastComponent(string dotted)
    {
        int dot = dotted.LastIndexOf('.');
        return dot >= 0 ? dotted[(dot + 1)..] : dotted;
    }
}
