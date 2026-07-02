// Known-good seed sources for the mutation harness. Each must parse with ZERO
// diagnostics (guarded by MutationTests.Seeds_parse_clean) so that any diagnostic
// on a mutant is attributable to the mutation alone. Keep seeds small but
// representative; new file types get a new entry here + catalog cases only.

using System.Collections.Generic;

namespace Therion.Syntax.Tests.Mutation;

public static class MutationSeeds
{
    public const string Th = """
        survey main -title "Mutation seed"
          centreline
            team "John Doe"
            date 2020.01.02
            units length metres
            calibrate compass 0.5
            data normal from to length compass clino
            1 2 5.2 100 -5
            2 3 4.1 200 10
            fix 1 500 600 700
            station 2 "junction" continuation
            mark 2 fixed
            flags duplicate
            extend left
          endcentreline
        endsurvey
        """;

    public const string Th2 = """
        scrap s1 -projection plan
          point 100 200 station -name 1
          point 150 250 label -text "Entrance"
          line wall
            300 400
            310 410
          endline
        endscrap
        """;

    public const string Thconfig = """
        source main.th
        layout base
          scale 1 500
        endlayout
        select main
        export map -fmt pdf -o out.pdf
        export model -fmt loch
        """;

    /// <summary>Seed lookup by id; the id doubles as the routing extension's stem.</summary>
    public static readonly IReadOnlyDictionary<string, (string Extension, string Text)> ById =
        new Dictionary<string, (string, string)>
        {
            ["th"] = (".th", Th),
            ["th2"] = (".th2", Th2),
            ["thconfig"] = (".thconfig", Thconfig),
        };
}
