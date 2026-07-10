// In-memory model parsed from a Therion .lox / Survex .3d file (BA-B2).
//
// Extraction rule (D-19): the parsers store EVERY piece of information the file
// carries — surveys, stations + flags, shots/LRUD, wall triangles, surface grid +
// bitmap, entrances, dates, coordinate system, metadata — even fields no downstream
// stage consumes yet. Coordinates stay in the file's own double-precision world
// system; recentering to a float-safe local origin happens in the geometry stage
// (BA-B3, D-15). Fields only one format can supply are null/empty for the other; the
// per-record XML docs say which format supplies what.

namespace Therion.Blender;

/// <summary>Source file format a <see cref="CaveModel"/> was parsed from.</summary>
public enum CaveSourceFormat
{
    Unknown = 0,
    /// <summary>Therion loch <c>.lox</c> (chunked binary; the only format with walls).</summary>
    Lox,
    /// <summary>Survex <c>.3d</c> (item stream; centerline + LRUD cross-sections).</summary>
    Survex3d,
}

/// <summary>
/// The parsed cave: geometry + all metadata extracted from a <c>.lox</c>/<c>.3d</c>
/// file. Produced by <c>Parsing.LoxReader</c> / <c>Parsing.Survex3dReader</c>.
/// </summary>
public sealed class CaveModel
{
    /// <summary>Absolute path of the source artifact this model was parsed from, if any.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Source format, set by the parser.</summary>
    public CaveSourceFormat SourceFormat { get; init; }

    /// <summary>File format version for <c>.3d</c> (3–8); null for <c>.lox</c>, which
    /// is unversioned.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>Survey title from the file header (<c>.3d</c> only).</summary>
    public string? Title { get; init; }

    /// <summary>Coordinate-system string suitable for PROJ, e.g. "EPSG:31700"
    /// (<c>.3d</c> v8 only; null when unspecified).</summary>
    public string? CoordinateSystem { get; init; }

    /// <summary>Character separating survey levels in station labels
    /// (<c>.3d</c> v8 metadata; '.' by default and for all other sources).</summary>
    public char SeparatorChar { get; init; } = '.';

    /// <summary>The header datestamp exactly as stored (<c>.3d</c> only; v8 uses
    /// "@" + Unix seconds, older versions a human-readable string).</summary>
    public string? Datestamp { get; init; }

    /// <summary>File-generation time parsed from <see cref="Datestamp"/> when it is in
    /// the v8 "@" + Unix-seconds form (null otherwise).</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>True when the file is an extended elevation rather than a plan-view
    /// model (<c>.3d</c> only).</summary>
    public bool IsExtendedElevation { get; init; }

    /// <summary>Survey tree (<c>.lox</c> only; empty for <c>.3d</c>).</summary>
    public IReadOnlyList<CaveSurvey> Surveys { get; init; } = [];

    /// <summary>All stations, in file order.</summary>
    public IReadOnlyList<CaveStation> Stations { get; init; } = [];

    /// <summary>All centerline shots/legs, in file order.</summary>
    public IReadOnlyList<CaveShot> Shots { get; init; } = [];

    /// <summary>Wall meshes (<c>.lox</c> only; empty for <c>.3d</c>).</summary>
    public IReadOnlyList<CaveScrap> Scraps { get; init; } = [];

    /// <summary>Terrain height grids (<c>.lox</c> only).</summary>
    public IReadOnlyList<CaveSurfaceGrid> Surfaces { get; init; } = [];

    /// <summary>Surface texture bitmaps (<c>.lox</c> only).</summary>
    public IReadOnlyList<CaveSurfaceBitmap> SurfaceBitmaps { get; init; } = [];

    /// <summary>Passage cross-section tubes (<c>.3d</c> only).</summary>
    public IReadOnlyList<CavePassage> Passages { get; init; } = [];

    /// <summary>Per-traverse loop-closure error info (<c>.3d</c> only).</summary>
    public IReadOnlyList<CaveTraverseError> TraverseErrors { get; init; } = [];

    /// <summary>
    /// True when the model carries (or can synthesize) wall geometry: explicit scrap
    /// meshes, or at least one skinnable shot — a shot with a cross-section shape that
    /// is not surface/duplicate/LRUD-less (mirrors Therion's own walls test).
    /// </summary>
    public bool HasWalls
    {
        get
        {
            if (Scraps.Count > 0) return true;
            const CaveShotFlags noWalls =
                CaveShotFlags.Surface | CaveShotFlags.Duplicate | CaveShotFlags.NotLrud;
            foreach (var shot in Shots)
            {
                if (shot.SectionType != CaveShotSection.None && (shot.Flags & noWalls) == 0)
                    return true;
            }
            return false;
        }
    }
}
