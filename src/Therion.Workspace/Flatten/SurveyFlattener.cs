// Whole project → one survey, one centreline. Reads the project (ProjectReader), resolves every station
// name the way Therion does, merges equated stations into one, works out the magnetic declination of
// every shot, thins the splays if asked, names the stations and writes the file (SingleThWriter).

using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Workspace.Flatten;

public static class SurveyFlattener
{
    /// <summary>
    /// Merges the project whose entry point is <paramref name="entryPath"/> (a <c>.thconfig</c> or a
    /// <c>.th</c>) into the text of a single <c>.th</c> file. Never throws on bad data: problems come
    /// back in <see cref="FlattenResult.Diagnostics"/>, and the text is written from what could be read.
    /// </summary>
    public static FlattenResult Flatten(string entryPath, FlattenOptions? options = null, FlattenFileSource? files = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        options ??= new FlattenOptions();
        var project = ProjectReader.Read(entryPath, files ?? FlattenFileSource.Disk);
        return new Merge(project, options, Path.GetFullPath(entryPath)).Run();
    }

    private sealed class Merge(FlatProject p, FlattenOptions o, string entryPath)
    {
        private readonly Dictionary<StationId, StationId> _parent = new();
        private readonly List<StationId> _order = new();
        private readonly Dictionary<StationId, int> _rank = new();
        private readonly Dictionary<RawStation, StationId?> _resolved = new();

        public FlattenResult Run()
        {
            CheckOptions();
            ResolveStations();
            var classes = Classes();
            var declinations = ApplyDeclinations();
            var (kept, dropped) = ThinSplays();
            var names = StationNamer.Name(classes, o, p);
            string Name(StationId id) => names[Find(id)];

            int components = Components(classes);
            bool fixesNeeded = !o.Standalone && components > 1 && p.Fixes.Count > 0;
            if (fixesNeeded)
                p.Report(FlattenDiagnosticCodes.NotCarried, DiagnosticSeverity.Info,
                    $"The merged centreline has {components} pieces that only their fixed points place; the fixed points are written so that Therion can compile it.",
                    SourceSpan.None);
            if (!o.Standalone && MostFixesInOnePiece > 1)
                p.Report(FlattenDiagnosticCodes.NotCarried, DiagnosticSeverity.Info,
                    "Several fixed points hold the same piece of the project in place; without them (they come with the standalone option) "
                    + "Therion spreads the loop errors differently, so stations can sit a little apart from where the project puts them.",
                    SourceSpan.None);
            if (components > 1 && p.Fixes.Count == 0)
                p.Report(FlattenDiagnosticCodes.NotCarried, DiagnosticSeverity.Warning,
                    $"The merged centreline has {components} unconnected pieces and no fixed points; Therion cannot place all of them.",
                    SourceSpan.None);

            var writer = new SingleThWriter(p, o, Name, Path.GetFileName(entryPath), SurveyName(), SurveyTitle()) { FixesNeeded = fixesNeeded }.WithIds(_resolved);
            var text = writer.Write(dropped);

            var stationNames = _order
                .Select(id => new FlattenStationName(id.TherionName, Name(id)))
                .ToImmutableArray();
            var stats = new FlattenStatistics(
                Files: p.Files.Count,
                Centrelines: p.Centrelines.Count,
                Shots: p.Legs.Count(l => !l.IsSplay),
                SplaysKept: kept,
                SplaysDropped: dropped,
                Stations: writer.StationsWritten,
                EquatesMerged: _order.Count - classes.Count,
                FixedPoints: p.Fixes.Count,
                StationComments: p.StationCommands.Count,
                Components: components);
            return new FlattenResult(text, [.. p.Diagnostics], stats, stationNames, declinations) { ConfigCs = p.ConfigCs };
        }

        private void CheckOptions()
        {
            if (o.SplayKeepPercent is < 0 or > 100 || double.IsNaN(o.SplayKeepPercent))
                p.Report(FlattenDiagnosticCodes.BadOption, DiagnosticSeverity.Error, "The share of splays to keep must be 0–100 %.", SourceSpan.None);
            if (o.MaxSplaysPerStation is < 0)
                p.Report(FlattenDiagnosticCodes.BadOption, DiagnosticSeverity.Error, "The number of splays per station cannot be negative.", SourceSpan.None);
            if (!StationNamer.IsNamePart(o.Prefix, allowEmpty: true) || o.Prefix.StartsWith('-'))
                p.Report(FlattenDiagnosticCodes.BadOption, DiagnosticSeverity.Error,
                    $"The station prefix '{o.Prefix}' has a character Therion does not allow in a station name.", SourceSpan.None);
            if (!StationNamer.IsNamePart(o.Separator, allowEmpty: false))
                p.Report(FlattenDiagnosticCodes.BadOption, DiagnosticSeverity.Error,
                    $"The separator '{o.Separator}' has a character Therion does not allow in a station name.", SourceSpan.None);
            if (o.SurveyName is { } n && (n.Length == 0 || !TherionIdentifiers.IsValid(n) || n.Contains('@') || n.Contains('.')))
                p.Report(FlattenDiagnosticCodes.BadOption, DiagnosticSeverity.Error, $"'{n}' is not a usable survey name.", SourceSpan.None);
        }

        // ---- names --------------------------------------------------------------------------------

        private StationId? Resolve(RawStation raw)
        {
            if (_resolved.TryGetValue(raw, out var known)) return known;
            return _resolved[raw] = ResolveNew(raw);
        }

        private StationId? ResolveNew(RawStation raw)
        {
            var text = raw.Text;
            int at = text.IndexOf('@');
            string point = at < 0 ? text : text[..at];
            var node = raw.Scope;
            if (at >= 0)
            {
                var parts = text[(at + 1)..].Split('.');
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (!node.Children.TryGetValue(parts[i], out var child))
                    {
                        p.Report(FlattenDiagnosticCodes.UnknownSurvey, DiagnosticSeverity.Error,
                            $"Station '{text}' names a survey that does not exist (seen from '{(raw.Scope.IsRoot ? "the top level" : raw.Scope.TherionName)}').",
                            raw.Span);
                        return null;
                    }
                    node = child;
                }
            }
            var id = new StationId(node.Namespace, point);
            if (_rank.TryAdd(id, _order.Count)) { _order.Add(id); _parent[id] = id; }
            return id;
        }

        private StationId Find(StationId id)
        {
            while (!_parent[id].Equals(id))
            {
                _parent[id] = _parent[_parent[id]];
                id = _parent[id];
            }
            return id;
        }

        private void Union(StationId a, StationId b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra.Equals(rb)) return;
            // The station seen first stays the representative: its name is the one kept.
            if (_rank[ra] <= _rank[rb]) _parent[rb] = ra; else _parent[ra] = rb;
        }

        private void ResolveStations()
        {
            foreach (var leg in p.Legs)
            {
                if (leg.From is { } f) leg.FromId = Resolve(f);
                if (leg.To is { } t) leg.ToId = Resolve(t);
            }
            foreach (var fix in p.Fixes) fix.Id = Resolve(fix.Station);
            foreach (var eq in p.Equates)
            {
                StationId? first = null;
                foreach (var s in eq.Stations)
                {
                    if (Resolve(s) is not { } id) continue;
                    if (first is null) first = id; else Union(first, id);
                }
            }
            foreach (var st in p.StationCommands) st.Id = Resolve(st.Station);
            foreach (var mark in p.Marks) foreach (var s in mark.Stations) Resolve(s);
            foreach (var ex in p.Extends) foreach (var s in ex.Stations) Resolve(s);
            foreach (var d in p.DimsRows) Resolve(d.Station);
            if (p.Root.Children.Count == 1 && p.Root.Children.Values.First().Entrance is { } entrance) Resolve(entrance);
        }

        /// <summary>The merged stations, representatives in the order the project first names them.</summary>
        private List<StationClass> Classes()
        {
            var byRoot = new Dictionary<StationId, StationClass>();
            var list = new List<StationClass>();
            foreach (var id in _order)
            {
                var root = Find(id);
                if (!byRoot.TryGetValue(root, out var c))
                {
                    byRoot[root] = c = new StationClass(root);
                    list.Add(c);
                }
                c.Members.Add(id);
            }
            return list;
        }

        public StationId? IdOf(RawStation raw) => Resolve(raw) is { } id ? Find(id) : null;

        private int MostFixesInOnePiece;

        private int Components(List<StationClass> classes)
        {
            var uf = classes.ToDictionary(c => c.Representative, c => c.Representative);
            StationId F(StationId x) { while (!uf[x].Equals(x)) x = uf[x] = uf[uf[x]]; return x; }
            foreach (var leg in p.Legs)
                if (!leg.Dropped && leg.FromId is { } a && leg.ToId is { } b)
                {
                    var ra = F(Find(a)); var rb = F(Find(b));
                    if (!ra.Equals(rb)) uf[ra] = rb;
                }
            var pieces = new HashSet<StationId>();
            foreach (var leg in p.Legs.Where(l => !l.Dropped))
            {
                if (leg.FromId is { } a) pieces.Add(F(Find(a)));
                if (leg.ToId is { } b) pieces.Add(F(Find(b)));
            }
            var fixesPerPiece = new Dictionary<StationId, int>();
            foreach (var fix in p.Fixes)
                if (fix.Id is { } id && uf.ContainsKey(Find(id)))
                {
                    var piece = F(Find(id));
                    fixesPerPiece[piece] = fixesPerPiece.GetValueOrDefault(piece) + 1;
                }
            MostFixesInOnePiece = fixesPerPiece.Values.DefaultIfEmpty(0).Max();
            return pieces.Count;
        }

        private string SurveyName()
        {
            if (o.SurveyName is { Length: > 0 } n) return n;
            if (p.Root.Children.Count == 1) return p.Root.Children.Values.First().Name;
            // A configuration called just "thconfig" says nothing; its folder usually names the cave.
            var stem = Path.GetFileNameWithoutExtension(entryPath);
            if (stem.Equals("thconfig", StringComparison.OrdinalIgnoreCase))
                stem = Path.GetFileName(Path.GetDirectoryName(entryPath) ?? "") ?? "";
            stem = new string(stem.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray()).Trim('-', '_');
            return stem.Length == 0 ? "merged" : stem;
        }

        private string? SurveyTitle() =>
            o.Title ?? (p.Root.Children.Count == 1 ? p.Root.Children.Values.First().Title : null);

        // ---- declination --------------------------------------------------------------------------

        private ImmutableArray<FlattenDeclinationUse> ApplyDeclinations()
        {
            var location = Location();
            double? minYear = p.Centrelines.Where(c => c.StartYear is not null).Select(c => c.StartYear!.Value)
                .DefaultIfEmpty(double.NaN).Min() is var m && !double.IsNaN(m) ? m : null;
            // Therion works out a date's declination only for a project fixed in a global coordinate
            // system; a position given for the declination applies it to any project.
            bool georeferenced = p.Fixes.Any(f => IsGlobal(f.Cs)) || o.DeclinationLocation is not null;
            if (!georeferenced && o.ApplyDeclination && p.Centrelines.Any(c => c.StartYear is not null))
                p.Report(FlattenDiagnosticCodes.DeclinationNote, DiagnosticSeverity.Info,
                    "The project has no fixed point in a global coordinate system, so (as in Therion) its dates give no declination; "
                    + "give the cave's position to apply the declination of each survey's date anyway.", SourceSpan.None);
            bool reportedUnknown = false, reportedUndated = false;
            var igrf = IgrfModel.Default;

            double? Igrf(double year, SourceSpan span)
            {
                if (location is not { } loc)
                {
                    if (!reportedUnknown)
                        p.Report(FlattenDiagnosticCodes.DeclinationUnknown, DiagnosticSeverity.Error,
                            "The declination of the dated surveys cannot be worked out: ThIDE cannot place the fixed points on the globe "
                            + $"(coordinate system {string.Join(", ", p.Fixes.Where(f => IsGlobal(f.Cs)).Select(f => f.Cs).Distinct())}). "
                            + "Give the cave's position (latitude and longitude) for the declination.", span);
                    reportedUnknown = true;
                    return null;
                }
                if (year < igrf.FirstYear)
                {
                    p.Report(FlattenDiagnosticCodes.DeclinationUnknown, DiagnosticSeverity.Error,
                        $"A survey dated {year:0.##} is older than the magnetic model ({igrf.FirstYear:0}); Therion needs its declination written out too.", span);
                    return null;
                }
                if (year > igrf.PredictedUntil + 10)
                {
                    p.Report(FlattenDiagnosticCodes.DeclinationUnknown, DiagnosticSeverity.Error,
                        $"A survey dated {year:0.##} is too far past the magnetic model ({igrf.PredictedUntil:0}).", span);
                    return null;
                }
                if (year > igrf.PredictedUntil)
                    p.Report(FlattenDiagnosticCodes.DeclinationNote, DiagnosticSeverity.Warning,
                        $"A survey dated {year:0.##} is past the period the magnetic model predicts ({igrf.PredictedUntil:0}); its declination is extrapolated.", span);
                return igrf.Declination(loc.Latitude, loc.Longitude, 0.0, year);
            }

            var uses = new List<FlattenDeclinationUse>();
            foreach (var group in p.Legs.GroupBy(l => l.Centreline))
            {
                var cl = group.Key;
                double? first = null;
                var firstSource = FlattenDeclinationSource.None;
                int count = 0;
                // The centreline's own declination is per shot; the survey's and the date's are one value.
                double? surveyValue = null, dated = null;
                bool surveyResolved = false, datedResolved = false;
                foreach (var leg in group)
                {
                    count++;
                    double value = 0;
                    var source = FlattenDeclinationSource.None;
                    if (!o.ApplyDeclination) { }
                    else if (!double.IsNaN(leg.DeclaredDeclination))
                    {
                        value = leg.DeclaredDeclination;
                        source = FlattenDeclinationSource.Centreline;
                        if (leg.GridNorth)
                        {
                            if (location is { } loc && Convergence(leg.GridCs, loc) is { } mc) value -= mc;
                            else if (location is not null)
                                p.Report(FlattenDiagnosticCodes.DeclinationNote, DiagnosticSeverity.Warning,
                                    $"The grid convergence of '{leg.GridCs}' is not known to ThIDE; the grid-angle is taken as a true-north declination.", leg.Span);
                        }
                    }
                    else
                    {
                        if (!surveyResolved)
                        {
                            surveyResolved = true;
                            for (var s = cl.Survey; s is not null; s = s.Parent)
                                if (s.Declination is { } sd)
                                {
                                    surveyValue = sd.Evaluate(cl.AverageYear, out bool middle);
                                    if (middle)
                                        p.Report(FlattenDiagnosticCodes.DeclinationNote, DiagnosticSeverity.Warning,
                                            $"No declination for undated survey data; the survey's average ({surveyValue:0.00}°) is used, as Therion does.", cl.Span);
                                    break;
                                }
                        }
                        if (surveyValue is { } sv) { value = sv; source = FlattenDeclinationSource.Survey; }
                        else if (georeferenced)
                        {
                            if (!datedResolved)
                            {
                                datedResolved = true;
                                if (cl.AverageYear is { } y) dated = Igrf(y, cl.Span);
                                else if (minYear is { } my)
                                {
                                    dated = Igrf(my, cl.Span);
                                    if (!reportedUndated)
                                        p.Report(FlattenDiagnosticCodes.DeclinationNote, DiagnosticSeverity.Info,
                                            $"Undated surveys take the declination of the earliest survey date ({Math.Floor(my):0}), as Therion does.", cl.Span);
                                    reportedUndated = true;
                                }
                            }
                            if (dated is { } d)
                            {
                                value = d;
                                source = cl.AverageYear is null ? FlattenDeclinationSource.IgrfUndated : FlattenDeclinationSource.Igrf;
                            }
                        }
                    }
                    leg.AppliedDeclination = value;
                    leg.DeclinationSource = source;
                    if (first is null) { first = value; firstSource = source; }
                }
                uses.Add(new FlattenDeclinationUse(cl.Survey.TherionName, cl.Span, cl.DateText,
                    cl.AverageYear, first ?? 0, firstSource, count));
            }
            return [.. uses];
        }

        private static bool IsGlobal(string? cs) => cs is { Length: > 0 } && !cs.Equals("local", StringComparison.OrdinalIgnoreCase);

        /// <summary>Where the declination is evaluated: the option, else the mean of the fixed points (as Therion).</summary>
        private (double Latitude, double Longitude)? Location()
        {
            if (o.DeclinationLocation is { } given) return given;
            double sumLat = 0, sumLon = 0;
            int n = 0;
            foreach (var fix in p.Fixes)
            {
                if (!IsGlobal(fix.Cs) || fix.Values.Length < 2) continue;
                if (TherionArgs.ParseDms(fix.Values[0], out var x) != NumberKind.Number) continue;
                if (TherionArgs.ParseDms(fix.Values[1], out var y) != NumberKind.Number) continue;
                if (!CoordinateTransform.TryToWgs84(fix.Cs, x, y, out var ll)) continue;
                sumLat += ll.Lat; sumLon += ll.Lon; n++;
            }
            return n == 0 ? null : (sumLat / n, sumLon / n);
        }

        /// <summary>
        /// Therion's meridian convergence of <paramref name="cs"/> at <paramref name="at"/>: the angle that
        /// turns a true bearing into a grid bearing (grid = true + convergence). Null when not known here.
        /// </summary>
        internal static double? Convergence(string? cs, (double Latitude, double Longitude) at)
        {
            if (string.IsNullOrWhiteSpace(cs)) return null;
            var s = cs.Trim();
            if (s.Equals("lat-long", StringComparison.OrdinalIgnoreCase) || s.Equals("long-lat", StringComparison.OrdinalIgnoreCase)
                || s.Equals("wgs84", StringComparison.OrdinalIgnoreCase))
                return 0.0;
            int zone = 0;
            if (s.StartsWith("UTM", StringComparison.OrdinalIgnoreCase))
            {
                var rest = s[3..].TrimEnd('N', 'n', 'S', 's');
                if (!int.TryParse(rest, out zone)) return null;
            }
            else if (s.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.AsSpan(5), out int code))
            {
                if (code is >= 32601 and <= 32660) zone = code - 32600;
                else if (code is >= 32701 and <= 32760) zone = code - 32700;
                else return null;
            }
            else return null;
            if (zone is < 1 or > 60) return null;
            double lon0 = zone * 6 - 183;
            double dl = (lon0 - at.Longitude) * Math.PI / 180.0;
            double phi = at.Latitude * Math.PI / 180.0;
            return Math.Atan(Math.Tan(dl) * Math.Sin(phi)) * 180.0 / Math.PI;
        }

        // ---- splays -------------------------------------------------------------------------------

        private (int Kept, int Dropped) ThinSplays()
        {
            var splays = p.Legs.Where(l => l.IsSplay).ToList();
            bool thin = o.SplayKeepPercent < 100 || o.MaxSplaysPerStation is not null;
            if (!thin) return (splays.Count, 0);

            // How often each station is named anywhere: a splay ending at a station named elsewhere too
            // is part of the survey and is never dropped.
            var uses = new Dictionary<StationId, int>();
            void Use(StationId? id) { if (id is { } i) { var r = Find(i); uses[r] = uses.GetValueOrDefault(r) + 1; } }
            foreach (var leg in p.Legs) { Use(leg.FromId); Use(leg.ToId); }
            foreach (var f in p.Fixes) Use(f.Id);
            foreach (var s in p.StationCommands) Use(s.Id);
            foreach (var e in p.Equates) foreach (var s in e.Stations) Use(IdOf(s));
            foreach (var mk in p.Marks) foreach (var s in mk.Stations) Use(IdOf(s));
            foreach (var ex in p.Extends) foreach (var s in ex.Stations) Use(IdOf(s));
            foreach (var d in p.DimsRows) Use(IdOf(d.Station));

            int dropped = 0;
            var byStation = new Dictionary<StationId, List<(FlatLeg Leg, double X, double Y, double Z, bool Droppable)>>();
            foreach (var leg in splays)
            {
                // The splay's station is its named end (the from end when both are named).
                bool atFrom = leg.FromId is not null;
                var station = atFrom ? leg.FromId : leg.ToId;
                if (station is null) continue;
                var other = atFrom ? leg.ToId : null;
                bool droppable = other is null || uses.GetValueOrDefault(Find(other)) <= 1;
                var (x, y, z) = Vector(leg);
                if (!atFrom) { x = -x; y = -y; z = -z; }
                var key = Find(station);
                if (!byStation.TryGetValue(key, out var list)) byStation[key] = list = new();
                list.Add((leg, x, y, z, droppable));
            }

            foreach (var list in byStation.Values)
            {
                int target = o.SplayKeepPercent >= 100 ? list.Count : (int)Math.Ceiling(list.Count * o.SplayKeepPercent / 100.0 - 1e-9);
                if (o.MaxSplaysPerStation is { } max) target = Math.Min(target, max);
                var keep = new HashSet<int>();
                for (int i = 0; i < list.Count; i++) if (!list[i].Droppable) keep.Add(i);
                // Farthest-point sampling of the splay ends, from the longest: the kept splays spread
                // round the passage instead of crowding one wall.
                var candidates = Enumerable.Range(0, list.Count).Where(i => list[i].Droppable).ToList();
                var minDist = candidates.ToDictionary(i => i, _ => double.PositiveInfinity);
                foreach (var k in keep) foreach (var c in candidates) minDist[c] = Math.Min(minDist[c], Dist(list[c], list[k]));
                while (keep.Count < target && candidates.Count > 0)
                {
                    int best = -1;
                    double bestScore = double.NegativeInfinity;
                    foreach (var c in candidates)
                    {
                        double score = keep.Count == 0 ? Norm(list[c]) : minDist[c];
                        if (score > bestScore + 1e-12) { best = c; bestScore = score; }
                    }
                    keep.Add(best);
                    candidates.Remove(best);
                    foreach (var c in candidates) minDist[c] = Math.Min(minDist[c], Dist(list[c], list[best]));
                }
                for (int i = 0; i < list.Count; i++)
                    if (!keep.Contains(i)) { list[i].Leg.Dropped = true; dropped++; }
            }
            return (splays.Count - dropped, dropped);

            static double Norm((FlatLeg, double X, double Y, double Z, bool) s) => Math.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z);
            static double Dist((FlatLeg, double X, double Y, double Z, bool) a, (FlatLeg, double X, double Y, double Z, bool) b)
            {
                double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        /// <summary>The shot as east/north/up metres (declination applied).</summary>
        internal static (double X, double Y, double Z) Vector(FlatLeg leg)
        {
            if (leg.Kind != LegKind.Polar || double.IsNaN(leg.Length)) return (0, 0, 0);
            double g = double.IsPositiveInfinity(leg.Gradient) ? 90 : double.IsNegativeInfinity(leg.Gradient) ? -90 : leg.Gradient;
            double b = (double.IsNaN(leg.Bearing) ? 0 : leg.Bearing) + leg.AppliedDeclination;
            double h = leg.Length * Math.Cos(g * Math.PI / 180.0);
            return (h * Math.Sin(b * Math.PI / 180.0), h * Math.Cos(b * Math.PI / 180.0), leg.Length * Math.Sin(g * Math.PI / 180.0));
        }
    }
}

/// <summary>Stations that are one point (equated), named after their representative.</summary>
internal sealed class StationClass(StationId representative)
{
    public StationId Representative { get; } = representative;
    public List<StationId> Members { get; } = new();
}
