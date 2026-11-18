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

    /// <summary>Set when the lead markers grow with their distance from the camera
    /// (<see cref="LeadMarkerSizing.CameraDistance"/>): the emitter drives their scale, and a
    /// caption grows with its marker and stays above it.</summary>
    public LeadDistanceSizing? LeadsByDistance { get; init; }

    public bool IsEmpty => Stations.Count == 0 && Components.Count == 0 && Leads.Count == 0;
}

/// <summary>A lead marker's radius as <paramref name="Factor"/> × its distance from the camera,
/// kept within [<paramref name="MinRadius"/>, <paramref name="MaxRadius"/>] metres.</summary>
public sealed record LeadDistanceSizing(double Factor, double MinRadius, double MaxRadius);

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

        var stations = PlanStations(labels.Stations, labels.Roof, meta, diagonal, out bool capped, out int matched);
        var components = labels.Components.Show ? PlanComponents(labels.Components, meta, diagonal) : [];
        bool inside = spec.Camera.Template == CameraTemplate.Flythrough;
        var leads = labels.Leads.Show ? PlanLeads(labels.Leads, labels.Roof, meta, diagonal, inside) : [];
        var byDistance = leads.Count > 0 && labels.Leads.Sizing == LeadMarkerSizing.CameraDistance
            ? new LeadDistanceSizing(LeadDistanceFactor * labels.Leads.MarkerScale,
                MinInsideRadius * labels.Leads.MarkerScale, diagonal * LeadRadiusFraction * labels.Leads.MarkerScale)
            : null;

        return new LabelPlan
        {
            Stations = stations,
            Components = components,
            Leads = leads,
            LeadsByDistance = byDistance,
            StationsCapped = capped,
            StationMatchCount = matched,
        };
    }

    private static IReadOnlyList<LabelItem> PlanStations(
        StationLabelSpec spec, LabelRoofSource roofSource, SceneMeta meta, double diagonal, out bool capped, out int matched)
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
            result.Add(new LabelItem(s.Name, AboveTheRoof(s, RoofOf(s, roofSource), size), size));
        return result;
    }

    /// <summary>A station sits inside its passage, where the walls hide its label from any
    /// camera outside; the label goes just above the passage roof instead, its lower edge clear
    /// of it. A station with no roof above it keeps its label on the station.</summary>
    private static CaveVector3 AboveTheRoof(SceneMetaStation s, double? roof, double size)
    {
        var at = Vec(s.Position);
        return roof is { } r ? at + new CaveVector3(0.0, 0.0, r + size * RoofClearanceFactor) : at;
    }

    /// <summary>The station's roof from the chosen source. A document written before the two
    /// sources were kept apart has only <see cref="SceneMetaStation.Roof"/>, which is used for
    /// every source but <see cref="LabelRoofSource.None"/>.</summary>
    internal static double? RoofOf(SceneMetaStation s, LabelRoofSource source)
    {
        if (source == LabelRoofSource.None) return null;
        if (s.WallRoof is null && s.SurveyedRoof is null) return s.Roof;
        return source switch
        {
            LabelRoofSource.WallsFirst => s.WallRoof ?? s.SurveyedRoof,
            LabelRoofSource.WallsOnly => s.WallRoof,
            _ => s.SurveyedRoof ?? s.WallRoof,
        };
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

    /// <summary>Lead markers, sized as <see cref="LeadMarkerSpec.Sizing"/> says. From the cave: a
    /// fraction of the cave's diagonal, so a marker shows from outside it. From the passage: a
    /// fraction of the roof height at its station, so a camera inside does not fly into a ball
    /// wider than the passage; never larger than the cave size. By camera distance: planned at
    /// the cave size, which the emitter's driver then scales.</summary>
    private static IReadOnlyList<LeadItem> PlanLeads(
        LeadMarkerSpec spec, LabelRoofSource roofSource, SceneMeta meta, double diagonal, bool inside)
    {
        if (inside && spec.Sizing == LeadMarkerSizing.HiddenInFlythrough) return [];
        bool byPassage = spec.Sizing == LeadMarkerSizing.Passage || (spec.Sizing == LeadMarkerSizing.Auto && inside);

        double caveRadius = diagonal * LeadRadiusFraction * spec.MarkerScale;
        double caveTextSize = diagonal * StationSizeFraction;
        var roofs = new Dictionary<string, double?>(StringComparer.Ordinal);
        if (byPassage)
            foreach (var s in meta.Stations) roofs.TryAdd(s.Name, RoofOf(s, roofSource));

        var result = new List<LeadItem>(meta.Leads.Count);
        foreach (var lead in meta.Leads)
        {
            string text = !string.IsNullOrWhiteSpace(lead.Note) ? lead.Note! : lead.Station;
            double radius = caveRadius, textSize = caveTextSize;
            if (byPassage)
            {
                double roof = roofs.GetValueOrDefault(lead.Station) ?? UnknownPassageRoof;
                double passageRadius = Math.Max(MinInsideRadius, roof * LeadRoofFraction) * spec.MarkerScale;
                radius = Math.Min(caveRadius, passageRadius);
                textSize = Math.Min(caveTextSize, radius);
            }
            result.Add(new LeadItem(text, Vec(lead.Position), radius, spec.ShowText, textSize) { Station = lead.Station });
        }
        return result;
    }

    /// <summary>A camera-distance marker's radius per metre of distance: with a 50 mm lens on a
    /// 36 mm sensor the ball is about 4 % of the picture's width across.</summary>
    private const double LeadDistanceFactor = 0.015;

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
