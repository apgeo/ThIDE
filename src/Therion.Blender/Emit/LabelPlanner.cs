// Label engine (BA-B8, FR-05) — selects which stations/components/leads get a label and
// where, from scene-meta.json. Like the camera (D-16), the selection + the R-13 cap +
// distance thinning run in C# so they are unit-tested and the emitted script is a compact
// data table, not thousands of runtime branches. Pure and deterministic.
//
// Positions are LOCAL (recentered, matching the PLY and camera keyframes — D-15).

using System.Text.RegularExpressions;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Emit;

/// <summary>A billboarded text label: <paramref name="Text"/> at <paramref name="Position"/>
/// (local coords) rendered at <paramref name="Size"/> metres.</summary>
public readonly record struct LabelItem(string Text, CaveVector3 Position, double Size);

/// <summary>A lead / QM marker: an icosphere of <paramref name="Radius"/> at
/// <paramref name="Position"/>, optionally captioned.</summary>
public readonly record struct LeadItem(string Text, CaveVector3 Position, double Radius, bool ShowText, double TextSize)
{
    /// <summary>The lead's station, which names the marker's objects (two leads may share a
    /// note); null falls back to <see cref="Text"/>.</summary>
    public string? Station { get; init; }
}

/// <summary>The label engine's output — the data tables the emitter writes out.</summary>
public sealed record LabelPlan
{
    public IReadOnlyList<LabelItem> Stations { get; init; } = [];
    public IReadOnlyList<LabelItem> Components { get; init; } = [];
    public IReadOnlyList<LeadItem> Leads { get; init; } = [];

    /// <summary>True when the station cap trimmed the matched set (surfaced as a warning).</summary>
    public bool StationsCapped { get; init; }

    /// <summary>How many stations matched the filter before the cap.</summary>
    public int StationMatchCount { get; init; }

    public bool IsEmpty => Stations.Count == 0 && Components.Count == 0 && Leads.Count == 0;
}

/// <summary>Plans labels from a <see cref="SceneSpec"/> and its <see cref="SceneMeta"/>.</summary>
public static class LabelPlanner
{
    private const double StationSizeFraction = 0.012;
    private const double LeadRadiusFraction = 0.01;

    public static LabelPlan Plan(SceneSpec spec, SceneMeta meta)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(meta);
        var labels = spec.Labels;
        double diagonal = Diagonal(meta.LocalBounds);

        var stations = PlanStations(labels.Stations, meta, diagonal, out bool capped, out int matched);
        var components = labels.Components.Show ? PlanComponents(labels.Components, meta, diagonal) : [];
        bool inside = spec.Camera.Template == CameraTemplate.Flythrough;
        var leads = labels.Leads.Show ? PlanLeads(labels.Leads, meta, diagonal, inside) : [];

        return new LabelPlan
        {
            Stations = stations,
            Components = components,
            Leads = leads,
            StationsCapped = capped,
            StationMatchCount = matched,
        };
    }

    private static IReadOnlyList<LabelItem> PlanStations(
        StationLabelSpec spec, SceneMeta meta, double diagonal, out bool capped, out int matched)
    {
        capped = false;
        matched = 0;
        if (!spec.Show) return [];

        var predicate = FilterPredicate(spec);
        var selected = new List<SceneMetaStation>();
        foreach (var s in meta.Stations)
            if (predicate(s)) selected.Add(s);
        matched = selected.Count;

        if (selected.Count > spec.MaxCount)
        {
            var points = selected.Select(s => Vec(s.Position)).ToList();
            var keep = FarthestPointSubset(points, spec.MaxCount);
            selected = keep.Select(i => selected[i]).ToList();
            capped = true;
        }

        double size = diagonal * StationSizeFraction * spec.TextScale;
        var result = new List<LabelItem>(selected.Count);
        foreach (var s in selected)
            result.Add(new LabelItem(s.Name, AboveTheRoof(s, size), size));
        return result;
    }

    /// <summary>A station sits inside its passage, where the walls hide its label from any
    /// camera outside; the label goes just above the passage roof instead, its lower edge clear
    /// of it. A station with no roof above it keeps its label on the station.</summary>
    private static CaveVector3 AboveTheRoof(SceneMetaStation s, double size)
    {
        var at = Vec(s.Position);
        return s.Roof is { } roof ? at + new CaveVector3(0.0, 0.0, roof + size * RoofClearanceFactor) : at;
    }

    /// <summary>Half a label's height plus a margin, in label sizes.</summary>
    private const double RoofClearanceFactor = 0.75;

    private static Func<SceneMetaStation, bool> FilterPredicate(StationLabelSpec spec)
    {
        switch (spec.Filter)
        {
            case StationFilter.Entrances:
                return s => s.Entrance;
            case StationFilter.Named:
                return _ => true;
            case StationFilter.Regex:
                var regex = new Regex(spec.Pattern ?? "", RegexOptions.CultureInvariant);
                return s => regex.IsMatch(s.Name);
            case StationFilter.DepthRange:
                double lo = spec.MinDepth ?? double.NegativeInfinity;
                double hi = spec.MaxDepth ?? double.PositiveInfinity;
                return s => s.Position.Z >= lo && s.Position.Z <= hi;
            default:
                return _ => false;
        }
    }

    private static IReadOnlyList<LabelItem> PlanComponents(ComponentLabelSpec spec, SceneMeta meta, double diagonal)
    {
        double size = diagonal * StationSizeFraction * spec.TextScale;
        var result = new List<LabelItem>();
        foreach (var c in meta.Components)
        {
            if (c.StationCount < spec.MinStationCount) continue;
            // Components are connectivity pieces; meta v1 carries no survey name for them.
            result.Add(new LabelItem($"Component {c.Index + 1}", Vec(c.Centroid), size));
        }
        return result;
    }

    /// <summary>Lead markers. Seen from outside the cave a marker is sized from the whole cave so
    /// it shows at that distance; seen from inside (a flythrough) it is sized from the passage it
    /// is in — a fraction of the roof height at its station — so the camera does not fly into a
    /// ball wider than the passage. Never larger than the outside size.</summary>
    private static IReadOnlyList<LeadItem> PlanLeads(LeadMarkerSpec spec, SceneMeta meta, double diagonal, bool inside)
    {
        double outsideRadius = diagonal * LeadRadiusFraction * spec.MarkerScale;
        double textSize = diagonal * StationSizeFraction;
        var roofs = new Dictionary<string, double?>(StringComparer.Ordinal);
        if (inside)
            foreach (var s in meta.Stations) roofs.TryAdd(s.Name, s.Roof);

        var result = new List<LeadItem>(meta.Leads.Count);
        foreach (var lead in meta.Leads)
        {
            string text = !string.IsNullOrWhiteSpace(lead.Note) ? lead.Note! : lead.Station;
            double radius = outsideRadius;
            if (inside)
            {
                double roof = roofs.GetValueOrDefault(lead.Station) ?? UnknownPassageRoof;
                double passageRadius = Math.Max(MinInsideRadius, roof * LeadRoofFraction) * spec.MarkerScale;
                radius = Math.Min(outsideRadius, passageRadius);
                textSize = Math.Min(textSize, radius);
            }
            result.Add(new LeadItem(text, Vec(lead.Position), radius, spec.ShowText, textSize) { Station = lead.Station });
        }
        return result;
    }

    /// <summary>Inside the cave, a marker's radius as a fraction of the roof height at its
    /// station: at the top of its pulse (×1.5) the ball is under half that height across.</summary>
    private const double LeadRoofFraction = 0.15;

    /// <summary>The roof height assumed for a lead whose station has none.</summary>
    private const double UnknownPassageRoof = 1.5;

    private const double MinInsideRadius = 0.1;

    /// <summary>Greedy farthest-point sampling: keeps <paramref name="k"/> of
    /// <paramref name="points"/> spread as far apart as possible, so a capped label set
    /// still covers the whole cave (R-13). Deterministic (starts at index 0, ties broken by
    /// lowest index); returns the kept indices in ascending order.</summary>
    internal static List<int> FarthestPointSubset(IReadOnlyList<CaveVector3> points, int k)
    {
        int n = points.Count;
        var all = new List<int>(Math.Min(n, k));
        if (n <= k)
        {
            for (int i = 0; i < n; i++) all.Add(i);
            return all;
        }

        var selected = new List<int> { 0 };
        var minDistSq = new double[n];
        for (int i = 0; i < n; i++) minDistSq[i] = (points[i] - points[0]).LengthSquared;

        for (int c = 1; c < k; c++)
        {
            int best = 0;
            double bestDist = -1.0;
            for (int i = 0; i < n; i++)
            {
                if (minDistSq[i] > bestDist) { bestDist = minDistSq[i]; best = i; }
            }
            selected.Add(best);
            for (int i = 0; i < n; i++)
            {
                double d = (points[i] - points[best]).LengthSquared;
                if (d < minDistSq[i]) minDistSq[i] = d;
            }
        }

        selected.Sort();
        return selected;
    }

    private static CaveVector3 Vec(SceneMetaVec v) => new(v.X, v.Y, v.Z);

    private static double Diagonal(SceneMetaBounds bounds)
    {
        var size = bounds.Size;
        double length = Math.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z);
        return length > 1e-6 ? length : 1.0;
    }
}
