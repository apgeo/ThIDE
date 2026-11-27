// Writes the merged survey: one survey, one centreline, shots in the order of the source, each source
// centreline introduced by a comment. State that Therion keeps from line to line (flags, infer, walls,
// sd, extend) is written where it changes; readings are metres and degrees with the declination already
// in the compass, so `declination 0` stops Therion from adding any.

using System.Globalization;
using System.Text;
using Therion.Semantics;

namespace Therion.Workspace.Flatten;

internal sealed class SingleThWriter(
    FlatProject p,
    FlattenOptions o,
    Func<StationId, string> name,
    string entryFileName,
    string surveyName,
    string? title)
{
    private readonly StringBuilder _sb = new();
    private const string I1 = "  ", I2 = "    ";

    // Defaults Therion falls back to when no `sd` is given (thdataleg.cxx calc_total_stds).
    private static readonly string[] SdDefaults =
    [
        "sd length counter depth 0.1 metres",
        "sd compass clino 1 degrees",
        "sd dx dy dz 0.057735026919 metres",
    ];

    /// <summary>
    /// Write the fixed points even without <see cref="FlattenOptions.Standalone"/>: the merged centreline
    /// has pieces that only they place, and Therion refuses a piece it cannot place.
    /// </summary>
    public bool FixesNeeded { get; init; }

    /// <summary>How many distinct stations the file names (set by <see cref="Write"/>).</summary>
    public int StationsWritten { get; private set; }

    public string Write(int splaysDropped)
    {
        var kept = p.Legs.Where(l => !l.Dropped).ToList();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leg in kept)
        {
            if (leg.FromId is { } a) present.Add(name(a));
            if (leg.ToId is { } b) present.Add(name(b));
        }
        if (o.Standalone || FixesNeeded) foreach (var f in p.Fixes) if (f.Id is { } id) present.Add(name(id));

        StationsWritten = present.Count;
        Header(kept, splaysDropped, present.Count);
        foreach (var grade in p.GradeDefinitions.Distinct()) _sb.Append(grade).Append("\n\n");

        _sb.Append("survey ").Append(surveyName);
        if (!string.IsNullOrEmpty(title)) _sb.Append(" -title ").Append(Quote(title));
        if (o.Standalone && Entrance() is { } entrance && present.Contains(entrance)) _sb.Append(" -entrance ").Append(entrance);
        _sb.Append("\n\n");
        _sb.Append(I1).Append("centreline\n");

        if (o.Standalone) Metadata();
        if (o.Standalone || FixesNeeded) FixedPoints();
        _sb.Append(I2).Append("declination 0 degrees\n");
        _sb.Append(I2).Append("units length meters\n");
        _sb.Append(I2).Append("units compass clino degrees\n");

        Shots(kept);
        Dimensions(present);
        Extends(present, kept);
        if (o.Standalone) StationCommands(present);

        _sb.Append(I1).Append("endcentreline\n\n");
        _sb.Append("endsurvey\n");
        return _sb.ToString();
    }

    private void Header(List<FlatLeg> kept, int splaysDropped, int stations)
    {
        int shots = kept.Count(l => !l.IsSplay), splays = kept.Count - shots;
        _sb.Append("encoding utf-8\n");
        _sb.Append("# Merged by ThIDE from ").Append(entryFileName);
        if (o.GeneratedAt is { } at) _sb.Append(" on ").Append(at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        _sb.Append(": every centreline of the project in one.\n");
        _sb.Append("# ").Append(p.Files.Count).Append(" file(s), ").Append(p.Centrelines.Count).Append(" centreline(s); ")
            .Append(shots).Append(" shot(s), ").Append(splays).Append(" splay(s)");
        if (splaysDropped > 0) _sb.Append(" (").Append(splaysDropped).Append(" more left out)");
        _sb.Append(", ").Append(stations).Append(" station(s).\n");
        _sb.Append(o.ApplyDeclination
            ? "# The compass readings include each survey's magnetic declination (noted before its shots);\n# the declination is 0 below so that Therion adds none.\n"
            : "# The compass readings are as surveyed: no magnetic declination is applied.\n");
        if (o.Standalone && p.ConfigCs is { } cs)
            _sb.Append("# The project's configuration sets the output coordinate system: cs ").Append(cs).Append('\n');
        _sb.Append('\n');
    }

    private string? Entrance()
    {
        if (p.Root.Children.Count != 1 || p.Root.Children.Values.First().Entrance is not { } raw) return null;
        return Id(raw) is { } id ? name(id) : null;
    }

    private void Metadata()
    {
        var cls = p.Centrelines;
        foreach (var d in cls.SelectMany(c => c.Dates).Distinct()) Line("date " + d);
        foreach (var d in cls.SelectMany(c => c.ExploDates).Distinct()) Line("explo-date " + d);
        foreach (var t in cls.SelectMany(c => c.Team).Distinct()) Line("team " + t);
        foreach (var t in cls.SelectMany(c => c.ExploTeam).Distinct()) Line("explo-team " + t);
        foreach (var i in cls.SelectMany(c => c.Instruments).Distinct()) Line("instrument " + i);
    }

    private void FixedPoints()
    {
        // Fixed points, each in its own coordinate system (local ones first: no `cs` before them).
        foreach (var group in p.Fixes.Where(f => f.Id is not null).GroupBy(f => f.Cs ?? "").OrderBy(g => g.Key.Length == 0 ? 0 : 1))
        {
            if (group.Key.Length > 0) Line("cs " + group.Key);
            foreach (var fix in group)
            {
                // Equated stations fixed apiece are one station now: one fix, and a word when they differ.
                var station = name(fix.Id!);
                var values = string.Join(' ', fix.Values);
                if (_fixed.TryGetValue(station, out var earlier))
                {
                    if (earlier != values)
                        p.Report(FlattenDiagnosticCodes.NotCarried, Therion.Core.DiagnosticSeverity.Warning,
                            $"Station '{station}' is fixed twice with different coordinates (equated stations fixed apiece); the first fix is kept.", fix.Span);
                    continue;
                }
                _fixed[station] = values;
                Line("fix " + station + (values.Length > 0 ? " " + values : ""));
            }
        }
    }

    private readonly Dictionary<string, string> _fixed = new(StringComparer.Ordinal);

    private void Line(string text) => _sb.Append(I2).Append(text).Append('\n');

    // ---- shots -------------------------------------------------------------------------------------

    private readonly record struct Layout(Therion.Syntax.DataStyle Style, bool Dims)
    {
        public string Command => Style switch
        {
            Therion.Syntax.DataStyle.Nosurvey => "data nosurvey from to",
            Therion.Syntax.DataStyle.Diving => "data diving from to length compass depthchange",
            Therion.Syntax.DataStyle.Cylpolar => "data cylpolar from to length compass depthchange",
            Therion.Syntax.DataStyle.Cartesian => "data cartesian from to easting northing altitude",
            _ => "data normal from to length compass clino",
        } + (Dims ? " left right up down" : "");
    }

    private void Shots(List<FlatLeg> legs)
    {
        var flags = ShotFlags.None;
        var state = LegState.Default;
        Layout? layout = null;
        CentrelineInfo? current = null;
        var block = new List<string[]>();
        var blockComments = new List<string?>();

        void Flush()
        {
            if (block.Count == 0) return;
            int cols = block.Max(r => r.Length);
            var width = new int[cols];
            foreach (var r in block) for (int c = 0; c < r.Length; c++) width[c] = Math.Max(width[c], r[c].Length);
            for (int k = 0; k < block.Count; k++)
            {
                var r = block[k];
                if (r.Length == 1 && r[0].StartsWith('#')) { Line(r[0]); continue; }
                var sb = new StringBuilder();
                for (int c = 0; c < r.Length; c++)
                {
                    if (c > 0) sb.Append(' ');
                    bool numeric = c >= 2;
                    sb.Append(numeric ? r[c].PadLeft(width[c]) : r[c].PadRight(width[c]));
                }
                var line = sb.ToString().TrimEnd();
                if (blockComments[k] is { } comment) line += "  # " + comment;
                Line(line);
            }
            block.Clear();
            blockComments.Clear();
        }

        void Command(string text) { Flush(); Line(text); }

        foreach (var leg in legs)
        {
            if (!ReferenceEquals(leg.Centreline, current))
            {
                current = leg.Centreline;
                if (o.SourceComments) { Flush(); _sb.Append('\n'); Line("# " + Describe(leg.Centreline, leg)); }
            }

            var want = leg.Flags & (ShotFlags.Surface | ShotFlags.Duplicate | ShotFlags.Approximate | ShotFlags.Splay);
            if (want != flags)
            {
                var on = new List<string>();
                var off = new List<string>();
                foreach (var (f, word) in FlagWords)
                {
                    if ((want & f) != 0 && (flags & f) == 0) on.Add(word);
                    if ((want & f) == 0 && (flags & f) != 0) off.Add(word);
                }
                if (off.Count > 0) Command("flags not " + string.Join(" not ", off));
                if (on.Count > 0) Command("flags " + string.Join(' ', on));
                flags = want;
            }

            if (!leg.State.Equals(state)) { foreach (var c in StateChanges(state, leg.State)) Command(c); state = leg.State; }

            var style = leg.Kind == LegKind.NoSurvey ? Therion.Syntax.DataStyle.Nosurvey : leg.Style;
            var wantLayout = new Layout(style, style != Therion.Syntax.DataStyle.Nosurvey && !(leg.FromDims.IsEmpty && leg.ToDims.IsEmpty));
            if (wantLayout != layout)
            {
                Command(wantLayout.Command);
                layout = wantLayout;
            }

            if ((leg.ExtendOnce & ExtendBits.Start) != 0) Command("extend start");

            var from = leg.FromId is { } f1 ? name(f1) : leg.FromAnonymous ?? "-";
            var to = leg.ToId is { } t1 ? name(t1) : leg.ToAnonymous ?? "-";
            var row = new List<string> { from, to };
            switch (style)
            {
                case Therion.Syntax.DataStyle.Nosurvey:
                    break;
                case Therion.Syntax.DataStyle.Cartesian:
                    // Turned by the declination as Therion turns a cartesian shot (thdb1d.cxx).
                    double d = leg.AppliedDeclination * Math.PI / 180.0;
                    row.Add(Length(Math.Cos(d) * leg.Dx + Math.Sin(d) * leg.Dy));
                    row.Add(Length(Math.Cos(d) * leg.Dy - Math.Sin(d) * leg.Dx));
                    row.Add(Length(leg.Dz));
                    break;
                case Therion.Syntax.DataStyle.Diving:
                    row.AddRange([Length(leg.Length), Bearing(leg), Length(leg.DepthChange)]);
                    break;
                case Therion.Syntax.DataStyle.Cylpolar:
                    row.AddRange([Length(leg.HorizontalLength), Bearing(leg), Length(leg.DepthChange)]);
                    break;
                default:
                    row.AddRange([Length(leg.Length), Bearing(leg), Gradient(leg.Gradient)]);
                    break;
            }
            if (wantLayout.Dims)
                row.AddRange([Dim(leg.FromDims.Left, leg.ToDims.Left), Dim(leg.FromDims.Right, leg.ToDims.Right),
                    Dim(leg.FromDims.Up, leg.ToDims.Up), Dim(leg.FromDims.Down, leg.ToDims.Down)]);
            block.Add([.. row]);
            blockComments.Add(o.ShotComments && !string.IsNullOrWhiteSpace(leg.Comment) ? leg.Comment!.Trim() : null);
        }
        Flush();
    }

    private static readonly (ShotFlags Flag, string Word)[] FlagWords =
    [
        (ShotFlags.Surface, "surface"), (ShotFlags.Duplicate, "duplicate"),
        (ShotFlags.Approximate, "approximate"), (ShotFlags.Splay, "splay"),
    ];

    private IEnumerable<string> StateChanges(LegState from, LegState to)
    {
        if (from.InferPlumbs != to.InferPlumbs) yield return "infer plumbs " + (to.InferPlumbs ? "on" : "off");
        if (from.InferEquates != to.InferEquates) yield return "infer equates " + (to.InferEquates ? "on" : "off");
        if (from.Walls != to.Walls) yield return "walls " + (to.Walls ?? "auto");
        if (from.VThreshold != to.VThreshold) yield return "vthreshold " + (to.VThreshold ?? "67.5 degrees");
        if (from.SdSignature != to.SdSignature)
        {
            if (from.SdCommands.Length > 0) foreach (var d in SdDefaults) yield return d;
            foreach (var c in to.SdCommands) yield return c;
        }
        if (from.ExtendDirection != to.ExtendDirection)
            yield return "extend " + to.ExtendDirection switch
            {
                ExtendBits.Reverse => "reverse",
                ExtendBits.Left => "left",
                ExtendBits.Right => "right",
                ExtendBits.Vertical => "vertical",
                _ => "normal",
            };
        if (!Same(from.ExtendRatio, to.ExtendRatio))
            yield return "extend " + (double.IsNaN(to.ExtendRatio) ? "100" : Num(to.ExtendRatio * 100.0, 1));
    }

    private static bool Same(double a, double b) => (double.IsNaN(a) && double.IsNaN(b)) || a == b;

    private string Describe(CentrelineInfo cl, FlatLeg first)
    {
        var sb = new StringBuilder();
        sb.Append(cl.Survey.IsRoot ? "(top level)" : cl.Survey.TherionName);
        sb.Append(" — ").Append(Path.GetFileName(cl.Span.FilePath)).Append(':').Append(cl.Span.Start.Line);
        if (cl.DateText is { } d) sb.Append(" — ").Append(d);
        if (o.ApplyDeclination)
        {
            _declinations ??= p.Legs.GroupBy(l => l.Centreline)
                .ToDictionary(g => g.Key, g => g.Select(l => Math.Round(l.AppliedDeclination, 3)).Distinct().ToList());
            var values = _declinations[cl];
            sb.Append(" — declination ");
            sb.Append(values.Count == 1 ? Signed(values[0]) : string.Join(" / ", values.Select(Signed)));
            sb.Append(first.DeclinationSource switch
            {
                FlattenDeclinationSource.Centreline => " (stated in the centreline)",
                FlattenDeclinationSource.Survey => " (the survey's)",
                FlattenDeclinationSource.Igrf => $" (IGRF {cl.AverageYear:0.00})",
                FlattenDeclinationSource.IgrfUndated => " (undated: IGRF of the earliest date)",
                _ => " (none)",
            });
        }
        return sb.ToString();
    }

    private Dictionary<CentrelineInfo, List<double>>? _declinations;

    private string Signed(double v) => (v >= 0 ? "+" : "") + Num(v, o.AngleDecimals) + "°";

    // ---- after the shots ---------------------------------------------------------------------------

    private void Dimensions(HashSet<string> present)
    {
        var rows = p.DimsRows.Where(d => Id(d.Station) is { } id && present.Contains(name(id))).ToList();
        if (rows.Count == 0) return;
        _sb.Append('\n');
        Line("data dimensions station left right up down");
        foreach (var d in rows)
            Line(string.Join(' ', name(Id(d.Station)!), Dim1(d.Dims.Left), Dim1(d.Dims.Right), Dim1(d.Dims.Up), Dim1(d.Dims.Down)));
    }

    private void Extends(HashSet<string> present, List<FlatLeg> kept)
    {
        var lines = new List<string>();
        foreach (var ex in p.Extends)
        {
            var ids = ex.Stations.Select(Id).ToList();
            if (ids.Any(i => i is null || !present.Contains(name(i)))) continue;
            lines.Add("extend " + ex.Spec + " " + string.Join(' ', ids.Select(i => name(i!))));
        }
        foreach (var leg in kept)
        {
            if (leg.FromId is null || leg.ToId is null) continue;
            if ((leg.ExtendOnce & ExtendBits.Ignore) != 0) lines.Add($"extend ignore {name(leg.FromId)} {name(leg.ToId)}");
            if ((leg.ExtendOnce & ExtendBits.Hide) != 0) lines.Add($"extend hide {name(leg.FromId)} {name(leg.ToId)}");
        }
        if (lines.Count == 0) return;
        _sb.Append('\n');
        foreach (var l in lines.Distinct()) Line(l);
    }

    private void StationCommands(HashSet<string> present)
    {
        var lines = new List<string>();
        foreach (var st in p.StationCommands)
            if (st.Id is { } id && present.Contains(name(id)))
                lines.Add("station " + name(id) + (st.Rest.Length > 0 ? " " + st.Rest : ""));
        foreach (var mk in p.Marks)
        {
            var names = mk.Stations.Select(Id).Where(i => i is not null && present.Contains(name(i))).Select(i => name(i!)).ToList();
            if (names.Count > 0) lines.Add("mark " + string.Join(' ', names) + (mk.Type.Length > 0 ? " " + mk.Type : ""));
        }
        if (lines.Count == 0) return;
        _sb.Append('\n');
        foreach (var l in lines.Distinct()) Line(l);
    }

    private StationId? Id(RawStation raw)
    {
        // Every raw station was resolved before writing; look it up through the legs' own resolution.
        return _ids.TryGetValue(raw, out var id) ? id : null;
    }

    private Dictionary<RawStation, StationId?> _ids = new();

    public SingleThWriter WithIds(Dictionary<RawStation, StationId?> ids) { _ids = ids; return this; }

    // ---- numbers -----------------------------------------------------------------------------------

    private string Num(double v, int decimals)
    {
        var s = Math.Round(v, decimals).ToString("F" + decimals, CultureInfo.InvariantCulture);
        return s.StartsWith('-') && s.Trim('-', '0', '.').Length == 0 ? s[1..] : s;
    }

    private string Length(double metres) => Num(metres, o.LengthDecimals);

    private string Bearing(FlatLeg leg)
    {
        if (double.IsNaN(leg.Bearing)) return "-";
        double b = ProjectReader.NormalizeBearing(leg.Bearing + leg.AppliedDeclination);
        var s = Num(b, o.AngleDecimals);
        return double.Parse(s, CultureInfo.InvariantCulture) >= 360.0 ? Num(0, o.AngleDecimals) : s;
    }

    private string Gradient(double g) =>
        double.IsPositiveInfinity(g) ? "up" : double.IsNegativeInfinity(g) ? "down" : Num(g, o.AngleDecimals);

    private string Dim1(double v) => double.IsNaN(v) ? "-" : Num(v, o.LengthDecimals);

    private string Dim(double from, double to) =>
        double.IsNaN(from) ? Dim1(to) : "[" + Dim1(from) + " " + Dim1(to) + "]";

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
}
