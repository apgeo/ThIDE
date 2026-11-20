// scene-meta.json v1 (BA-B3): everything the Blender script generator (BA-B5) needs
// that is NOT in the mesh — stations, surveys, components, the recenter offset, bounds,
// and source provenance. Coordinates are LOCAL (recentered, matching the PLY);
// world = local + offset. Versioned by BlenderModule.SceneMetaSchemaVersion.
//
// Leads/QM enrichment from the workspace leads register is added by BA-B4 (the `Leads`
// list is intentionally present-but-empty here so the schema is stable).

using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender;

/// <summary>A point in the metadata document (local coordinates unless stated).</summary>
public sealed record SceneMetaVec(double X, double Y, double Z)
{
    public static SceneMetaVec From(CaveVector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>An axis-aligned box with its derived centre and size, for convenience.</summary>
public sealed record SceneMetaBounds(SceneMetaVec Min, SceneMetaVec Max, SceneMetaVec Center, SceneMetaVec Size)
{
    /// <summary>An empty box (no geometry) maps to all-zero extents: its ±infinity Min/Max
    /// cannot be written as JSON.</summary>
    public static SceneMetaBounds From(BoundingBox box)
    {
        if (box.IsEmpty)
        {
            var zero = SceneMetaVec.From(CaveVector3.Zero);
            return new(zero, zero, zero, zero);
        }
        return new(
            SceneMetaVec.From(box.Min), SceneMetaVec.From(box.Max),
            SceneMetaVec.From(box.Center), SceneMetaVec.From(box.Size));
    }
}

/// <summary>Where the model came from (echoed from the parsed <see cref="CaveModel"/>).</summary>
public sealed record SceneMetaSource
{
    public string? Path { get; init; }
    public required string Format { get; init; }
    public int? FormatVersion { get; init; }
    public string? Title { get; init; }
    public string? CoordinateSystem { get; init; }
    public string Separator { get; init; } = ".";
    public string? Datestamp { get; init; }
}

/// <summary>A labelled survey station (local coordinates).</summary>
public sealed record SceneMetaStation
{
    public required string Name { get; init; }
    public required SceneMetaVec Position { get; init; }
    public bool Entrance { get; init; }
    public bool Fixed { get; init; }
    public bool Surface { get; init; }
    public uint RawFlags { get; init; }
    public uint? SurveyId { get; init; }

    /// <summary>How far above the station the roof of its passage is, in metres: from the
    /// surveyed up distances of a tube model's level legs, else from the walls above the station.
    /// Null when neither gives one — a station halfway down a shaft, a model without walls,
    /// documents written before this field existed.</summary>
    public double? Roof { get; init; }

    /// <summary>The roof from the walls alone (see <see cref="Roof"/>); null when they give none.</summary>
    public double? WallRoof { get; init; }

    /// <summary>The roof from the surveyed up distances alone (tube models); null when there is none.</summary>
    public double? SurveyedRoof { get; init; }
}

/// <summary>A survey-tree node (from <c>.lox</c>; empty for <c>.3d</c>).</summary>
public sealed record SceneMetaSurvey(uint Id, uint ParentId, string Name, string? Title);

/// <summary>A connected centerline component with its centroid and bounds.</summary>
public sealed record SceneMetaComponent
{
    public required int Index { get; init; }
    public required int StationCount { get; init; }
    public required SceneMetaVec Centroid { get; init; }
    public required SceneMetaBounds Bounds { get; init; }
}

/// <summary>A survey lead / question-mark marker. Populated by BA-B4 from the workspace
/// leads register; empty in the geometry-stage-only document.</summary>
public sealed record SceneMetaLead
{
    public required string Station { get; init; }
    public required SceneMetaVec Position { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// The full <c>scene-meta.json</c> document (schema v1). Build with
/// <see cref="Build"/> from a <see cref="GeometryResult"/>; serialize with
/// <see cref="Writers.SceneMetaWriter"/>.
/// </summary>
public sealed record SceneMeta
{
    public int Version { get; init; } = BlenderModule.SceneMetaSchemaVersion;
    public string Generator { get; init; } = "ThIDE Blender module";
    public required SceneMetaSource Source { get; init; }
    public string Units { get; init; } = "metres";

    /// <summary>The vector subtracted to recenter (world = local + offset).</summary>
    public required SceneMetaVec Offset { get; init; }

    /// <summary>Bounds in original world coordinates.</summary>
    public required SceneMetaBounds WorldBounds { get; init; }

    /// <summary>Bounds in local (recentered) coordinates.</summary>
    public required SceneMetaBounds LocalBounds { get; init; }

    public bool HasWalls { get; init; }
    public int WallVertexCount { get; init; }
    public int WallTriangleCount { get; init; }

    public required IReadOnlyList<SceneMetaSurvey> Surveys { get; init; }
    public required IReadOnlyList<SceneMetaStation> Stations { get; init; }
    public required IReadOnlyList<SceneMetaComponent> Components { get; init; }
    public IReadOnlyList<SceneMetaLead> Leads { get; init; } = [];

    /// <summary>
    /// Builds the metadata document from a geometry result. Only labelable stations are
    /// included: anonymous stations (e.g. <c>.3d</c> splay endpoints with empty names)
    /// are dropped — the label stage never wants them and they would bloat the document.
    /// </summary>
    public static SceneMeta Build(GeometryResult geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var model = geometry.RecenteredModel;

        var roofs = geometry.HasWalls ? new RoofFinder(geometry.Walls) : null;
        var surveyedRoofs = SurveyedRoofs(geometry.TubeLegs);
        var stations = new List<SceneMetaStation>();
        foreach (var s in model.Stations)
        {
            if ((s.Flags & CaveStationFlags.Anonymous) != 0) continue;
            // Tubes are built from the surveyed distances, so those are the roof; a ray up
            // through the walls can leave by a junction's opening and find another level.
            double? surveyedRoof = surveyedRoofs.TryGetValue(s.Position, out double surveyed) ? surveyed : null;
            double? wallRoof = roofs?.HeightNear(s.Position);
            double? roof = surveyedRoof ?? wallRoof;
            stations.Add(new SceneMetaStation
            {
                Roof = roof is { } r ? Math.Round(r, 3) : null,
                WallRoof = wallRoof is { } w ? Math.Round(w, 3) : null,
                SurveyedRoof = surveyedRoof is { } u ? Math.Round(u, 3) : null,
                Name = s.Name,
                Position = SceneMetaVec.From(s.Position),
                Entrance = s.IsEntrance,
                Fixed = s.IsFixed,
                Surface = (s.Flags & CaveStationFlags.Surface) != 0,
                RawFlags = s.RawFlags,
                SurveyId = s.SurveyId,
            });
        }

        var surveys = new List<SceneMetaSurvey>(model.Surveys.Count);
        foreach (var survey in model.Surveys)
            surveys.Add(new SceneMetaSurvey(survey.Id, survey.ParentId, survey.Name, survey.Title));

        var components = new List<SceneMetaComponent>(geometry.Centerline.Components.Count);
        foreach (var c in geometry.Centerline.Components)
            components.Add(new SceneMetaComponent
            {
                Index = c.Index,
                StationCount = c.StationCount,
                Centroid = SceneMetaVec.From(c.Centroid),
                Bounds = SceneMetaBounds.From(c.Bounds),
            });

        return new SceneMeta
        {
            Source = new SceneMetaSource
            {
                Path = model.SourcePath,
                Format = model.SourceFormat.ToString().ToLowerInvariant(),
                FormatVersion = model.FormatVersion,
                Title = model.Title,
                CoordinateSystem = model.CoordinateSystem,
                Separator = model.SeparatorChar.ToString(),
                Datestamp = model.Datestamp,
            },
            Offset = SceneMetaVec.From(geometry.Offset),
            WorldBounds = SceneMetaBounds.From(geometry.OriginalBounds),
            LocalBounds = SceneMetaBounds.From(geometry.LocalBounds),
            HasWalls = geometry.HasWalls,
            WallVertexCount = geometry.Walls.Vertices.Count,
            WallTriangleCount = geometry.Walls.Triangles.Count,
            Surveys = surveys,
            Stations = stations,
            Components = components,
        };
    }

    /// <summary>
    /// The roof above each station of a tube model as surveyed: the largest height of the up
    /// distance of the tube sections at the station, counting only sections of legs that are not
    /// steep (in a shaft the "up" of a cross-section points sideways). Used before the walls: a
    /// pitch head has no floor under it, so the walls give it no roof, and at a junction a ray up
    /// can leave through the opening and find another level. Empty for scrap walls.
    /// </summary>
    private static Dictionary<CaveVector3, double> SurveyedRoofs(IReadOnlyList<TubeLeg> legs)
    {
        var roofs = new Dictionary<CaveVector3, double>();
        foreach (var leg in legs)
        {
            foreach (var section in new[] { leg.From, leg.To })
            {
                if (section.UpAxis.Z < SteepLegUpZ) continue;
                double height = section.Up * section.UpAxis.Z;
                if (height <= 0.0) continue;
                if (!roofs.TryGetValue(section.Station, out double known) || height > known)
                    roofs[section.Station] = height;
            }
        }
        return roofs;
    }

    /// <summary>The vertical part of a cross-section's up axis below which its leg counts as
    /// steep (about 60° from horizontal).</summary>
    private const double SteepLegUpZ = 0.5;
}
