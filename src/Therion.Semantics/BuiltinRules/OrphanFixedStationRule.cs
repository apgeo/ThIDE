// Implementation Plan §5.3 / M6 follow-up #6 — stock semantic rule.
//
// Reports stations introduced exclusively by a `fix` command but never
// referenced by any shot or equate. Such stations usually indicate a typo
// in shot rows or a leftover survey marker.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Semantics.BuiltinRules;

public sealed class OrphanFixedStationRule : ISemanticRule
{
    public string Id => SemanticDiagnosticCodes.OrphanFixedStation;

    public ImmutableArray<Diagnostic> Run(SemanticContext ctx)
    {
        var model = ctx.Model;
        if (model.Stations.Count == 0) return ImmutableArray<Diagnostic>.Empty;

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var s in model.Stations.Values)
        {
            if (s.Kind != StationDeclarationKind.Fix) continue;
            if (s.References.Length > 0) continue;
            // Was this station also the from/to of any shot? Implicit-shot decls
            // would have made Kind=Shot, but a Fix on a shot-touched station gets
            // promoted to Fix while References stays accurate.
            bool touchedByShot = false;
            foreach (var shot in model.Shots)
            {
                if (shot.From.Equals(s.Name) || shot.To.Equals(s.Name)) { touchedByShot = true; break; }
            }
            if (touchedByShot) continue;
            diags.Add(Diagnostic.Create(
                SemanticDiagnosticCodes.OrphanFixedStation,
                DiagnosticSeverity.Info,
                $"Fixed station '{s.Name}' is never referenced by a shot or equate.",
                s.DeclarationSpan));
        }
        return diags.ToImmutable();
    }
}
