// In-memory model parsed from a Therion 3D output (BA-B1 scaffold placeholder).
//
// BA-B2 (★, Fable 5) fills this in by porting Therion `lxFile.cxx` (.lox) and Survex
// `img.c` (.3d). Per D-19 the parser must EXTRACT ALL INFORMATION the files carry —
// surveys, stations + flags, shots/legs, LRUD, wall triangles, surface grid + bitmap,
// entrances, teams, dates, coordinate system, arbitrary metadata — and STORE it here
// even when a field isn't consumed downstream yet. UTM-scale coordinates are kept in
// double precision (recentering to a float-safe local origin happens later, in the
// geometry stage BA-B3, cf. D-15).
//
// Kept as an intentionally-empty container at scaffold time so the public seam
// (IBlenderRenderService) compiles; the real shape lands in BA-B2 with its tests.

namespace Therion.Blender;

/// <summary>
/// The parsed cave: geometry + all metadata extracted from a <c>.lox</c>/<c>.3d</c> file.
/// Placeholder — populated by the native parser in BA-B2 (see file header).
/// </summary>
public sealed class CaveModel
{
    /// <summary>Absolute path of the source artifact this model was parsed from, if any.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Source format ("loch"/"survex"), set by the parser once known.</summary>
    public string? SourceFormat { get; init; }
}
