// Project diagnostics with exact numbers: the fore/back wrap and tolerance edges, the loop misclosure
// along a tree path that is walked backwards through a vertical leg, and the wording of the messages
// that carry those numbers. Every survey here is invented.

using System;
using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ProjectDiagnosticsNumericsTests
{
    private static WorkspaceSemanticModel Ws(params (string Path, string Text)[] files)
    {
        var dict = new Dictionary<string, ParseResult<TherionFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, text) in files)
            dict[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(dict, Array.Empty<XviFile>());
    }

    private static Diagnostic[] Of(WorkspaceSemanticModel ws, string code, ProjectDiagnosticOptions? o = null) =>
        ProjectDiagnostics.Analyze(ws, o).Where(d => d.Code == code).ToArray();

    private static WorkspaceSemanticModel Rows(string data, params string[] rows) =>
        Ws(("/p/a.th", "survey s\n  centreline\n    " + data + "\n" +
                       string.Concat(rows.Select(r => "    " + r + "\n")) +
                       "  endcentreline\nendsurvey\n"));

    private const string CompassStyle = "data normal from to length compass backcompass clino";
    private const string ClinoStyle = "data normal from to length compass clino backclino";

    // ---- fore/back compass --------------------------------------------------------------------

    [Theory]
    [InlineData("180", "1")]      // expected back 0 = 360: 1 degree off across the wrap
    [InlineData("0", "182.5")]    // exactly on the 2.5 degree tolerance
    [InlineData("0", "177.5")]
    [InlineData("359", "179")]
    public void Compass_backsight_within_tolerance_is_not_flagged(string fore, string back)
    {
        var d = Of(Rows(CompassStyle, $"1 2 10 {fore} {back} 0"), SemanticDiagnosticCodes.ForeBackMismatch);
        Assert.True(d.Length == 0, string.Join("; ", d.Select(x => x.Message)));
    }

    [Theory]
    [InlineData("0", "182.6")]    // just over the tolerance
    [InlineData("350", "175")]    // expected 170 across the wrap: 5 degrees off
    [InlineData("10", "10")]      // back read as a second foresight
    public void Compass_backsight_beyond_tolerance_is_flagged(string fore, string back)
    {
        Assert.Single(Of(Rows(CompassStyle, $"1 2 10 {fore} {back} 0"), SemanticDiagnosticCodes.ForeBackMismatch));
    }

    [Fact]
    public void Compass_mismatch_message_holds_the_difference_and_the_expected_back()
    {
        var d = Assert.Single(Of(Rows(CompassStyle, "1 2 10 350 175 0"), SemanticDiagnosticCodes.ForeBackMismatch));
        Assert.Equal(
            "Foresight/backsight compass disagree by 5° (fore 350°, back 175°, expected ~170°).",
            d.Message);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Compass_tolerance_is_taken_from_the_options()
    {
        var ws = Rows(CompassStyle, "1 2 10 0 184 0");
        Assert.Single(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch));
        Assert.Empty(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch,
            new ProjectDiagnosticOptions { ForeBackCompassToleranceDeg = 5 }));
    }

    // ---- fore/back clino ----------------------------------------------------------------------

    [Theory]
    [InlineData("10", "-10", false)]
    [InlineData("10", "-12.5", false)]   // exactly on the tolerance
    [InlineData("10", "-12.6", true)]
    [InlineData("10", "-7", true)]
    [InlineData("10", "10", true)]       // back written with the foresight's sign
    [InlineData("-90", "90", false)]
    public void Clino_backsight_is_checked_against_the_negated_foresight(string fore, string back, bool flagged)
    {
        var d = Of(Rows(ClinoStyle, $"1 2 10 0 {fore} {back}"), SemanticDiagnosticCodes.ForeBackMismatch);
        Assert.Equal(flagged ? 1 : 0, d.Length);
    }

    [Fact]
    public void Clino_mismatch_message_shows_signed_angles()
    {
        // A clino is a signed angle; it must not be printed as a bearing (−7° is not 353°).
        var d = Assert.Single(Of(Rows(ClinoStyle, "1 2 10 0 10 -7"), SemanticDiagnosticCodes.ForeBackMismatch));
        Assert.Equal("Foresight/backsight clino disagree by 3° (fore 10°, back -7°, expected ~-10°).", d.Message);
    }

    [Fact]
    public void Clino_tolerance_is_taken_from_the_options()
    {
        var ws = Rows(ClinoStyle, "1 2 10 0 10 -14");
        Assert.Single(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch));
        Assert.Empty(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch,
            new ProjectDiagnosticOptions { ForeBackClinoToleranceDeg = 4.5 }));
    }

    [Theory]
    [InlineData("grads", "50", "-50", false)]     // 45 degrees both ways
    [InlineData("grads", "50", "-40", true)]      // 9 degrees off
    [InlineData("percent", "100", "-100", false)] // 45 degrees both ways
    [InlineData("percent", "100", "-90", true)]   // 45 vs 41.99 degrees
    public void Clino_pair_is_compared_in_degrees_whatever_the_units(string unit, string fore, string back, bool flagged)
    {
        var d = Of(Rows($"units clino {unit}\n    {ClinoStyle}", $"1 2 10 0 {fore} {back}"),
            SemanticDiagnosticCodes.ForeBackMismatch);
        Assert.Equal(flagged ? 1 : 0, d.Length);
    }

    [Fact]
    public void Grads_mismatch_is_reported_in_degrees()
    {
        // fore 100 grad = 90 deg, back 280 grad = 252 deg, expected 270 deg.
        var ws = Rows($"units compass grads\n    {CompassStyle}", "1 2 10 100 280 0");
        var d = Assert.Single(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch));
        Assert.Equal("Foresight/backsight compass disagree by 18° (fore 90°, back 252°, expected ~270°).", d.Message);
    }

    [Fact]
    public void Splays_are_not_checked_for_backsights_or_blunders()
    {
        var ws = Rows("data normal from to length compass backcompass clino",
            "1 2 10 0 180 0", "2 - 0 0 90 0", "2 - 400 0 90 0");
        Assert.Empty(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch));
        Assert.Empty(Of(ws, SemanticDiagnosticCodes.ShotOutlier));
    }

    // ---- blunders ------------------------------------------------------------------------------

    [Fact]
    public void Long_leg_threshold_is_strict_and_the_message_holds_the_length()
    {
        var ws = Rows("data normal from to length compass clino", "1 2 300 0 0", "2 3 300.5 0 0");
        var d = Assert.Single(Of(ws, SemanticDiagnosticCodes.ShotOutlier));
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
        Assert.Equal("Unusually long leg (300.5 m) s.2 → s.3; check for a transcription error.", d.Message);
    }

    [Fact]
    public void Zero_length_threshold_includes_a_tenth_of_a_millimetre()
    {
        var ws = Rows("data normal from to length compass clino", "1 2 0.0001 0 0", "2 3 0.0002 0 0");
        var d = Assert.Single(Of(ws, SemanticDiagnosticCodes.ShotOutlier));
        Assert.Equal("Zero-length leg between distinct stations s.1 → s.2.", d.Message);
    }

    [Fact]
    public void Each_check_can_be_switched_off_on_its_own()
    {
        var ws = Rows("data normal from to length compass backcompass clino", "1 2 0 0 90 0");
        Assert.Single(Of(ws, SemanticDiagnosticCodes.ShotOutlier));
        Assert.Single(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch));

        var noBlunders = new ProjectDiagnosticOptions { EnableBlunders = false };
        Assert.Empty(Of(ws, SemanticDiagnosticCodes.ShotOutlier, noBlunders));
        Assert.Single(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch, noBlunders));

        var noForeBack = new ProjectDiagnosticOptions { EnableForeBack = false };
        Assert.Single(Of(ws, SemanticDiagnosticCodes.ShotOutlier, noForeBack));
        Assert.Empty(Of(ws, SemanticDiagnosticCodes.ForeBackMismatch, noForeBack));
    }

    [Fact]
    public void Every_check_is_on_by_default()
    {
        var o = ProjectDiagnosticOptions.Default;
        Assert.True(o.EnableLoopClosure && o.EnableBlunders && o.EnableForeBack && o.EnableDuplicates
                    && o.EnableDangling && o.EnableDisconnection);
        Assert.False(o.LocalFixGrounds);
    }

    // ---- loop closure --------------------------------------------------------------------------

    [Fact]
    public void Triangle_misclosure_message_holds_error_loop_length_and_percentage()
    {
        // 1 at the origin, 2 at N 10, 3 at (3.536, 3.536) reached backwards from the 3→1 leg.
        // The 2→3 leg predicts (10, 10): misclosure 9.14 m over 10 + 10 + 5 = 25 m.
        var ws = Rows("data normal from to length compass clino", "1 2 10 0 0", "2 3 10 90 0", "3 1 5 225 0");
        var d = Assert.Single(Of(ws, SemanticDiagnosticCodes.LoopMisclosure));
        Assert.Equal(
            "Loop misclosure 9.14 m over a 25 m loop (36.6% error) — check this loop for a blunder.",
            d.Message);
    }

    [Fact]
    public void Loop_through_a_vertical_leg_walked_backwards_has_the_exact_misclosure()
    {
        // 1→2 north 10. 3→2 is a 5 m plumb up, walked from 2 to 3: 3 sits 5 m below 2 at (0, 10, -5).
        // The closing leg 1→3 (10 m, north, -30°) predicts (0, 8.660, -5): misclosure 1.34 m over 25 m.
        var ws = Rows("data normal from to length compass clino", "1 2 10 0 0", "3 2 5 0 90", "1 3 10 0 -30");
        var d = Assert.Single(Of(ws, SemanticDiagnosticCodes.LoopMisclosure));
        Assert.Equal(
            "Loop misclosure 1.34 m over a 25 m loop (5.4% error) — check this loop for a blunder.",
            d.Message);
    }

    [Fact]
    public void Loop_that_closes_through_a_vertical_leg_is_not_flagged()
    {
        // Same geometry with a closing leg that lands exactly on 3: sqrt(125) m at -26.565°.
        var ws = Rows("data normal from to length compass clino",
            "1 2 10 0 0", "3 2 5 0 90", "1 3 11.18034 0 -26.56505");
        Assert.Empty(Of(ws, SemanticDiagnosticCodes.LoopMisclosure));
    }

    [Fact]
    public void Misclosure_below_the_floor_or_the_fraction_is_not_flagged()
    {
        // 0.4 m error over 20.4 m: 2% and under the 0.5 m floor.
        var small = Rows("data normal from to length compass clino", "1 2 10 0 0", "2 1 10.4 180 0");
        Assert.Empty(Of(small, SemanticDiagnosticCodes.LoopMisclosure));
        // 0.6 m over 20.6 m: above the floor but 2.9% < 4%.
        var lowFraction = Rows("data normal from to length compass clino", "1 2 10 0 0", "2 1 10.6 180 0");
        Assert.Empty(Of(lowFraction, SemanticDiagnosticCodes.LoopMisclosure));
        // 1 m over 21 m = 4.8%: flagged.
        var flagged = Rows("data normal from to length compass clino", "1 2 10 0 0", "2 1 11 180 0");
        Assert.Single(Of(flagged, SemanticDiagnosticCodes.LoopMisclosure));
    }

    // ---- naming collisions and disconnected pieces ---------------------------------------------

    [Fact]
    public void Survey_and_map_collision_messages_name_the_kind()
    {
        var ws = Ws(
            ("/p/a.th", "survey cave\n  map m1\n  endmap\nendsurvey\n"),
            ("/p/b.th", "survey cave\n  map m1\n  endmap\nendsurvey\n"));
        var messages = Of(ws, SemanticDiagnosticCodes.DuplicateDeclaration).Select(d => d.Message).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "Naming collision: map 'm1' is declared in 2 files; map names should be unique across the project.",
            "Naming collision: survey 'cave' is declared in 2 files; survey names should be unique across the project.",
        }, messages);
    }

    private const string MainChain = """
        survey a
          centreline
            data normal from to length compass clino
            1 2 5 0 0
            2 3 5 0 0
            3 4 5 0 0
            4 5 5 0 0
            5 6 5 0 0
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Disconnected_message_counts_legs_with_singular_and_plural()
    {
        var ws = Ws(("/p/a.th", MainChain + """

            survey b
              centreline
                data normal from to length compass clino
                10 11 5 0 0
              endcentreline
            endsurvey
            survey c
              centreline
                data normal from to length compass clino
                20 21 5 0 0
                21 22 5 0 0
              endcentreline
            endsurvey
            """));
        var messages = Of(ws, SemanticDiagnosticCodes.DisconnectedSurvey).Select(d => d.Message).ToArray();
        Assert.Equal(2, messages.Length);
        // Largest detached piece first.
        Assert.StartsWith("Disconnected survey: this piece (3 stations, 2 legs; a.th) is not connected", messages[0]);
        Assert.Contains("It runs from station 'c.20' to 'c.22'.", messages[0]);
        Assert.StartsWith("Disconnected survey: this piece (2 stations, 1 leg; a.th) is not connected", messages[1]);
        Assert.EndsWith("Join it with an 'equate' to an adjacent station, or anchor it with a 'fix' under a 'cs'.", messages[1]);
    }

    [Fact]
    public void Disconnected_report_is_capped_by_the_options()
    {
        var ws = Ws(("/p/a.th", MainChain + """

            survey b
              centreline
                data normal from to length compass clino
                10 11 5 0 0
              endcentreline
            endsurvey
            survey c
              centreline
                data normal from to length compass clino
                20 21 5 0 0
                21 22 5 0 0
              endcentreline
            endsurvey
            """));
        var d = Assert.Single(Of(ws, SemanticDiagnosticCodes.DisconnectedSurvey,
            new ProjectDiagnosticOptions { MaxDisconnectedReported = 1 }));
        Assert.Contains("(3 stations, 2 legs;", d.Message);
    }

    [Fact]
    public void Disconnected_piece_over_many_files_lists_four_and_counts_the_rest()
    {
        // A detached chain b1.2 = b2.1, b2.2 = b3.1 … over five files, joined by @-equates.
        var files = new List<(string, string)> { ("/p/a.th", MainChain + "\n") };
        for (int i = 1; i <= 5; i++)
            files.Add(($"/p/b{i}.th",
                $"survey b{i}\n  centreline\n    data normal from to length compass clino\n    1 2 5 0 0\n  endcentreline\nendsurvey\n"));
        files.Add(("/p/link.th", "equate 2@b1 1@b2\nequate 2@b2 1@b3\nequate 2@b3 1@b4\nequate 2@b4 1@b5\n"));
        var d = Assert.Single(Of(Ws(files.ToArray()), SemanticDiagnosticCodes.DisconnectedSurvey));
        Assert.Contains("; b1.th, b2.th, b3.th, b4.th, +1 more)", d.Message);
    }
}
