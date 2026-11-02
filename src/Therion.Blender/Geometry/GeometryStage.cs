// The geometry stage (BA-B3): CaveModel → recentered wall mesh + derived geometry.
//
// Pipeline: measure world bounds → pick a recenter offset (bbox centre, R-03/D-15) →
// translate all geometry to the local origin → build the wall mesh (real .lox scraps,
// or synthesized LRUD tubes for centerline sources) → optional depth-tint colours →
// centerline graph. Pure and deterministic: same model + options ⇒ identical result.

namespace Therion.Blender.Geometry;

/// <summary>Turns a parsed <see cref="CaveModel"/> into Blender-ready geometry.</summary>
public static class GeometryStage
{
    public static GeometryResult Build(CaveModel model, GeometryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new GeometryOptions();

        var originalBounds = MeasureBounds(model);
        var offset = options.Recenter && !originalBounds.IsEmpty ? originalBounds.Center : CaveVector3.Zero;
        var recentered = offset == CaveVector3.Zero ? model : Translate(model, offset * -1.0);
        var localBounds = originalBounds.IsEmpty ? BoundingBox.Empty : originalBounds.Translate(offset * -1.0);

        var walls = BuildWalls(recentered, options);
        if (options.DepthTint && !walls.IsEmpty)
            walls = walls with { VertexColors = TintByDepth(walls.Vertices) };

        return new GeometryResult
        {
            Walls = walls,
            Offset = offset,
            OriginalBounds = originalBounds,
            LocalBounds = localBounds,
            RecenteredModel = recentered,
            Centerline = CenterlineGraph.Build(recentered),
        };
    }

    private static CaveMesh BuildWalls(CaveModel model, GeometryOptions options)
    {
        bool useScraps = options.WallSource switch
        {
            WallSource.Scraps => true,
            WallSource.Tubes => false,
            _ => model.Scraps.Count > 0, // Auto
        };
        return useScraps ? MeshFromScraps(model.Scraps) : TubeMesher.Build(WithPassageLrud(model), options);
    }

    /// <summary>
    /// <c>.3d</c> keeps its passage dimensions in XSECT records keyed by station name, while
    /// its legs carry only endpoint coordinates. Joins the two (name → station position →
    /// leg endpoint) so the tube mesher sees the measured LRUD instead of the default radius.
    /// Shots that already carry LRUD (<c>.lox</c>) are left as they are; the first
    /// cross-section seen for a station wins.
    /// </summary>
    private static IReadOnlyList<CaveShot> WithPassageLrud(CaveModel model)
    {
        if (model.Passages.Count == 0) return model.Shots;

        var positionByName = new Dictionary<string, CaveVector3>(StringComparer.Ordinal);
        foreach (var station in model.Stations) positionByName.TryAdd(station.Name, station.Position);

        var lrudAt = new Dictionary<(long, long, long), CaveLrud>();
        foreach (var passage in model.Passages)
            foreach (var xsect in passage.Stations)
                if (positionByName.TryGetValue(xsect.StationName, out var position))
                    lrudAt.TryAdd(PositionKey(position), new CaveLrud(xsect.Left, xsect.Right, xsect.Up, xsect.Down));
        if (lrudAt.Count == 0) return model.Shots;

        var shots = new List<CaveShot>(model.Shots.Count);
        foreach (var shot in model.Shots)
        {
            var from = shot.FromLrud ?? (lrudAt.TryGetValue(PositionKey(shot.FromPosition), out var f) ? f : null);
            var to = shot.ToLrud ?? (lrudAt.TryGetValue(PositionKey(shot.ToPosition), out var t) ? t : null);
            shots.Add(from == shot.FromLrud && to == shot.ToLrud ? shot : shot with { FromLrud = from, ToLrud = to });
        }
        return shots;
    }

    // Station and leg coordinates come from the same file values (and the same recenter
    // shift), so a millimetre grid matches them without accumulating float noise.
    private static (long, long, long) PositionKey(CaveVector3 p) =>
        ((long)Math.Round(p.X * 1000.0), (long)Math.Round(p.Y * 1000.0), (long)Math.Round(p.Z * 1000.0));

    private static CaveMesh MeshFromScraps(IReadOnlyList<CaveScrap> scraps)
    {
        var parts = new List<CaveMesh>(scraps.Count);
        foreach (var scrap in scraps)
        {
            if (scrap.Points.Count == 0 || scrap.Triangles.Count == 0) continue;
            parts.Add(new CaveMesh { Vertices = scrap.Points, Triangles = scrap.Triangles });
        }
        return CaveMesh.Combine(parts);
    }

    private static IReadOnlyList<CaveColor> TintByDepth(IReadOnlyList<CaveVector3> vertices)
    {
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
        foreach (var v in vertices)
        {
            if (v.Z < minZ) minZ = v.Z;
            if (v.Z > maxZ) maxZ = v.Z;
        }
        var colors = new CaveColor[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
            colors[i] = DepthRamp.SampleZ(vertices[i].Z, minZ, maxZ);
        return colors;
    }

    /// <summary>World bounds over every kind of geometry a model carries.</summary>
    private static BoundingBox MeasureBounds(CaveModel model)
    {
        var box = BoundingBox.Empty;
        foreach (var station in model.Stations) box = box.Encapsulate(station.Position);
        foreach (var shot in model.Shots)
        {
            box = box.Encapsulate(shot.FromPosition);
            box = box.Encapsulate(shot.ToPosition);
        }
        foreach (var scrap in model.Scraps)
            foreach (var p in scrap.Points) box = box.Encapsulate(p);
        return box;
    }

    /// <summary>Returns a copy of the model with all station/shot/scrap coordinates
    /// shifted by <paramref name="delta"/> (surfaces are passed through unchanged —
    /// surface meshing lands with surface materials, BA-B7).</summary>
    internal static CaveModel Translate(CaveModel model, CaveVector3 delta)
    {
        var stations = new List<CaveStation>(model.Stations.Count);
        foreach (var s in model.Stations)
            stations.Add(s with { Position = s.Position + delta });

        var shots = new List<CaveShot>(model.Shots.Count);
        foreach (var s in model.Shots)
            shots.Add(s with { FromPosition = s.FromPosition + delta, ToPosition = s.ToPosition + delta });

        var scraps = new List<CaveScrap>(model.Scraps.Count);
        foreach (var scrap in model.Scraps)
        {
            var points = new CaveVector3[scrap.Points.Count];
            for (int i = 0; i < points.Length; i++) points[i] = scrap.Points[i] + delta;
            scraps.Add(scrap with { Points = points });
        }

        return new CaveModel
        {
            SourcePath = model.SourcePath,
            SourceFormat = model.SourceFormat,
            FormatVersion = model.FormatVersion,
            Title = model.Title,
            CoordinateSystem = model.CoordinateSystem,
            SeparatorChar = model.SeparatorChar,
            Datestamp = model.Datestamp,
            Timestamp = model.Timestamp,
            IsExtendedElevation = model.IsExtendedElevation,
            Surveys = model.Surveys,
            Stations = stations,
            Shots = shots,
            Scraps = scraps,
            Surfaces = model.Surfaces,
            SurfaceBitmaps = model.SurfaceBitmaps,
            Passages = model.Passages,
            TraverseErrors = model.TraverseErrors,
        };
    }
}
