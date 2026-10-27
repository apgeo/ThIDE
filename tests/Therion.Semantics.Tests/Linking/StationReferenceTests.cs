using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Linking;

namespace Therion.Semantics.Tests.Linking;

public class StationReferenceTests
{
    private static ImmutableArray<string> S(params string[] parts) => [.. parts];
    private static QualifiedName St(string point, params string[] surveys) => QualifiedName.OfStation([.. surveys], point);

    [Fact]
    public void Same_scope_is_the_bare_point()
    {
        Assert.Equal("7", StationReference.Format(St("7", "cave", "a"), S("cave", "a")));
    }

    [Fact]
    public void One_level_down()
    {
        Assert.Equal("7@a", StationReference.Format(St("7", "cave", "a"), S("cave")));
    }

    [Fact]
    public void Three_levels_down_are_written_bottom_up()
    {
        Assert.Equal("7@c.b.a", StationReference.Format(St("7", "cave", "a", "b", "c"), S("cave")));
    }

    [Fact]
    public void From_the_root()
    {
        Assert.Equal("7@a.cave", StationReference.Format(St("7", "cave", "a"), S()));
    }

    [Fact]
    public void Dotted_station_name_stays_whole()
    {
        Assert.Equal("N32.11@a", StationReference.Format(St("N32.11", "cave", "a"), S("cave")));
    }

    [Fact]
    public void Scope_that_is_not_an_ancestor_gives_null()
    {
        Assert.Null(StationReference.Format(St("7", "cave", "a"), S("cave", "b")));
        Assert.Null(StationReference.Format(St("7", "cave"), S("cave", "a")));
    }

    [Theory]
    [InlineData("7", "cave.a.7")]
    [InlineData("7@b", "cave.a.b.7")]
    [InlineData("N32.11@c.b", "cave.a.b.c.N32.11")]
    public void Resolve_inverts_format(string reference, string expected)
    {
        var resolved = StationReference.Resolve(reference, S("cave", "a"));

        Assert.Equal(expected, resolved.ToString());
        Assert.Equal(reference, StationReference.Format(resolved, S("cave", "a")));
    }
}
