// Implementation Plan �8.1 (Units: canonicalize to metres, preserve original).

namespace Therion.Core;

/// <summary>
/// Therion length units. The canonical SI unit is <see cref="Metre"/>.
/// AST nodes preserve the originally parsed unit; semantic layer computes
/// canonical values via <see cref="IUnitConverter"/>.
/// </summary>
public enum LengthUnit
{
    Metre,
    Centimetre,
    Millimetre,
    Kilometre,
    Inch,
    Foot,
    Yard,
}

/// <summary>Therion angle units. Canonical SI unit is <see cref="Degree"/>.</summary>
public enum AngleUnit
{
    Degree,
    Grad,
    Mil,
    Minute,
    PercentSlope,
}

/// <summary>A length value with the original unit it was parsed in.</summary>
public readonly record struct Length(double Value, LengthUnit Unit)
{
    public override string ToString() => $"{Value} {Unit}";
}

/// <summary>An angle value with the original unit it was parsed in.</summary>
public readonly record struct Angle(double Value, AngleUnit Unit)
{
    public override string ToString() => $"{Value} {Unit}";
}

/// <summary>Converts <see cref="Length"/> / <see cref="Angle"/> to canonical SI values.</summary>
public interface IUnitConverter
{
    /// <summary>Convert a length to canonical metres.</summary>
    double ToMetres(Length length);

    /// <summary>Convert an angle to canonical degrees.</summary>
    double ToDegrees(Angle angle);
}

/// <summary>Default <see cref="IUnitConverter"/> implementation.</summary>
public sealed class UnitConverter : IUnitConverter
{
    public static UnitConverter Instance { get; } = new();

    public double ToMetres(Length length) => length.Unit switch
    {
        LengthUnit.Metre => length.Value,
        LengthUnit.Centimetre => length.Value * 0.01,
        LengthUnit.Millimetre => length.Value * 0.001,
        LengthUnit.Kilometre => length.Value * 1000.0,
        LengthUnit.Inch => length.Value * 0.0254,
        LengthUnit.Foot => length.Value * 0.3048,
        LengthUnit.Yard => length.Value * 0.9144,
        _ => throw new ArgumentOutOfRangeException(nameof(length)),
    };

    public double ToDegrees(Angle angle) => angle.Unit switch
    {
        AngleUnit.Degree => angle.Value,
        AngleUnit.Grad => angle.Value * 0.9,
        AngleUnit.Mil => angle.Value * 360.0 / 6400.0,
        AngleUnit.Minute => angle.Value / 60.0,
        AngleUnit.PercentSlope => Math.Atan(angle.Value / 100.0) * 180.0 / Math.PI,
        _ => throw new ArgumentOutOfRangeException(nameof(angle)),
    };

    // UTIL-04 — inverse + cross-unit conversions for the unit-converter palette.

    /// <summary>Converts canonical metres back into <paramref name="unit"/>.</summary>
    public double FromMetres(double metres, LengthUnit unit) => unit switch
    {
        LengthUnit.Metre => metres,
        LengthUnit.Centimetre => metres / 0.01,
        LengthUnit.Millimetre => metres / 0.001,
        LengthUnit.Kilometre => metres / 1000.0,
        LengthUnit.Inch => metres / 0.0254,
        LengthUnit.Foot => metres / 0.3048,
        LengthUnit.Yard => metres / 0.9144,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    /// <summary>Converts canonical degrees back into <paramref name="unit"/>.</summary>
    public double FromDegrees(double degrees, AngleUnit unit) => unit switch
    {
        AngleUnit.Degree => degrees,
        AngleUnit.Grad => degrees / 0.9,
        AngleUnit.Mil => degrees * 6400.0 / 360.0,
        AngleUnit.Minute => degrees * 60.0,
        AngleUnit.PercentSlope => Math.Tan(degrees * Math.PI / 180.0) * 100.0,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    /// <summary>Converts a length value from one unit to another.</summary>
    public double ConvertLength(double value, LengthUnit from, LengthUnit to) =>
        FromMetres(ToMetres(new Length(value, from)), to);

    /// <summary>Converts an angle value from one unit to another.</summary>
    public double ConvertAngle(double value, AngleUnit from, AngleUnit to) =>
        FromDegrees(ToDegrees(new Angle(value, from)), to);
}
