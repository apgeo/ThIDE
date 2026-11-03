// A `group … endgroup` block inherits the active flags and units (Therion saves the state at
// `group` and restores it at `endgroup`); rows inside it must not bind with defaults.

using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class GroupInheritsActiveStateTests
{
    private static SemanticModel Bind(string src) =>
        new SemanticBinder().Bind(new ThParser().Parse("g.th", src).Value!);

    [Fact]
    public void Rows_inside_a_group_keep_the_flags_set_before_it()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                flags surface
                group
                  1 2 8.0 90 0
                endgroup
                flags not surface
              endcentreline
            endsurvey
            """);

        var shot = Assert.Single(model.Shots);
        Assert.Equal(ShotFlags.Surface, shot.Flags);
    }

    [Fact]
    public void Rows_inside_a_group_keep_the_length_units_set_before_it()
    {
        var model = Bind("""
            survey s
              centreline
                data normal from to length compass clino
                units length feet
                group
                  1 2 10 90 0
                endgroup
              endcentreline
            endsurvey
            """);

        var shot = Assert.Single(model.Shots);
        Assert.NotNull(shot.Length);
        Assert.Equal(3.048, shot.Length!.Value, 3);
    }
}
