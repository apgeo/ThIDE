// The geometry stage (BA-B3): CaveModel → recentered wall mesh + derived geometry.
//
// Pipeline: measure world bounds → pick a recenter offset (bbox centre, R-03/D-15) →
// translate all geometry to the local origin → optional reveal values of the legs → build the
// wall mesh (real .lox scraps, or synthesized LRUD tubes for centerline sources), with those
// values on its vertices → optional depth-tint colours → centerline graph. Pure and
// deterministic: same model + options ⇒ identical result.

using Therion.Cave3D;

namespace Therion.CaveGeometry;

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

        var reveal = options.Reveal is null ? null : RevealField.Build(recentered, options.Reveal, offset);
        var (walls, tubeLegs, triangleLegs) = BuildWalls(recentered, options, reveal);
        if (options.DepthTint && !walls.IsEmpty)
            walls = walls with { VertexColors = TintByDepth(walls.Vertices) };

        return new GeometryResult
        {
            Walls = walls,
            TubeLegs = tubeLegs,
            TriangleLegs = triangleLegs,
            Reveal = reveal,
            Offset = offset,
            OriginalBounds = originalBounds,
            LocalBounds = localBounds,
            RecenteredModel = recentered,
            Centerline = CenterlineGraph.Build(recentered),
        };
    }

    private static (CaveMesh Walls, IReadOnlyList<TubeLeg> TubeLegs, IReadOnlyList<int> TriangleLegs) BuildWalls(
        CaveModel model, GeometryOptions options, RevealField? reveal)
    {
        bool useScraps = options.WallSource switch
        {
            WallSource.Scraps => true,
            WallSource.Tubes => false,
            _ => model.Scraps.Count > 0, // Auto
        };
        if (useScraps)
        {
            var scraps = MeshFromScraps(model.Scraps);
            return (reveal is null || scraps.IsEmpty ? scraps : reveal.OnWalls(scraps), [], []);
        }
        var legs = TubeMesher.Legs(WithPassageLrud(model), options);
        // The tubes are those of the legs the reveal values are of, in the same order; which leg
        // a triangle is of also says which part of the walls it goes to (WallParts).
        var triangleLegs = new List<int>();
        var tubes = TubeMesher.BuildFromLegs(legs, options, triangleLegs);
        if (reveal is null) return (tubes, legs, triangleLegs);
        return (tubes.IsEmpty ? tubes : reveal.OnTubes(tubes, triangleLegs), legs, triangleLegs);
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

    /// <summary>World bounds over the cave's own geometry: stations, shots and scraps. Surface
    /// grids are left out on purpose: a terrain tile is usually far larger than the cave, so
    /// including it would move the recentre offset (and the bounds a camera frames) off the
    /// cave. Surfaces are still translated by the same offset, see <see cref="Translate"/>.</summary>
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

    /// <summary>Returns a copy of the model with every coordinate shifted by
    /// <paramref name="delta"/>: stations, shots, scraps, surface grids (calibration origin
    /// and heights) and surface bitmaps (calibration origin, so an image still lands on its
    /// grid). Passages and traverse errors carry no coordinates and are passed through.</summary>
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

        var surfaces = new List<CaveSurfaceGrid>(model.Surfaces.Count);
        foreach (var grid in model.Surfaces)
        {
            var heights = new double[grid.Heights.Count];
            for (int i = 0; i < heights.Length; i++) heights[i] = grid.Heights[i] + delta.Z;
            surfaces.Add(grid with { Calibration = ShiftOrigin(grid.Calibration, delta), Heights = heights });
        }

        var bitmaps = new List<CaveSurfaceBitmap>(model.SurfaceBitmaps.Count);
        foreach (var bitmap in model.SurfaceBitmaps)
            bitmaps.Add(bitmap with { Calibration = ShiftOrigin(bitmap.Calibration, delta) });

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
            Surfaces = surfaces,
            SurfaceBitmaps = bitmaps,
            Passages = model.Passages,
            UnknownChunkTypes = model.UnknownChunkTypes,
            TraverseErrors = model.TraverseErrors,
        };
    }

    // Calibration coefficients are [originX, originY, 2×2 step matrix]: a translation moves
    // only the origin. A malformed calibration (fewer than two values) is kept as it is.
    private static IReadOnlyList<double> ShiftOrigin(IReadOnlyList<double> calibration, CaveVector3 delta)
    {
        if (calibration.Count < 2) return calibration;
        var shifted = calibration.ToArray();
        shifted[0] += delta.X;
        shifted[1] += delta.Y;
        return shifted;
    }
}
