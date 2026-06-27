// LANG-05 (extended) — unit tests for the per-reading data-value validator.

using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class DataReadingValidationTests
{
    [Theory]
    [InlineData("from", ReadingValueKind.Station)]
    [InlineData("to", ReadingValueKind.Station)]
    [InlineData("length", ReadingValueKind.Length)]
    [InlineData("backtape", ReadingValueKind.Length)]
    [InlineData("compass", ReadingValueKind.Bearing)]
    [InlineData("backbearing", ReadingValueKind.Bearing)]
    [InlineData("clino", ReadingValueKind.Clino)]
    [InlineData("gradient", ReadingValueKind.Clino)]
    [InlineData("depth", ReadingValueKind.Signed)]
    [InlineData("altitude", ReadingValueKind.Signed)]
    [InlineData("left", ReadingValueKind.Dimension)]
    [InlineData("ignore", ReadingValueKind.Ignore)]
    [InlineData("wibble", ReadingValueKind.None)]
    public void Classify_maps_readings_to_value_kinds(string reading, ReadingValueKind expected) =>
        Assert.Equal(expected, DataReadingValidation.Classify(reading));

    [Theory]
    [InlineData("length", "5.15x")]
    [InlineData("compass", "14x1.4")]
    [InlineData("clino", "49.7zxzxzx")]
    [InlineData("clino", "z")]
    [InlineData("depth", "abc")]
    public void Non_numeric_value_is_a_hard_error(string reading, string value)
    {
        var problem = DataReadingValidation.CheckValue(reading, value);
        Assert.True(problem is { IsError: true });
        Assert.Contains(value, problem!.Value.Message);
        Assert.Contains(reading, problem!.Value.Message);
    }

    [Theory]
    [InlineData("length", "4.85")]
    [InlineData("compass", "359.9")]
    [InlineData("compass", "0")]
    [InlineData("clino", "-90")]
    [InlineData("clino", "up")]        // vertical-plumb keyword
    [InlineData("clino", "down")]
    [InlineData("depth", "-3.25")]     // signed reading allows negatives
    [InlineData("left", "-")]          // omitted dimension
    [InlineData("backcompass", "-")]   // omitted back-reading
    [InlineData("from", "anything")]   // stations are never numeric-checked
    [InlineData("to", ".")]            // splay marker
    public void Acceptable_values_produce_no_problem(string reading, string value) =>
        Assert.Null(DataReadingValidation.CheckValue(reading, value));

    [Fact]
    public void Compass_out_of_range_in_degrees_is_a_range_warning()
    {
        var problem = DataReadingValidation.CheckValue("compass", "390");
        Assert.True(problem is { IsError: false });
    }

    [Fact]
    public void Compass_range_widens_with_grad_units()
    {
        Assert.Null(DataReadingValidation.CheckValue("compass", "390", compassUnit: AngleUnit.Grad));
        Assert.NotNull(DataReadingValidation.CheckValue("compass", "410", compassUnit: AngleUnit.Grad));
    }

    [Fact]
    public void Negative_length_is_a_warning_not_an_error()
    {
        var problem = DataReadingValidation.CheckValue("length", "-2");
        Assert.True(problem is { IsError: false });
    }
}
