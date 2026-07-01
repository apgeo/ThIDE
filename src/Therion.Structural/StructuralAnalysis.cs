// STRUCT-01 Phase 2 — the headless facade: SemanticModel + options → detected batches, fitted planes,
// cave legs and the resolved declination. The UI and a future CLI both drive this; the UI additionally
// calls Recompute() when the user toggles which measurements / splays / origin are included.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Therion.Semantics;

namespace Therion.Structural;

public static class StructuralAnalysis
{
    /// <summary>Runs detection + extraction + plane fitting + declination over a bound model.</summary>
    public static AnalysisResult Analyze(SemanticModel model, StructuralOptions options)
    {
        var solution = CenterlineGeometry.Solve(model.Shots, model.Equates);
        var batches = GeoStructureDetector.Detect(model, options.Detection, solution);
        var decl = DeclinationResolver.Resolve(options.Declination, options.DeclinationInputs);

        var planes = ImmutableArray.CreateBuilder<FittedPlane>(batches.Length);
        foreach (var batch in batches)
            planes.Add(Recompute(batch, batch.DefaultIncluded(), decl.Delta));

        return new AnalysisResult(batches, planes.ToImmutable(), solution.CaveLegs, decl);
    }

    /// <summary>
    /// Fits the given subset of a batch's measurements and applies declination δ. Prefers world
    /// coordinates (so multi-station batches share a frame); falls back to the local frame when every
    /// included point shares one origin station; otherwise reports the frame problem.
    /// </summary>
    public static FittedPlane Recompute(
        StructuralBatch batch, IReadOnlyCollection<StructuralMeasurement> included, double declinationDegrees)
    {
        int n = included.Count;
        if (n < 3) return FittedPlane.Invalid("not enough data points (need at least 3)", n);

        var points = ChooseFitPoints(included, out var frameError);
        if (frameError is not null) return FittedPlane.Invalid(frameError, n);

        return PlaneFitter.Fit(points).WithDeclination(declinationDegrees);
    }

    private static List<Vec3> ChooseFitPoints(IReadOnlyCollection<StructuralMeasurement> included, out string? error)
    {
        error = null;

        if (included.All(m => m.World is not null))
            return included.Select(m => m.World!.Value).ToList();

        // No full world placement → only valid if everything shares one origin station (local frame).
        var froms = included.Select(m => m.From).Distinct().ToList();
        if (froms.Count == 1)
            return included.Select(m => m.Local).ToList();

        error = "selected points span multiple stations with no common 3-D frame (survey not connected)";
        return new List<Vec3>();
    }
}
