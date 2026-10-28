using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

/// <summary>Invented: two georeferenced pieces that end a metre apart.</summary>
public class EquateCandidateFinderTests
{
    private static WorkspaceSemanticModel Model(string bFix)
    {
        string Piece(string name, string fix) =>
            $"survey {name}\n  centreline\n    cs UTM34N\n    fix 1 {fix}\n    data normal from to length compass clino\n    1 2 10 90 0\n  endcentreline\nendsurvey\n";
        var files = new Dictionary<string, string>
        {
            ["/c/a.th"] = Piece("a", "500000 5000000 100"),
            ["/c/b.th"] = Piece("b", bFix),
        };
        return WorkspaceSemanticModel.Build(files.ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value)), Array.Empty<XviFile>());
    }

    [Fact]
    public void Finds_the_close_cross_piece_pair()
    {
        var result = EquateCandidateFinder.Find(Model("500011 5000000 100"), 5);

        Assert.Equal(2, result.ComparablePieces);
        var c = Assert.Single(result.Candidates);
        Assert.Equal(("a.2", "b.1"), (c.A.Name.ToString(), c.B.Name.ToString()));
        Assert.Equal(1.0, c.Distance, 3);
    }

    [Fact]
    public void Far_apart_or_lat_long_pieces_give_nothing()
    {
        Assert.Empty(EquateCandidateFinder.Find(Model("600000 5000000 100"), 5).Candidates);
        Assert.Equal(1, EquateCandidateFinder.Find(Model("21.5 45.3 100"), 5).ComparablePieces);
    }
}
