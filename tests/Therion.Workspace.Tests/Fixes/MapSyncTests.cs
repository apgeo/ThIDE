using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;

namespace Therion.Workspace.Tests.Fixes;

public class MapSyncTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "map-sync");
    private static string P(string n) => Path.GetFullPath(Path.Combine(Root, n));

    private static (WorkspaceSemanticModel, FileScopes, Dictionary<string, string>, Func<string, TherionFile?>) Build(string main)
    {
        var files = new Dictionary<string, string>
        {
            [P("main.th")] = main,
            [P("a.th")] = "survey a\n  input a.th2\nendsurvey\n",
            [P("a.th2")] = "encoding utf-8\nscrap a_s1 -projection plan\nendscrap\nscrap a_s2 -projection plan\nendscrap\n",
        };
        var parsed = files.ToDictionary(kv => kv.Key, kv => kv.Key.EndsWith(".th2") ? new Th2Parser().Parse(kv.Key, kv.Value) : new ThParser().Parse(kv.Key, kv.Value));
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        TherionFile? Ast(string p) => parsed.TryGetValue(p, out var r) ? r.Value : null;
        return (ws, SurveyScopeResolver.Build(ws, Ast), files, Ast);
    }

    [Fact]
    public void Adds_scraps_named_from_the_maps_survey_after_the_last_member()
    {
        var (ws, scopes, files, ast) = Build("survey cave\n  input a.th\n  map plan\n    a_s1@a\n  endmap\nendsurvey\n");

        Assert.Equal(["a_s2"], ProjectStatistics.UnreferencedScraps(ws));
        Assert.Equal("plan", MapSync.DefaultMap(ws, "a_s2"));
        var (batch, problems) = MapSync.Plan(ws, scopes, new Dictionary<string, string?> { ["a_s2"] = "plan" }, files.GetValueOrDefault, ast);

        Assert.Empty(problems);
        Assert.Equal("survey cave\n  input a.th\n  map plan\n    a_s1@a\n    a_s2@a\n  endmap\nendsurvey\n",
            TextSplicer.Splice(files[P("main.th")], batch.Edits.Single().Edits));
    }

    [Fact]
    public void Skipped_scraps_and_unterminated_maps()
    {
        var (ws, scopes, files, ast) = Build("survey cave\n  input a.th\n  map plan\n    a_s1@a\nendsurvey\n");

        var (batch, problems) = MapSync.Plan(ws, scopes, new Dictionary<string, string?> { ["a_s2"] = "plan", ["a_s1"] = null }, files.GetValueOrDefault, ast);

        Assert.Empty(batch.Edits);
        Assert.Equal([("plan", MapEditProblem.Unterminated)], problems);
    }
}
