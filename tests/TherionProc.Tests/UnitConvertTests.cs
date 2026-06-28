using Therion.Core;

namespace TherionProc.Tests;

// UTIL-04 — bidirectional unit conversions used by the converter palette.
public class UnitConvertTests
{
    private static readonly UnitConverter C = UnitConverter.Instance;

    [Fact]
    public void Metres_to_feet_and_back()
    {
        Assert.Equal(3.28084, C.ConvertLength(1, LengthUnit.Metre, LengthUnit.Foot), 4);
        Assert.Equal(1.0, C.ConvertLength(C.ConvertLength(1, LengthUnit.Metre, LengthUnit.Foot), LengthUnit.Foot, LengthUnit.Metre), 9);
    }

    [Fact]
    public void Grads_and_degrees()
    {
        Assert.Equal(90.0, C.ConvertAngle(100, AngleUnit.Grad, AngleUnit.Degree), 9);
        Assert.Equal(400.0, C.ConvertAngle(360, AngleUnit.Degree, AngleUnit.Grad), 6);
    }

    [Fact]
    public void Percent_slope_and_degrees()
    {
        // 45° == 100% slope.
        Assert.Equal(100.0, C.ConvertAngle(45, AngleUnit.Degree, AngleUnit.PercentSlope), 6);
        Assert.Equal(45.0, C.ConvertAngle(100, AngleUnit.PercentSlope, AngleUnit.Degree), 6);
    }

    [Fact]
    public void Mils_round_trip()
    {
        Assert.Equal(6400.0, C.ConvertAngle(360, AngleUnit.Degree, AngleUnit.Mil), 6);
        Assert.Equal(90.0, C.ConvertAngle(1600, AngleUnit.Mil, AngleUnit.Degree), 6);
    }
}
