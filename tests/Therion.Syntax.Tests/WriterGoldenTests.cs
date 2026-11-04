// golden / snapshot round-trip tests for TherionWriter.
//
// Rather than committing brittle golden-string files, these assert the writer's guarantees:
// (1) round-trip stability — writing a re-parsed AST is byte-identical to the first write
// (idempotent serialization); (2) fidelity — the first write reads back to the same tree, spans
// aside, so a dropped command cannot hide behind (1); and (3) the re-parsed output preserves
// structure and introduces no new errors.

using System.Linq;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class WriterGoldenTests
{
    private static string WriteTh(string src)
    {
        var parse = new ThParser().Parse("a.th", src);
        Assert.NotNull(parse.Value);
        return new TherionWriter().Write(parse.Value!);
    }

    private static string WriteTh2(string src)
    {
        var parse = new Th2Parser().Parse("a.th2", src);
        Assert.NotNull(parse.Value);
        return new TherionWriter().Write(parse.Value!);
    }

    [Fact]
    public void Survey_write_is_idempotent()
    {
        const string src = """
            survey cave -title "Cave"
              centreline
                units length meters
                calibrate compass 0.5
                data normal from to length compass clino
                  1 2 10.0 90 0
                  2 3 5.5 100 -5
                fix 1 100 200 300
                flags duplicate
                flags not duplicate
              endcentreline
            endsurvey
            """;

        var first = WriteTh(src);
        var second = WriteTh(first);   // re-parse + re-write the writer's own output
        Assert.Equal(first, second);   // stable serialization (golden)

        // Stability alone holds even when the first write drops lines, so the first write must also
        // keep every command and read back to the same tree (spans aside).
        Assert.Contains("units length meters", first);
        Assert.Contains("calibrate compass 0.5", first);
        Assert.Equal(
            Properties.AstShape.Dump(new ThParser().Parse("a.th", src).Value!.Children, spans: false),
            Properties.AstShape.Dump(new ThParser().Parse("a.th", first).Value!.Children, spans: false));
    }

    [Fact]
    public void Survey_roundtrip_preserves_structure_without_new_errors()
    {
        const string src = """
            survey a
              survey b
                centreline
                  data normal from to length compass clino
                    1 2 10 0 0
                endcentreline
              endsurvey
            endsurvey
            """;

        var written = WriteTh(src);
        var reparse = new ThParser().Parse("a.th", written);

        Assert.NotNull(reparse.Value);
        Assert.DoesNotContain(reparse.Diagnostics, d => d.Severity == Core.DiagnosticSeverity.Error);
        // Nested surveys survive the round-trip.
        var outer = Assert.Single(reparse.Value!.Children.OfType<SurveyCommand>());
        Assert.Single(outer.Children.OfType<SurveyCommand>());
    }

    [Fact]
    public void Th2_scrap_write_is_idempotent()
    {
        const string src = """
            scrap s1 -projection plan
              point 0 0 station -name 1
              line wall
                0 0
                1 1
              endline
            endscrap
            """;

        var first = WriteTh2(src);
        var second = WriteTh2(first);
        Assert.Equal(first, second);
        Assert.Equal(
            Properties.AstShape.Dump(new Th2Parser().Parse("a.th2", src).Value!.Children, spans: false),
            Properties.AstShape.Dump(new Th2Parser().Parse("a.th2", first).Value!.Children, spans: false));
    }
}
