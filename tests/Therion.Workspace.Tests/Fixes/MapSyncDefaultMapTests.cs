using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

/// <summary>
/// Which map an unreferenced scrap is offered for: a map in a file that inputs the scrap's sketch,
/// else the map sharing the longest id prefix (two characters at least), else the first map by id.
/// </summary>
public class MapSyncDefaultMapTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "map-sync-default");
    private static string P(string n) => Path.GetFullPath(Path.Combine(Root, n));

    private const string Sketch = "encoding utf-8\nscrap a_s1 -projection plan\nendscrap\nscrap Ab_s2 -projection plan\nendscrap\n";

    // Invented files: a.th inputs the sketch; main.th inputs a.th and holds the maps given.
    private static WorkspaceSemanticModel Build(string mainMaps, string aMaps = "", string? bInputsSketch = null)
    {
        var files = new Dictionary<string, string>
        {
            [P("main.th")] = "survey cave\n  input a.th\n" + (bInputsSketch is null ? "" : "  input b.th\n") + mainMaps + "endsurvey\n",
            [P("a.th")] = "survey a\n  input a.th2\n" + aMaps + "endsurvey\n",
            [P("a.th2")] = Sketch,
        };
        if (bInputsSketch is not null) files[P("b.th")] = "survey b\n  input a.th2\n" + bInputsSketch + "endsurvey\n";
        var parsed = files.ToDictionary(kv => kv.Key, kv => kv.Key.EndsWith(".th2") ? new Th2Parser().Parse(kv.Key, kv.Value) : new ThParser().Parse(kv.Key, kv.Value));
        return WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
    }

    private static string Map(string id) => $"  map {id}\n    a_s1@a\n  endmap\n";

    [Fact]
    public void No_maps_no_default()
    {
        Assert.Null(MapSync.DefaultMap(Build(""), "a_s1"));
    }

    [Fact]
    public void A_map_in_the_file_that_inputs_the_sketch_wins_over_a_better_prefix()
    {
        var ws = Build(mainMaps: Map("a_s1_plan"), aMaps: Map("zz") + Map("yy"));

        // Two maps in a.th: the first by ordinal id.
        Assert.Equal("yy", MapSync.DefaultMap(ws, "a_s1"));
    }

    [Fact]
    public void A_map_in_one_of_two_including_files_still_wins()
    {
        var ws = Build(mainMaps: Map("a_s1_plan"), aMaps: Map("zz"), bInputsSketch: "");

        Assert.Equal("zz", MapSync.DefaultMap(ws, "a_s1"));
    }

    [Fact]
    public void The_longest_shared_prefix_wins_ignoring_case()
    {
        var ws = Build(mainMaps: Map("a_x") + Map("ab_s") + Map("b"));

        Assert.Equal("ab_s", MapSync.DefaultMap(ws, "Ab_s2"));   // "ab_s" shares 4, "a_x" 1
        Assert.Equal("a_x", MapSync.DefaultMap(ws, "a_s1"));     // "a_x" shares 2, "ab_s" 1
    }

    [Fact]
    public void A_prefix_of_exactly_two_counts_and_a_tie_goes_to_the_first_id()
    {
        var ws = Build(mainMaps: Map("a_q") + Map("a_p") + Map("c"));

        Assert.Equal("a_p", MapSync.DefaultMap(ws, "a_s1"));
    }

    [Fact]
    public void A_map_id_that_is_a_prefix_of_the_scrap_id_counts_whole()
    {
        var ws = Build(mainMaps: Map("a_") + Map("b"));

        Assert.Equal("a_", MapSync.DefaultMap(ws, "a_s1"));
        Assert.Equal("a_", MapSync.DefaultMap(ws, "a_"));   // the scrap id runs out first
    }

    [Fact]
    public void One_shared_character_is_not_enough_and_the_first_map_by_id_is_offered()
    {
        var ws = Build(mainMaps: Map("c_plan") + Map("a") + Map("b_plan"));

        Assert.Equal("a", MapSync.DefaultMap(ws, "a_s1"));          // "a" shares 1 only: first by id
        Assert.Equal("a", MapSync.DefaultMap(ws, "unknown_scrap"));  // a scrap the model does not hold
    }
}
