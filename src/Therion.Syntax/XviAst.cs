// XVI AST. Therion's `.xvi` (XTherion Vector Image) is the format written by
// `export map -fmt xvi`. It is a Tcl script of `set <var> {<body>}` statements:
//
//   set XVIgrids {1.0 m}
//   set XVIstations { {<x> <y> <name>} ... }
//   set XVIshots    { {<x1> <y1> <x2> <y2>} ... }
//   set XVIsketchlines { {<colour> <x1> <y1> <x2> <y2> ...} ... }
//   set XVIgrid {<x0> <y0> <xax> <yax> <xay> <yay> <nx> <ny>}
//
// Coordinates are in drawing units (XTherion pixels). The grid maps them to the
// real world. Source-of-truth: Therion `thexpmap.cxx` (xvi exporter) + xtherion.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>A station marker in an <c>.xvi</c> (<c>XVIstations</c>): drawing-coords + name.</summary>
public readonly record struct XviStation(double X, double Y, string Name);

/// <summary>A survey leg in an <c>.xvi</c> (<c>XVIshots</c>): two endpoints in drawing-coords.</summary>
public readonly record struct XviShot(double X1, double Y1, double X2, double Y2);

/// <summary>A sketch polyline (<c>XVIsketchlines</c>): a colour name + a flat <c>x y x y …</c> list.</summary>
public sealed record XviSketchLine(string Colour, ImmutableArray<double> Coordinates);

/// <summary>
/// The reference grid (<c>XVIgrid</c>): an origin, the two basis vectors of one grid cell
/// (<c>(XAxisX,XAxisY)</c> and <c>(YAxisX,YAxisY)</c>) and the cell counts along each axis.
/// </summary>
public readonly record struct XviGrid(
    double OriginX, double OriginY,
    double XAxisX, double XAxisY,
    double YAxisX, double YAxisY,
    int CountX, int CountY);

/// <summary>Parsed <c>.xvi</c> file (Therion <c>set XVI*</c> export format).</summary>
public sealed record XviFile(
    SourceSpan Span,
    string Path,
    double? GridSpacing,
    string GridUnits,
    ImmutableArray<XviStation> Stations,
    ImmutableArray<XviShot> Shots,
    ImmutableArray<XviSketchLine> SketchLines,
    XviGrid? Grid,
    ImmutableArray<TrivialComment> LeadingComments) : TherionNode(Span)
{
    /// <summary>
    /// Raw <c>XVIimages</c> records (background sketch bitmaps written by therion's exporter —
    /// thexpmap.cxx; B7). Kept verbatim: each record is one brace-group's inner text.
    /// </summary>
    public ImmutableArray<string> Images { get; init; } = ImmutableArray<string>.Empty;
}
