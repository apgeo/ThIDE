// The Object Browser edits a shot's bound value (metres, degrees, calibration applied) and writes it
// back into the data row. What it writes must be the reading as the surveyor writes it: in the row's
// units and before `calibrate`, so that binding the edited file gives back the value typed.

using System;
using System.Globalization;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class ShotReadingsTests
{
    private static ShotSymbol Shot(string body)
    {
        var text = "survey s\n  centreline\n" + body + "  endcentreline\nendsurvey\n";
        var ast = new ThParser().Parse("/p/a.th", text).Value!;
        return new SemanticBinder().Bind(ast).Shots.Single();
    }

    private const string Header = """
            units length feet
            units compass grads
            units clino grads
            calibrate tape 1 2
            calibrate compass 4
            calibrate clino -2
            data normal from to length compass clino

        """;

    [Fact]
    public void The_bound_values_give_back_the_readings_as_written()
    {
        var shot = Shot(Header + "        1 2 6 104 22\n");

        Assert.Equal(6.0, ShotReadings.Reading(shot, "length", shot.Length!.Value)!.Value, 9);
        Assert.Equal(104.0, ShotReadings.Reading(shot, "compass", shot.Compass!.Value)!.Value, 9);
        Assert.Equal(22.0, ShotReadings.Reading(shot, "clino", shot.Clino!.Value)!.Value, 9);
    }

    [Theory]
    [InlineData("length", 12.5)]
    [InlineData("compass", 245.0)]
    [InlineData("clino", -31.0)]
    public void A_value_written_back_as_its_reading_binds_to_the_value_typed(string field, double typed)
    {
        var shot = Shot(Header + "        1 2 6 104 22\n");
        var reading = ShotReadings.Reading(shot, field, typed)!.Value;
        var row = field switch
        {
            "length" => $"        1 2 {F(reading)} 104 22\n",
            "compass" => $"        1 2 6 {F(reading)} 22\n",
            _ => $"        1 2 6 104 {F(reading)}\n",
        };

        var edited = Shot(Header + row);
        double bound = field switch
        {
            "length" => edited.Length!.Value,
            "compass" => edited.Compass!.Value,
            _ => edited.Clino!.Value,
        };
        Assert.Equal(typed, bound, 6);
    }

    private static string F(double v) => v.ToString("0.#########", CultureInfo.InvariantCulture);

    [Fact]
    public void A_units_factor_is_undone_too()
    {
        var shot = Shot("""
                units length 2 feet
                data normal from to length compass clino
                1 2 5 0 0

            """);

        // 5 × 2 ft = 3.048 m; 6.096 m is 10 readings.
        Assert.Equal(10.0, ShotReadings.Reading(shot, "tape", 6.096)!.Value, 9);
    }

    [Fact]
    public void A_compass_reading_stays_within_one_turn()
    {
        var shot = Shot("""
                calibrate compass 0.5
                data normal from to length compass clino
                1 2 5 0.2 0

            """);

        Assert.Equal(359.7, shot.Compass!.Value, 9);
        Assert.Equal(0.2, ShotReadings.Reading(shot, "compass", shot.Compass.Value)!.Value, 9);
        Assert.Equal(0.2, ShotReadings.Reading(shot, "bearing", 359.7)!.Value, 9);
    }

    [Fact]
    public void Without_units_or_calibration_the_reading_is_the_value()
    {
        var shot = Shot("""
                data normal from to length compass clino
                1 2 5 10 -3

            """);

        Assert.Equal(7.25, ShotReadings.Reading(shot, "length", 7.25));
        Assert.Equal(123.0, ShotReadings.Reading(shot, "compass", 123.0));
        Assert.Equal(-12.0, ShotReadings.Reading(shot, "clino", -12.0));
        Assert.Null(ShotReadings.Reading(shot, "from", 1.0));
    }
}
