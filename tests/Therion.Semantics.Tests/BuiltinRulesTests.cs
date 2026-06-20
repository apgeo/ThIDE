// M6 #6 — built-in semantic rule registrations.

using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Semantics.BuiltinRules;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class BuiltinRulesTests
{
    [Fact]
    public void OrphanFixedStation_reports_unreferenced_fix()
    {
        const string src = """
            survey s
              centreline
                fix orphan 0 0 0
                data normal from to length compass clino
                  a b 1.0 0 0
              endcentreline
            endsurvey
            """;
        var parse = new ThParser().Parse("(t.th)", src);
        var model = new SemanticBinder().Bind(parse.Value!);
        var ctx = new SemanticContext(model);
        var diags = new OrphanFixedStationRule().Run(ctx);

        Assert.Contains(diags, d => d.Code == SemanticDiagnosticCodes.OrphanFixedStation
            && d.Message.Contains("orphan"));
        // Stations 'a' and 'b' are shot-touched, not orphans.
        Assert.DoesNotContain(diags, d => d.Message.Contains("'s.a'") || d.Message.Contains("'s.b'"));
    }

    [Fact]
    public void OrphanFixedStation_silent_when_fix_referenced_by_shot()
    {
        const string src = """
            survey s
              centreline
                fix a 0 0 0
                data normal from to length compass clino
                  a b 1.0 0 0
              endcentreline
            endsurvey
            """;
        var parse = new ThParser().Parse("(t.th)", src);
        var model = new SemanticBinder().Bind(parse.Value!);
        var diags = new OrphanFixedStationRule().Run(new SemanticContext(model));
        Assert.Empty(diags);
    }
}
