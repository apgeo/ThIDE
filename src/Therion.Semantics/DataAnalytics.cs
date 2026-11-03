// survey-domain analytics over a WorkspaceSemanticModel.
// Pure aggregation (no UI / no disk), unit-testable, reused by the dashboard, data-quality,
// team and entrances views. Lengths/depths/extents are "preview-quality": computed from our own
// model (shot length / compass / clino), not from a full Therion/Survex adjustment. The binder
// stores readings in metres and degrees whatever `units` the data was written in.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Therion.Semantics;

// ---- -----------------------------------------------------------------

/// <summary>Headline + breakdown metrics for the statistics panel.</summary>
public sealed record DetailedTotals(
    int Surveys, int Stations, int Shots,
    int SplayShots, int DuplicateShots, int SurfaceShots,
    double TotalLength, double UndergroundLength, double SurfaceLength,
    double DuplicateLength, double SplayLength,
    double VerticalRange, string? HighestStation, string? LowestStation,
    double EastWestExtent, double NorthSouthExtent,
    int Entrances, int FixedPoints);

// ---- -----------------------------------------------------------------

/// <summary>One bar/point of a length series (by survey or by date).</summary>
public sealed record LengthBucket(string Key, double Length, int Shots);

// ---- -----------------------------------------------------------------

/// <summary>Per-person surveying totals across the project.</summary>
public sealed record TeamMemberStat(string Name, int Surveys, double Length);

/// <summary>Per-date (trip) totals across the project.</summary>
public sealed record TripStat(string Date, int Surveys, double Length, ImmutableArray<string> Members);

// ---- -----------------------------------------------------------------

/// <summary>A fixed point or entrance with its coordinates and CRS.</summary>
public sealed record FixedPointRow(
    string Station, double? X, double? Y, double? Z, string Cs,
    bool IsEntrance, bool IsFixed, string File, int Line, Therion.Core.SourceSpan Span);

// ---- -----------------------------------------------------------------

/// <summary>Data-quality counts for the dashboard. <see cref="TotalShots"/> counts every shot; the leg
/// metrics (<see cref="TotalLegs"/> through <see cref="SteepLegs"/>) leave splays out, which are
/// counted on their own in <see cref="SplayShots"/>. <see cref="DuplicateShots"/> covers every shot.</summary>
public sealed record DataQualityReport(
    int TotalShots, int ZeroLength, int MissingLength, int MissingCompass, int MissingClino,
    int NoBacksight, int NoLrud, int SteepLegs, int SplayShots, int DuplicateShots,
    int UndatedSurveys, int TeamlessSurveys, int TotalLegs);

/// <summary>Pure survey-domain analytics computed from a <see cref="WorkspaceSemanticModel"/>.</summary>
public static class DataAnalytics
{
    // ===== : detailed totals ==========================================

    public static DetailedTotals ComputeDetailedTotals(WorkspaceSemanticModel model)
    {
        int splay = 0, dup = 0, surf = 0, shots = 0;
        double total = 0, underground = 0, surface = 0, dupLen = 0, splayLen = 0;

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
                }
                if (isSplay) { splay++; continue; }
                shots++;
                if ((shot.Flags & ShotFlags.Duplicate) != 0) dup++;
                if ((shot.Flags & ShotFlags.Surface) != 0) surf++;
            }

        var (range, hi, lo, ew, ns) = LayoutExtents(model);

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

    /// <summary>
    /// Vertical range (with its highest/lowest station) and horizontal extents, read off the
    /// equate-aware position estimate. Positions are local to each connected piece, so spans are
    /// measured within a piece, never across two: the range and its extremes come from the piece with
    /// the greatest vertical span, and each horizontal extent is the widest piece's on that axis.
    /// </summary>
    internal static (double Range, string? Hi, string? Lo, double EW, double NS) LayoutExtents(
        WorkspaceSemanticModel model)
    {
        var positions = StationPositionEstimator.Get(model).Positions;
        if (positions.Count == 0) return (0, null, null, 0, 0);

        var pieces = new Dictionary<int, PieceExtent>();
        // Every equate alias carries the same position: visit real names before `x@survey`
        // placeholders, then ordinal, so an extreme is labelled by a real station, deterministically.
        foreach (var (name, p) in positions
                     .OrderBy(kv => kv.Key.Last.Contains('@'))
                     .ThenBy(kv => kv.Key.ToString(), StringComparer.Ordinal))
        {
            if (!pieces.TryGetValue(p.ComponentId, out var e))
            {
                pieces[p.ComponentId] = new PieceExtent(name.ToString(), p);
                continue;
            }
            if (p.Z > e.MaxZ) { e.MaxZ = p.Z; e.Hi = name.ToString(); }
            if (p.Z < e.MinZ) { e.MinZ = p.Z; e.Lo = name.ToString(); }
            e.MinX = Math.Min(e.MinX, p.X); e.MaxX = Math.Max(e.MaxX, p.X);
            e.MinY = Math.Min(e.MinY, p.Y); e.MaxY = Math.Max(e.MaxY, p.Y);
        }

        var deepest = pieces
            .OrderByDescending(kv => kv.Value.MaxZ - kv.Value.MinZ).ThenBy(kv => kv.Key)
            .First().Value;
        return (deepest.MaxZ - deepest.MinZ, deepest.Hi, deepest.Lo,
            pieces.Values.Max(e => e.MaxX - e.MinX), pieces.Values.Max(e => e.MaxY - e.MinY));
    }

    private sealed class PieceExtent(string first, StationPosition p)
    {
        public double MinX = p.X, MaxX = p.X, MinY = p.Y, MaxY = p.Y, MinZ = p.Z, MaxZ = p.Z;
        public string Hi = first, Lo = first;
    }

    // ===== : length series ============================================

    /// <summary>Surveyed length per survey (direct, splay/duplicate/surface legs excluded), longest first.</summary>
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
                if (shot.Length is { } l && (shot.Flags & (ShotFlags.Duplicate | ShotFlags.Surface)) == 0) len += l;
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

    // ===== : team / trip ==============================================

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

    public static IReadOnlyList<TripStat> Trips(WorkspaceSemanticModel model)
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
            .Select(kv => new TripStat(kv.Key, kv.Value.Surveys, kv.Value.Len,
                kv.Value.People.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToImmutableArray()))
            .OrderBy(e => e.Date, StringComparer.Ordinal)
            .ToList();
    }

    // ===== : entrances & fixed points =================================

    public static IReadOnlyList<FixedPointRow> FixedPoints(WorkspaceSemanticModel model)
    {
        var rows = new List<FixedPointRow>();
        foreach (var st in model.StationsByQn.Values)
        {
            bool isFixed = st.Kind == StationDeclarationKind.Fix;
            if (!isFixed && !st.IsEntrance) continue;
            rows.Add(new FixedPointRow(
                st.Name.ToString(), st.FixX, st.FixY, st.FixZ, st.Cs ?? string.Empty,
                st.IsEntrance, isFixed, st.DeclarationSpan.FilePath, st.DeclarationSpan.Start.Line,
                st.DeclarationSpan));
        }
        return rows
            .OrderByDescending(r => r.IsFixed).ThenBy(r => r.Station, StringComparer.Ordinal)
            .ToList();
    }

    // ===== : data quality =============================================

    /// <param name="surveyPrefix">When set, only shots and surveys at or under this survey path are
    /// counted (e.g. "cave.upper") — the rest of the project is ignored.</param>
    public static DataQualityReport DataQuality(WorkspaceSemanticModel model, string? surveyPrefix = null)
    {
        string? prefixDot = string.IsNullOrEmpty(surveyPrefix) ? null : surveyPrefix + ".";
        bool ShotIn(ShotSymbol s) => prefixDot is null || s.From.ToString().StartsWith(prefixDot, StringComparison.Ordinal);
        bool SurveyIn(SurveySymbol s) =>
            surveyPrefix is not { Length: > 0 }
            || s.Name.ToString() == surveyPrefix || s.Name.ToString().StartsWith(prefixDot!, StringComparison.Ordinal);

        int total = 0, legs = 0, zero = 0, noLen = 0, noComp = 0, noClino = 0, noBack = 0, noLrud = 0,
            steep = 0, splay = 0, dup = 0;
        foreach (var perFile in model.PerFile.Values)
            foreach (var shot in perFile.Shots)
            {
                if (!ShotIn(shot)) continue;
                total++;
                if (DataQualityChecks.IsDuplicate(shot)) dup++;
                // A splay carries no LRUD and is often steep by design: counting it among the legs
                // swamped every leg metric on a project with thousands of splays.
                if (DataQualityChecks.IsSplay(shot)) { splay++; continue; }
                legs++;
                if (DataQualityChecks.IsZeroLength(shot)) zero++;
                if (DataQualityChecks.IsMissingLength(shot)) noLen++;
                if (DataQualityChecks.IsMissingCompass(shot)) noComp++;
                if (DataQualityChecks.IsMissingClino(shot)) noClino++;
                if (DataQualityChecks.IsSteep(shot)) steep++;
                if (DataQualityChecks.HasNoBacksight(shot)) noBack++;
                if (DataQualityChecks.HasNoLrud(shot)) noLrud++;
            }

        int undated = 0, teamless = 0;
        foreach (var sv in model.SurveysByFullName.Values)
        {
            if (!SurveyIn(sv)) continue;
            if (DataQualityChecks.IsUndated(sv)) undated++;
            if (DataQualityChecks.IsTeamless(sv)) teamless++;
        }

        return new DataQualityReport(total, zero, noLen, noComp, noClino, noBack, noLrud,
            steep, splay, dup, undated, teamless, legs);
    }

    // ===== shared helpers ====================================================

    /// <summary>Direct (non-rolled-up) surveyed length per survey, splay/duplicate/surface legs excluded.</summary>
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
                if (shot.Length is { } len && (shot.Flags & (ShotFlags.Duplicate | ShotFlags.Surface)) == 0)
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

/// <summary>
/// Row-level data-quality predicates over a single shot or survey. Shared by the dashboard counts
/// (<see cref="DataAnalytics.DataQuality"/>) and the Object Browser drill-down filters, so a metric's
/// count and the rows it filters to always describe the same set.
/// </summary>
public static class DataQualityChecks
{
    public static bool IsSplay(ShotSymbol s) => (s.Flags & ShotFlags.Splay) != 0;
    /// <summary>A survey leg: any shot that is not a splay. The leg metrics count only these.</summary>
    public static bool IsLeg(ShotSymbol s) => !IsSplay(s);
    public static bool IsDuplicate(ShotSymbol s) => (s.Flags & ShotFlags.Duplicate) != 0;
    public static bool IsZeroLength(ShotSymbol s) => s.Length is { } l && l == 0;
    public static bool IsMissingLength(ShotSymbol s) => s.Length is null;
    // A plumbed leg (clino ±90°) has no bearing to read, so a blank compass there is not missing.
    public static bool IsMissingCompass(ShotSymbol s) => s.Compass is null && !IsSplay(s) && !IsPlumb(s);
    public static bool IsMissingClino(ShotSymbol s) => s.Clino is null && !IsSplay(s);
    public static bool IsPlumb(ShotSymbol s) => s.Clino is { } c && Math.Abs(c) == 90;
    public static bool IsSteep(ShotSymbol s) => s.Clino is { } c && Math.Abs(c) >= 80 && Math.Abs(c) < 90;
    public static bool HasNoBacksight(ShotSymbol s) => !HasBacksight(Fields(s));
    public static bool HasNoLrud(ShotSymbol s) => !HasLrud(Fields(s));

    public static bool IsUndated(SurveySymbol s) => s.Dates.IsDefaultOrEmpty;
    public static bool IsTeamless(SurveySymbol s) => s.Team.IsDefaultOrEmpty;

    private static ImmutableArray<string> Fields(ShotSymbol s) =>
        s.FieldDefinition?.Fields ?? ImmutableArray<string>.Empty;

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
}
