using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Linking;

namespace Therion.Workspace.Tests.Linking;

public class LinkCatalogTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "link-catalog");
    private static string P(string name) => Path.GetFullPath(Path.Combine(Root, name));

    private static string Survey(string name, params int[] stations) =>
        $"survey {name}\n  centreline\n    data normal from to length compass clino\n" +
        string.Concat(stations.Zip(stations.Skip(1), (a, b) => $"    {a} {b} 5 0 0\n")) +
        "  endcentreline\nendsurvey\n";

    private static (WorkspaceSemanticModel, FileScopes) Build()
    {
        var files = new Dictionary<string, string>
        {
            [P("main.th")] = "survey cave\n  input b.th\n  input a.th\nendsurvey\n",
            [P("a.th")] = Survey("a", 1, 2, 10),
            [P("b.th")] = Survey("b", 3, 4),
        };
        var parsed = files.ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value), StringComparer.OrdinalIgnoreCase);
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        return (ws, SurveyScopeResolver.Build(ws, p => parsed.TryGetValue(p, out var r) ? r.Value : null));
    }

    [Fact]
    public void Surveys_are_listed_as_a_tree_with_effective_paths()
    {
        var (ws, scopes) = Build();

        var surveys = LinkCatalog.Surveys(ws, scopes);

        Assert.Equal(["cave", "cave.a", "cave.b"], surveys.Select(s => string.Join('.', s.Effective)));
        Assert.Equal([0, 1, 1], surveys.Select(s => s.Depth));
        Assert.Equal(P("a.th"), surveys[1].FilePath);
    }

    [Fact]
    public void Stations_sort_naturally_and_narrow_to_a_survey()
    {
        var (ws, scopes) = Build();

        var all = LinkCatalog.Stations(ws, scopes);
        var inA = LinkCatalog.Stations(ws, scopes, ImmutableArray.Create("cave", "a"));

        Assert.Equal(["cave.a.1", "cave.a.2", "cave.a.10", "cave.b.3", "cave.b.4"], all.Select(s => s.Effective.ToString()));
        Assert.Equal(["a.1", "a.2", "a.10"], inA.Select(s => s.Local.ToString()));
    }
}
