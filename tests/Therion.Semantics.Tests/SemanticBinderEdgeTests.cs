// Binder edge cases: backsight-only readings in grads, which `cs` a file declares, empty comments,
// the "did you mean" suggestion for an unresolved equate member and its distance threshold, binding
// without occurrences, and a scrap id declared twice. Every survey here is invented.

using System.Collections.Immutable;
using System.Linq;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SemanticBinderEdgeTests
{
    private static SemanticModel Bind(string text, bool buildOccurrences = true) =>
        new SemanticBinder().Bind(new ThParser().Parse("/p/edge.th", text).Value, buildOccurrences);

    private static string Survey(params string[] lines) =>
        "survey s\n  centreline\n" + string.Concat(lines.Select(l => "    " + l + "\n")) +
        "  endcentreline\nendsurvey\n";

    // ---- backsight-only readings ---------------------------------------------------------------

    [Fact]
    public void Backbearing_alone_is_turned_round_in_degrees_whatever_the_compass_units()
    {
        // 300 grad = 270 deg; the foresight equivalent is 90 deg.
        var shot = Assert.Single(Bind(Survey(
            "units compass grads",
            "data normal from to length backbearing clino",
            "1 2 10 300 0")).Shots);
        Assert.Equal(90.0, shot.Compass!.Value, 9);
    }

    [Fact]
    public void Backclino_alone_is_negated_and_converted()
    {
        // 50 grad = 45 deg up as a backsight, so the leg goes 45 deg down.
        var shot = Assert.Single(Bind(Survey(
            "units clino grads",
            "data normal from to length compass backclino",
            "1 2 10 0 50")).Shots);
        Assert.Equal(-45.0, shot.Clino!.Value, 9);
    }

    [Fact]
    public void Foresight_wins_over_backsight_when_both_are_read()
    {
        var shot = Assert.Single(Bind(Survey(
            "data normal from to length compass backcompass clino backclino",
            "1 2 10 10 195 5 -8")).Shots);
        Assert.Equal(10.0, shot.Compass!.Value, 9);
        Assert.Equal(5.0, shot.Clino!.Value, 9);
    }

    // ---- coordinate system ---------------------------------------------------------------------

    [Fact]
    public void First_cs_is_the_input_system_and_each_fix_takes_the_cs_in_force()
    {
        var model = Bind(Survey(
            "cs UTM33",
            "fix 1 400000 5000000 100",
            "cs EPSG:32634",
            "fix 2 500000 5000000 200",
            "data normal from to length compass clino",
            "1 2 10 0 0"));
        Assert.Equal("UTM33", model.InputCoordinateSystem);
        Assert.Equal("UTM33", model.Stations[QualifiedName.Parse("s.1")].Cs);
        Assert.Equal("EPSG:32634", model.Stations[QualifiedName.Parse("s.2")].Cs);
    }

    [Fact]
    public void A_file_without_cs_has_no_input_system()
    {
        Assert.Null(Bind(Survey("data normal from to length compass clino", "1 2 10 0 0")).InputCoordinateSystem);
    }

    // ---- comments ------------------------------------------------------------------------------

    [Fact]
    public void Shot_comments_are_trimmed_and_an_empty_one_is_none()
    {
        var shots = Bind(Survey(
            "data normal from to length compass clino",
            "1 2 10 0 0 #",
            "2 3 10 0 0 #    ",
            "3 4 10 0 0 #  squeeze  ",
            "# above the row",
            "4 5 10 0 0 # and after it",
            "# not adjacent",
            "",
            "5 6 10 0 0",
            "#",
            "6 7 10 0 0")).Shots;
        Assert.Equal(new string?[] { null, null, "squeeze", "above the row | and after it", null, null },
            shots.Select(s => s.Comment).ToArray());
    }

    // ---- unresolved equate suggestion ----------------------------------------------------------

    private static string? HintFor(string text, string raw) =>
        Assert.Single(Bind(text).UnresolvedEquateRefs, r => r.Raw == raw).Hint;

    [Fact]
    public void Close_name_in_the_same_survey_is_suggested_by_its_bare_name()
    {
        var text = Survey("data normal from to length compass clino", "a1 b7 10 0 0", "equate b7 a2");
        Assert.Equal("did you mean 'a1'?", HintFor(text, "a2"));
    }

    [Fact]
    public void Close_name_outside_any_survey_is_suggested()
    {
        var text = "centreline\n  data normal from to length compass clino\n  a1 b7 10 0 0\n  equate b7 a2\nendcentreline\n";
        Assert.Equal("did you mean 'a1'?", HintFor(text, "a2"));
    }

    [Fact]
    public void Far_name_gets_no_suggestion()
    {
        var text = Survey("data normal from to length compass clino", "a1 b7 10 0 0", "equate b7 zzzz");
        Assert.Null(HintFor(text, "zzzz"));
    }

    [Theory]
    [InlineData("abcdefghi", "abcdefxyz", true)]    // 9 characters allow a distance of 3
    [InlineData("abcdefghi", "abcdewxyz", false)]   // distance 4
    [InlineData("abc", "xyc", true)]                // short names always allow 2
    [InlineData("abc", "xyz", false)]
    public void Suggestion_threshold_is_a_third_of_the_name_but_at_least_two(string raw, string station, bool suggested)
    {
        var text = Survey("data normal from to length compass clino", $"{station} q 10 0 0", $"equate q {raw}");
        Assert.Equal(suggested ? $"did you mean '{station}'?" : null, HintFor(text, raw));
    }

    [Fact]
    public void Standalone_diagnostic_carries_the_suggestion()
    {
        var model = Bind(Survey("data normal from to length compass clino", "a1 b7 10 0 0", "equate b7 a2"));
        var d = Assert.Single(model.UnresolvedEquateDiagnostics());
        Assert.Equal("Unresolved station reference 'a2'.", d.Message);
        Assert.Equal("did you mean 'a1'?", d.Hint);
    }

    // ---- occurrences and scraps ----------------------------------------------------------------

    [Fact]
    public void Binding_without_occurrences_keeps_the_model_and_drops_only_the_index()
    {
        var text = Survey("data normal from to length compass clino", "1 2 10 0 0", "2 3 10 0 0", "equate 3 1");
        var full = Bind(text);
        var lean = Bind(text, buildOccurrences: false);
        Assert.NotEmpty(full.Occurrences.All);
        Assert.Empty(lean.Occurrences.All);
        Assert.Equal(full.Stations.Keys.OrderBy(k => k.ToString()), lean.Stations.Keys.OrderBy(k => k.ToString()));
        Assert.Equal(full.Shots.Length, lean.Shots.Length);
        Assert.Equal(full.Equates.Find(QualifiedName.Parse("s.3")), lean.Equates.Find(QualifiedName.Parse("s.3")));
    }

    [Fact]
    public void Scrap_id_declared_twice_keeps_the_first_declaration()
    {
        var th2 = new Th2Parser().Parse("/p/edge.th2", "scrap s1\nendscrap\nscrap s1 -scale [0 0 1 1 0 0 1 1 m]\nendscrap\nscrap s2\nendscrap\n");
        var model = new SemanticBinder().Bind(th2.Value);
        Assert.Equal(new[] { "s1", "s2" }, model.Scraps.Keys.OrderBy(k => k, System.StringComparer.Ordinal));
        Assert.Equal(1, model.Scraps["s1"].DeclarationSpan.Start.Line);
        Assert.Equal(5, model.Scraps["s2"].DeclarationSpan.Start.Line);
    }
}
