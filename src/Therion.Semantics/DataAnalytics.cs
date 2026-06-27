// DATA-01/02/05/06/08 — survey-domain analytics over a WorkspaceSemanticModel.
// Pure aggregation (no UI / no disk), unit-testable, reused by the dashboard, data-quality,
// team and entrances views. Lengths/depths/extents are "preview-quality": computed from our own
// model (shot length / compass / clino), not from a full Therion/Survex adjustment. Angles are
// assumed to be degrees (the dominant case); a future units-aware pass can refine this.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Therion.Semantics;

// ---- DATA-01 -----------------------------------------------------------------

/// <summary>Headline + breakdown metrics for the statistics panel (DATA-01).</summary>
public sealed record DetailedTotals(
    int Surveys, int Stations, int Shots,
    int SplayShots, int DuplicateShots, int SurfaceShots,
    double TotalLength, double UndergroundLength, double SurfaceLength,
    double DuplicateLength, double SplayLength,
    double VerticalRange, string? HighestStation, string? LowestStation,
    double EastWestExtent, double NorthSouthExtent,
    int Entrances, int FixedPoints);

// ---- DATA-02 -----------------------------------------------------------------

/// <summary>One bar/point of a length series (by survey or by date) (DATA-02).</summary>
public sealed record LengthBucket(string Key, double Length, int Shots);

// ---- DATA-05 -----------------------------------------------------------------

/// <summary>Per-person surveying totals across the project (DATA-05).</summary>
public sealed record TeamMemberStat(string Name, int Surveys, double Length);

/// <summary>Per-date (expedition) totals across the project (DATA-05).</summary>
public sealed record ExpeditionStat(string Date, int Surveys, double Length, ImmutableArray<string> Members);

// ---- DATA-06 -----------------------------------------------------------------

/// <summary>A fixed point or entrance with its coordinates and CRS (DATA-06).</summary>
public sealed record FixedPointRow(
    string Station, double? X, double? Y, double? Z, string Cs,
    bool IsEntrance, bool IsFixed, string File, int Line);

// ---- DATA-08 -----------------------------------------------------------------

/// <summary>Data-quality counts for the dashboard (DATA-08).</summary>
public sealed record DataQualityReport(
    int TotalShots, int ZeroLength, int MissingLength, int MissingCompass, int MissingClino,
    int NoBacksight, int NoLrud, int SteepLegs, int SplayShots, int DuplicateShots,
    int UndatedSurveys, int TeamlessSurveys);

/// <summary>Pure survey-domain analytics computed from a <see cref="WorkspaceSemanticModel"/>.</summary>
public static class DataAnalytics
{
    private const double DegToRad = Math.PI / 180.0;

    // ===== DATA-01: detailed totals ==========================================

    public static DetailedTotals ComputeDetailedTotals(WorkspaceSemanticModel model)
    {
        int splay = 0, dup = 0, surf = 0, shots = 0;
        double total = 0, underground = 0, surface = 0, dupLen = 0, splayLen = 0;
        var adjacency = new Dictionary<string, List<(string To, double Dx, double Dy, double Dz)>>(StringComparer.Ordinal);

        void Link(string a, string b, double dx, double dy, double dz)
        {
            (adjacency.TryGetValue(a, out var la) ? la : adjacency[a] = new()).Add((b, dx, dy, dz));
            (adjacency.TryGetValue(b, out var lb) ? lb : adjacency[b] = new()).Add((a, -dx, -dy, -dz));
        }

        foreach (var perFile in model.PerFile.Values)
            foreach (var shot in perFile.Shots)
            {
                bool isSplay = (shot.Flags & ShotFlags.Splay) != 0;
                if (shot.Length is { } len)
                {
                    bool isDup = (shot.Flags & ShotFlags.Duplicate) != 0;
                    bool isSurf = (shot.Flags & ShotFlags.Surface) != 0;
                    if (isSplay) splayLen += len;
                    else if (isDup) dupLen += len;
                    else { total += len; if (isSurf) surface += len; else underground += len; }

                    if (!isSplay)
                    {
                        double h = shot.Clino is { } c ? len * Math.Cos(c * DegToRad) : len;
                        double dz = shot.Clino is { } c2 ? len * Math.Sin(c2 * DegToRad) : 0;
                        double dx = shot.Compass is { } b ? h * Math.Sin(b * DegToRad) : 0;
                        double dy = shot.Compass is { } b2 ? h * Math.Cos(b2 * DegToRad) : h;
                        Link(shot.From.ToString(), shot.To.ToString(), dx, dy, dz);
                    }
                }
                if (isSplay) { splay++; continue; }
                shots++;
                if ((shot.Flags & ShotFlags.Duplicate) != 0) dup++;
                if ((shot.Flags & ShotFlags.Surface) != 0) surf++;
            }

        var (range, hi, lo, ew, ns) = LayoutExtents(adjacency);

        int entrances = 0, fixedPts = 0;
        foreach (var st in model.StationsByQn.Values)
        {
            if (st.IsEntrance) entrances++;
            if (st.Kind == StationDeclarationKind.Fix) fixedPts++;
        }

        return new DetailedTotals(
            model.SurveysByFullName.Count, model.StationsByQn.Count, shots,
            splay, dup, surf,
            total, underground, surface, dupLen, splayLen,
            range, hi, lo, ew, ns, entrances, fixedPts);
    }

    // Relative 3-D layout: BFS each component from an arbitrary root, accumulate dx/dy/dz, then
    // derive the vertical range (+ extreme station ids) and the horizontal bounding box.
    private static (double Range, string? Hi, string? Lo, double EW, double NS) LayoutExtents(
        Dictionary<string, List<(string To, double Dx, double Dy, double Dz)>> adjacency)
    {
        if (adjacency.Count == 0) return (0, null, null, 0, 0);
        var pos = new Dictionary<string, (double X, double Y, double Z)>(StringComparer.Ordinal);
        double minX = 0, maxX = 0, minY = 0, maxY = 0, minZ = 0, maxZ = 0;
        string? hi = null, lo = null;
        bool any = false;

        foreach (var start in adjacency.Keys)
        {
            if (pos.ContainsKey(start)) continue;
            pos[start] = (0, 0, 0);
            var queue = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                var (x, y, z) = pos[u];
                if (!any || z > maxZ) { maxZ = z; hi = u; }
                if (!any || z < minZ) { minZ = z; lo = u; }
                minX = any ? Math.Min(minX, x) : x; maxX = any ? Math.Max(maxX, x) : x;
                minY = any ? Math.Min(minY, y) : y; maxY = any ? Math.Max(maxY, y) : y;
                any = true;
                foreach (var (v, dx, dy, dz) in adjacency[u])
                    if (!pos.ContainsKey(v)) { pos[v] = (x + dx, y + dy, z + dz); queue.Enqueue(v); }
            }
        }
        return (maxZ - minZ, hi, lo, maxX - minX, maxY - minY);
    }

    // ===== DATA-02: length series ============================================

    /// <summary>Surveyed length per survey (direct, splays/duplicates excluded), longest first.</summary>
    public static IReadOnlyList<LengthBucket> LengthBySurvey(WorkspaceSemanticModel model)
    {
        var byName = new Dictionary<string, (double Len, int Shots)>(StringComparer.Ordinal);
        var nodeNames = new HashSet<string>(model.SurveysByFullName.Keys, StringComparer.Ordinal);
        foreach (var perFile in model.PerFile.Values)
            foreach (var shot in perFile.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                var sv = SurveyOf(shot.From, nodeNames) ?? "(root)";
                var (len, n) = byName.GetValueOrDefault(sv);
                if (shot.Length is { } l && (shot.Flags & ShotFlags.Duplicate) == 0) len += l;
                byName[sv] = (len, n + 1);
            }
        return byName
            .Select(kv => new LengthBucket(kv.Key, kv.Value.Len, kv.Value.Shots))
            .OrderByDescending(b => b.Length)
            .ToList();
    }

    /// <summary>Surveyed length per survey date (from the survey <c>date</c>), chronological.</summary>
    public static IReadOnlyList<LengthBucket> LengthByDate(WorkspaceSemanticModel model)
    {
        // Map each survey to its direct length, then attribute that to the survey's first date.
        var lenBySurvey = DirectLengthBySurvey(model);
        var byDate = new Dictionary<string, (double Len, int Surveys)>(StringComparer.Ordinal);
        foreach (var sv in model.SurveysByFullName.Values)
        {
            var date = sv.Dates.IsDefaultOrEmpty ? "(undated)" : NormalizeDate(sv.Dates[0]);
            var len = lenBySurvey.GetValueOrDefault(sv.Name.ToString());
            var (l, n) = byDate.GetValueOrDefault(date);
            byDate[date] = (l + len, n + 1);
        }
        return byDate
            .Select(kv => new LengthBucket(kv.Key, kv.Value.Len, kv.Value.Surveys))
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();
    }

    // ===== DATA-05: team / expedition ========================================

    public static IReadOnlyList<TeamMemberStat> TeamMembers(WorkspaceSemanticModel model)
    {
        var lenBySurvey = DirectLengthBySurvey(model);
        var byPerson = new Dictionary<string, (int Surveys, double Len)>(StringComparer.OrdinalIgnoreCase);
        foreach (var sv in model.SurveysByFullName.Values)
        {
            if (sv.Team.IsDefaultOrEmpty) continue;
            var len = lenBySurvey.GetValueOrDefault(sv.Name.ToString());
            foreach (var person in sv.Team.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var (n, l) = byPerson.GetValueOrDefault(person);
                byPerson[person] = (n + 1, l + len);
            }
        }
        return byPerson
            .Select(kv => new TeamMemberStat(kv.Key, kv.Value.Surveys, kv.Value.Len))
            .OrderByDescending(p => p.Length).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ExpeditionStat> Expeditions(WorkspaceSemanticModel model)
    {
        var lenBySurvey = DirectLengthBySurvey(model);
        var byDate = new Dictionary<string, (int Surveys, double Len, HashSet<string> People)>(StringComparer.Ordinal);
        foreach (var sv in model.SurveysByFullName.Values)
        {
            if (sv.Dates.IsDefaultOrEmpty) continue;
            var date = NormalizeDate(sv.Dates[0]);
            var len = lenBySurvey.GetValueOrDefault(sv.Name.ToString());
            if (!byDate.TryGetValue(date, out var agg))
                agg = byDate[date] = (0, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            byDate[date] = (agg.Surveys + 1, agg.Len + len, agg.People);
            foreach (var p in sv.Team) agg.People.Add(p);
        }
        return byDate
            .Select(kv => new ExpeditionStat(kv.Key, kv.Value.Surveys, kv.Value.Len,
                kv.Value.People.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToImmutableArray()))
            .OrderBy(e => e.Date, StringComparer.Ordinal)
            .ToList();
    }

    // ===== DATA-06: entrances & fixed points =================================

    public static IReadOnlyList<FixedPointRow> FixedPoints(WorkspaceSemanticModel model)
    {
        var rows = new List<FixedPointRow>();
        foreach (var st in model.StationsByQn.Values)
        {
            bool isFixed = st.Kind == StationDeclarationKind.Fix;
            if (!isFixed && !st.IsEntrance) continue;
            rows.Add(new FixedPointRow(
                st.Name.ToString(), st.FixX, st.FixY, st.FixZ, st.Cs ?? string.Empty,
                st.IsEntrance, isFixed, st.DeclarationSpan.FilePath, st.DeclarationSpan.Start.Line));
        }
        return rows
            .OrderByDescending(r => r.IsFixed).ThenBy(r => r.Station, StringComparer.Ordinal)
            .ToList();
    }

    // ===== DATA-08: data quality =============================================

    public static DataQualityReport DataQuality(WorkspaceSemanticModel model)
    {
        int total = 0, zero = 0, noLen = 0, noComp = 0, noClino = 0, noBack = 0, noLrud = 0,
            steep = 0, splay = 0, dup = 0;
        foreach (var perFile in model.PerFile.Values)
            foreach (var shot in perFile.Shots)
            {
                total++;
                if ((shot.Flags & ShotFlags.Splay) != 0) splay++;
                if ((shot.Flags & ShotFlags.Duplicate) != 0) dup++;
                if (shot.Length is { } l && l == 0) zero++;
                if (shot.Length is null) noLen++;
                if (shot.Compass is null && (shot.Flags & ShotFlags.Splay) == 0) noComp++;
                if (shot.Clino is null && (shot.Flags & ShotFlags.Splay) == 0) noClino++;
                if (shot.Clino is { } c && Math.Abs(c) >= 80 && Math.Abs(c) < 90) steep++;
                var fields = shot.FieldDefinition?.Fields ?? ImmutableArray<string>.Empty;
                if (!HasBacksight(fields)) noBack++;
                if (!HasLrud(fields)) noLrud++;
            }

        int undated = 0, teamless = 0;
        foreach (var sv in model.SurveysByFullName.Values)
        {
            if (sv.Dates.IsDefaultOrEmpty) undated++;
            if (sv.Team.IsDefaultOrEmpty) teamless++;
        }

        return new DataQualityReport(total, zero, noLen, noComp, noClino, noBack, noLrud,
            steep, splay, dup, undated, teamless);
    }

    private static bool HasBacksight(ImmutableArray<string> fields)
    {
        foreach (var f in fields)
            if (f.StartsWith("back", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool HasLrud(ImmutableArray<string> fields)
    {
        bool l = false, r = false, u = false, d = false;
        foreach (var f in fields)
            switch (f.ToLowerInvariant())
            {
                case "left": l = true; break;
                case "right": r = true; break;
                case "up" or "ceiling": u = true; break;
                case "down" or "floor": d = true; break;
            }
        return l && r && u && d;
    }

    // ===== shared helpers ====================================================

    /// <summary>Direct (non-rolled-up) surveyed length per survey, splays/duplicates excluded.</summary>
    public static Dictionary<string, double> DirectLengthBySurvey(WorkspaceSemanticModel model)
    {
        var nodeNames = new HashSet<string>(model.SurveysByFullName.Keys, StringComparer.Ordinal);
        var directLength = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var perFile in model.PerFile.Values)
            foreach (var shot in perFile.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                var sv = SurveyOf(shot.From, nodeNames);
                if (sv is null) continue;
                if (shot.Length is { } len && (shot.Flags & ShotFlags.Duplicate) == 0)
                    directLength[sv] = directLength.GetValueOrDefault(sv) + len;
            }
        return directLength;
    }

    private static string? SurveyOf(QualifiedName station, HashSet<string> nodeNames)
    {
        var qn = station;
        while (qn.HasParent)
        {
            qn = qn.Parent();
            var name = qn.ToString();
            if (nodeNames.Contains(name)) return name;
        }
        return null;
    }

    // Normalize a Therion date to its YYYY[.MM[.DD]] head (drop time + interval tail) for grouping.
    // The tokenizer splits "2024.07.01" into "2024.07"+".01", so the raw value can contain artifact
    // spaces around the dots — strip whitespace from the head after isolating it.
    private static string NormalizeDate(string raw)
    {
        var s = raw.Trim();
        int dash = s.IndexOf('-');           // interval "a - b" → take the start
        if (dash > 0) s = s[..dash];
        int at = s.IndexOf('@');             // drop time-of-day
        if (at > 0) s = s[..at];
        s = s.Replace(" ", string.Empty).Replace("\t", string.Empty);
        return s.Length == 0 ? "(undated)" : s;
    }
}
