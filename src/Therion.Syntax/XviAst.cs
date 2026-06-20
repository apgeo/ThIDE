// Implementation Plan §3.1, §4.3 — XVI AST.
// Geo-referenced sketch metadata: maps a raster image to Therion survey
// coordinates via a 2D affine transform. Source-of-truth: xtherion output +
// thbook §"Maps from scanned drawings".

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>2-D affine transform <c>(survey ? pixel)</c>.</summary>
public sealed record AffineTransform2D(double A, double B, double C, double D, double Tx, double Ty)
{
    /// <summary>Determinant: zero / near-zero ? non-invertible (degenerate).</summary>
    public double Determinant => A * D - B * C;
}

/// <summary>One ground-control point (survey ? pixel).</summary>
public sealed record CalibrationPoint(double SurveyX, double SurveyY, double PixelX, double PixelY);

/// <summary>Parsed <c>.xvi</c> file.</summary>
public sealed record XviFile(
    SourceSpan Span,
    string Path,
    int Version,
    double Scale,
    AffineTransform2D Transform,
    string ImageRelativePath,
    ImmutableArray<CalibrationPoint> CalibrationPoints,
    ImmutableArray<TrivialComment> LeadingComments) : TherionNode(Span);
