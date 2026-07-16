// scene-meta.json v1 (BA-B3): everything the Blender script generator (BA-B5) needs
// that is NOT in the mesh — stations, surveys, components, the recenter offset, bounds,
// and source provenance. Coordinates are LOCAL (recentered, matching the PLY);
// world = local + offset. Versioned by BlenderModule.SceneMetaSchemaVersion.
//
// Leads/QM enrichment from the workspace leads register is added by BA-B4 (the `Leads`
// list is intentionally present-but-empty here so the schema is stable).

using Therion.Blender.Geometry;

namespace Therion.Blender;

/// <summary>A point in the metadata document (local coordinates unless stated).</summary>
public sealed record SceneMetaVec(double X, double Y, double Z)
{
    public static SceneMetaVec From(CaveVector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>An axis-aligned box with its derived centre and size, for convenience.</summary>
public sealed record SceneMetaBounds(SceneMetaVec Min, SceneMetaVec Max, SceneMetaVec Center, SceneMetaVec Size)
{
    public static SceneMetaBounds From(BoundingBox box) => new(
        SceneMetaVec.From(box.Min), SceneMetaVec.From(box.Max),
        SceneMetaVec.From(box.Center), SceneMetaVec.From(box.Size));
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

        var stations = new List<SceneMetaStation>();
        foreach (var s in model.Stations)
        {
            if ((s.Flags & CaveStationFlags.Anonymous) != 0) continue;
            stations.Add(new SceneMetaStation
            {
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
}
