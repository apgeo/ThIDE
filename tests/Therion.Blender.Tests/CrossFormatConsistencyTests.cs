// The strongest BA-B2 correctness evidence: tests/Corpus carries the SAME Therion
// export of av_cerbul_de_aur in both formats, so the two independent parsers must
// reconstruct the same cave. .3d coordinates are centimetre-quantized s32 while .lox
// keeps full doubles — geometric comparisons therefore use a 1 cm tolerance.

using Therion.Blender;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class CrossFormatConsistencyTests
{
    private const double CmTolerance = 0.011; // half-cm rounding on either side + slack

    private static (CaveModel Lox, CaveModel Svx) LoadBoth() => (
        LoxReader.ReadFile(TestCorpus.AvCerbulLox()),
        Survex3dReader.ReadFile(TestCorpus.AvCerbul3d()));

    [CorpusFact]
    public void BoundingBoxes_Agree()
    {
        var (lox, svx) = LoadBoth();

        var loxBounds = Bounds(lox.Stations.Select(s => s.Position));
        var svxBounds = Bounds(svx.Stations.Select(s => s.Position));

        AssertClose(loxBounds.Min, svxBounds.Min);
        AssertClose(loxBounds.Max, svxBounds.Max);
    }

    [CorpusFact]
    public void NamedSurvexStations_ExistInLoxAtTheSamePosition()
    {
        var (lox, svx) = LoadBoth();

        // .lox keys stations by (survey, short name); .3d by full label. Positions are
        // the reliable join key. Quantize .lox positions to cm cells for the lookup.
        var loxCells = new HashSet<(long, long, long)>(lox.Stations.SelectMany(s => Cells(s.Position)));

        var named = svx.Stations.Where(s => (s.Flags & CaveStationFlags.Anonymous) == 0).ToList();
        Assert.True(named.Count > 10, $"expected named .3d stations, found {named.Count}");

        var unmatched = named.Where(s => !Cells(s.Position).Any(loxCells.Contains)).ToList();
        Assert.True(unmatched.Count == 0,
            $"{unmatched.Count}/{named.Count} named .3d stations have no .lox station within 1 cm, " +
            $"e.g. {unmatched.FirstOrDefault()?.Name} @ {unmatched.FirstOrDefault()?.Position}");
    }

    [CorpusFact]
    public void EntranceStations_AgreeAcrossFormats()
    {
        var (lox, svx) = LoadBoth();

        var loxEntrances = lox.Stations.Where(s => s.IsEntrance).Select(s => s.Position).ToList();
        var svxEntrances = svx.Stations.Where(s => s.IsEntrance).Select(s => s.Position).ToList();

        Assert.NotEmpty(loxEntrances);
        Assert.Equal(loxEntrances.Count, svxEntrances.Count);
        foreach (var entrance in svxEntrances)
            Assert.Contains(loxEntrances, candidate => Distance(candidate, entrance) < CmTolerance);
    }

    [CorpusFact]
    public void CenterlineLegs_AgreeAcrossFormats()
    {
        var (lox, svx) = LoadBoth();

        // Compare the non-splay, non-surface, non-duplicate centerline as unordered
        // endpoint pairs (leg direction may differ between exporters).
        var loxLegs = lox.Shots
            .Where(NotAuxiliary)
            .Select(s => LegKey(s.FromPosition, s.ToPosition))
            .ToHashSet();
        var svxLegs = svx.Shots.Where(NotAuxiliary).ToList();

        Assert.True(svxLegs.Count > 10, $"expected centerline legs, found {svxLegs.Count}");
        var unmatched = svxLegs.Where(s => !loxLegs.Contains(LegKey(s.FromPosition, s.ToPosition))).ToList();
        Assert.True(unmatched.Count == 0,
            $"{unmatched.Count}/{svxLegs.Count} .3d centerline legs missing from .lox, " +
            $"e.g. {unmatched.FirstOrDefault()?.FromPosition} -> {unmatched.FirstOrDefault()?.ToPosition}");

        static bool NotAuxiliary(CaveShot s) =>
            (s.Flags & (CaveShotFlags.Splay | CaveShotFlags.Surface | CaveShotFlags.Duplicate)) == 0;
    }

    // ----- geometry helpers -----

    private static (CaveVector3 Min, CaveVector3 Max) Bounds(IEnumerable<CaveVector3> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        return (new CaveVector3(minX, minY, minZ), new CaveVector3(maxX, maxY, maxZ));
    }

    private static void AssertClose(CaveVector3 expected, CaveVector3 actual)
        => Assert.True(Distance(expected, actual) < CmTolerance, $"expected {expected} ≈ {actual}");

    private static double Distance(CaveVector3 a, CaveVector3 b)
        => Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));

    /// <summary>Quantizes a position to its cm cell and the 26 neighbours, so two
    /// values within tolerance always share at least one cell.</summary>
    private static IEnumerable<(long, long, long)> Cells(CaveVector3 p)
    {
        long x = (long)Math.Round(p.X * 100), y = (long)Math.Round(p.Y * 100), z = (long)Math.Round(p.Z * 100);
        for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
                for (long dz = -1; dz <= 1; dz++)
                    yield return (x + dx, y + dy, z + dz);
    }

    /// <summary>Order-independent cm-quantized endpoint pair key for a leg.</summary>
    private static (long, long, long, long, long, long) LegKey(CaveVector3 a, CaveVector3 b)
    {
        var ka = ((long)Math.Round(a.X * 100), (long)Math.Round(a.Y * 100), (long)Math.Round(a.Z * 100));
        var kb = ((long)Math.Round(b.X * 100), (long)Math.Round(b.Y * 100), (long)Math.Round(b.Z * 100));
        var (lo, hi) = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
        return (lo.Item1, lo.Item2, lo.Item3, hi.Item1, hi.Item2, hi.Item3);
    }
}
