// Verifies the binder folds stateful `flags` commands into each shot and
// attaches leading/inline comments — the data behind the Measurements view.

using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ShotFlagsAndCommentsTests
{
    private const string Src =
        "survey demo\n" +
        "  centreline\n" +
        "    data normal from to length compass clino\n" +
        "      # leading note\n" +
        "      0 1 12.5 0 -5    # trailing note\n" +
        "    flags surface\n" +
        "      1 2 8.0 90 0\n" +
        "    flags duplicate\n" +
        "      2 3 4.0 180 10\n" +
        "    flags not surface\n" +
        "      3 4 2.0 270 -20\n" +
        "  endcentreline\n" +
        "endsurvey\n";

    private static SemanticModel Bind()
    {
        var parse = new ThParser().Parse("demo.th", Src);
        return new SemanticBinder().Bind(parse.Value!);
    }

    [Fact]
    public void Flags_are_stateful_across_shots()
    {
        var shots = Bind().Shots;

        Assert.Equal(4, shots.Length);
        Assert.Equal(ShotFlags.None, shots[0].Flags);
        Assert.Equal(ShotFlags.Surface, shots[1].Flags);
        Assert.Equal(ShotFlags.Surface | ShotFlags.Duplicate, shots[2].Flags);
        // `flags not surface` clears Surface but leaves Duplicate in force.
        Assert.Equal(ShotFlags.Duplicate, shots[3].Flags);
    }

    [Fact]
    public void Leading_and_trailing_comments_are_combined_on_the_shot()
    {
        var shots = Bind().Shots;

        Assert.Equal("leading note | trailing note", shots[0].Comment);
        Assert.Null(shots[1].Comment);
    }
}
