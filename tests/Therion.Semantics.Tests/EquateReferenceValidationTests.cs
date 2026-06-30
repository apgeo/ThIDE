// Cross-file equate validation: the per-file binder can't see other files, so it defers unresolved
// equate references to WorkspaceSemanticModel.ValidateEquateReferences, which resolves @-qualified
// cross-file targets (the grind project pattern) and only flags genuinely unresolved references.

using System.Collections.Generic;
using System;
using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class EquateReferenceValidationTests
{
    private static WorkspaceSemanticModel Build(string bEquate)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            ["/p/a.th"] = new ThParser().Parse("/p/a.th", """
                survey grind_baza_wg_temp
                  centreline
                    data normal from to length compass clino
                    BZWG0 BZWG1 10 0 0
                  endcentreline
                endsurvey
                """),
            ["/p/b.th"] = new ThParser().Parse("/p/b.th", $$"""
                survey grind_wg_deasupra_bivuac
                  centreline
                    data normal from to length compass clino
                    BVWG4 BVWG5 10 0 0
                  endcentreline
                  {{bEquate}}
                endsurvey
                """),
        };
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>(), _ => false);
    }

    [Fact]
    public void Valid_cross_file_at_equate_is_not_flagged()
    {
        var ws = Build("equate BZWG0@grind_baza_wg_temp BVWG4@grind_wg_deasupra_bivuac");
        var b = ws.PerFile["/p/b.th"];

        // The per-file binder couldn't resolve the foreign (and @-form local) targets…
        Assert.Contains(b.UnresolvedEquateRefs, r => r.Raw == "BZWG0@grind_baza_wg_temp");
        // …but the workspace resolves both, so no TH_SEM_001 is produced.
        Assert.Empty(ws.ValidateEquateReferences(b));
    }

    [Fact]
    public void Genuinely_unresolved_reference_is_flagged_by_the_workspace()
    {
        var ws = Build("equate BVWG4@grind_wg_deasupra_bivuac NOPE@grind_does_not_exist");
        var b = ws.PerFile["/p/b.th"];

        var diags = ws.ValidateEquateReferences(b);
        Assert.Contains(diags, d => d.Code.Value == SemanticDiagnosticCodes.UnresolvedStation
                                    && d.Message.Contains("NOPE@grind_does_not_exist"));
        Assert.DoesNotContain(diags, d => d.Message.Contains("BVWG4@grind_wg_deasupra_bivuac"));
    }
}
