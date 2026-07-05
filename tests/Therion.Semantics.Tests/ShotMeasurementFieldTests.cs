// Regression: shot distance/bearing/inclination must be read from Therion's field synonyms and
// backsight variants, not only the literal length/compass/clino. Missing these dropped ~a third of
// the legs on real projects (tape-based + backsight surveys), fragmenting the centreline layout and
// undercounting length statistics. See .claude/shot-field-synonyms-finding.md.

using System.Linq;
using Therion.Syntax;
using Xunit;

namespace Therion.Semantics.Tests;

public class ShotMeasurementFieldTests
{
    private static ShotSymbol BindOne(string src)
    {
        var parse = new ThParser().Parse("/t.th", src);
        var model = new SemanticBinder().Bind(parse.Value!);
        return model.Shots.Single();
    }

    [Fact]
    public void Tape_is_read_as_length()
    {
        // `tape` is Therion's synonym for `length`; forward compass/clino present.
        var shot = BindOne("""
            survey s
              centreline
                data normal from to compass tape clino
                0 1 90 12.5 -3
              endcentreline
            endsurvey
            """);
        Assert.Equal(12.5, shot.Length!.Value, 6);
        Assert.Equal(90.0, shot.Compass!.Value, 6);
        Assert.Equal(-3.0, shot.Clino!.Value, 6);
    }

    [Fact]
    public void Backsight_readings_are_reduced_to_foresight()
    {
        // Backsight-only shot: backcompass reduces by ±180°, backclino negates, backtape/tape is length.
        var shot = BindOne("""
            survey s
              centreline
                data normal to from backcompass tape backclino
                1 0 286 2.88 -21
              endcentreline
            endsurvey
            """);
        Assert.Equal(2.88, shot.Length!.Value, 6);   // tape -> length
        Assert.Equal(106.0, shot.Compass!.Value, 6); // backcompass 286 -> foresight 106
        Assert.Equal(21.0, shot.Clino!.Value, 6);    // backclino -21 -> foresight +21
    }

    [Fact]
    public void Bearing_and_gradient_synonyms_are_recognized()
    {
        var shot = BindOne("""
            survey s
              centreline
                data normal from to length bearing gradient
                0 1 10 45 8
              endcentreline
            endsurvey
            """);
        Assert.Equal(10.0, shot.Length!.Value, 6);
        Assert.Equal(45.0, shot.Compass!.Value, 6);
        Assert.Equal(8.0, shot.Clino!.Value, 6);
    }

    [Fact]
    public void Foresight_wins_when_both_foresight_and_backsight_present()
    {
        var shot = BindOne("""
            survey s
              centreline
                data normal from to length compass backcompass clino backclino
                0 1 10 90 271 5 -6
              endcentreline
            endsurvey
            """);
        Assert.Equal(90.0, shot.Compass!.Value, 6);  // forward compass, not the backsight
        Assert.Equal(5.0, shot.Clino!.Value, 6);
    }
}
