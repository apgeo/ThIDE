// The centreline half of the reader: the state a centreline body carries from line to line (units,
// calibration, data order, flags, declination, …) and the reduction of each data row to a shot.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Workspace.Flatten;

/// <summary>The quantities that carry their own units and calibration (thdata.cxx dlu_* / dlc_*).</summary>
internal enum Quantity { Length, Counter, Depth, Dx, Dy, Dz, Bearing, Gradient, Up, Down, Left, Right }

/// <summary>A <c>calibrate</c>: (reading − zero error) × scale, or a piecewise table held at its ends (thtfpwf).</summary>
internal sealed record Calibration(double ZeroError, double Scale, ImmutableArray<(double X, double Y)> Table)
{
    public static Calibration None { get; } = new(0.0, 1.0, ImmutableArray<(double, double)>.Empty);

    public double Apply(double value)
    {
        if (Table.IsDefaultOrEmpty) return (value - ZeroError) * Scale;
        if (Table.Length == 1 || value <= Table[0].X) return Table[0].Y;
        if (value >= Table[^1].X) return Table[^1].Y;
        for (int i = 1; i < Table.Length; i++)
            if (Table[i].X >= value)
            {
                var (x1, y1) = Table[i - 1];
                var (x2, y2) = Table[i];
                return x1 == x2 ? y1 : y1 + (value - x1) * (y2 - y1) / (x2 - x1);
            }
        return Table[^1].Y;
    }
}

/// <summary>A <c>data &lt;style&gt; &lt;readings&gt;</c> order, readings in their canonical names.</summary>
internal sealed record DataDefinition(DataStyle Style, ImmutableArray<string> Readings)
{
    public static DataDefinition Default { get; } =
        new(DataStyle.Normal, ["from", "to", "length", "bearing", "gradient"]);

    public bool Interleaved => Readings.Contains("station");
    public bool HasNewline => Readings.Contains("newline");

    public static DataDefinition? From(List<TherionArg> args, FlatProject project, SourceSpan span)
    {
        if (args.Count < 2) return null;
        var style = DataStyles.ParseStyle(args[1].Value);
        if (style == DataStyle.Unknown)
        {
            project.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error,
                $"Unknown data style '{args[1].Value}'; the rows after it are skipped.", span);
            return new DataDefinition(DataStyle.Unknown, ImmutableArray<string>.Empty);
        }
        if (style == DataStyle.Topofil) style = DataStyle.Normal;   // topofil is normal with counters
        var readings = args.Skip(2).Select(a => DataStyles.CanonicalReading(a.Value)).ToImmutableArray();
        return new DataDefinition(style, readings);
    }
}

/// <summary>What a centreline body carries from line to line; a <c>group</c> works on a copy.</summary>
internal sealed class CentrelineState
{
    public DataDefinition Data = DataDefinition.Default;
    public double[] LengthScale = Enumerable.Repeat(1.0, 12).ToArray();     // metres per unit, length-like quantities
    public AngleUnit BearingUnit = AngleUnit.Degree, GradientUnit = AngleUnit.Degree;
    public double BearingFactor = 1.0, GradientFactor = 1.0;
    // Therion keeps the factor of the last `units` that gave one, for lengths and for angles apart, and
    // uses it for every later `units` without one (thdata.cxx: dlu_sdlength / dlu_sdangle keep sfactor).
    public double StickyLengthFactor = 1.0, StickyAngleFactor = 1.0;
    public Calibration[] Cal = Enumerable.Repeat(Calibration.None, 12).ToArray();
    public ShotFlags Flags;
    public double Declination = double.NaN;
    public bool GridNorth;
    public string? GridCs;
    public string? Cs;
    public StationNames Names = StationNames.None;
    public bool InferPlumbs, InferEquates;
    public ImmutableArray<string> Sd = ImmutableArray<string>.Empty;
    public ExtendBits Extend = ExtendBits.Normal;
    public double ExtendRatio = double.NaN;
    public string? Walls;
    public string? VThreshold;

    public CentrelineState Clone()
    {
        var c = (CentrelineState)MemberwiseClone();
        c.LengthScale = (double[])LengthScale.Clone();
        c.Cal = (Calibration[])Cal.Clone();
        return c;
    }

    public LegState LegState() => new(InferPlumbs, InferEquates, Walls, VThreshold,
        string.Join("\n", Sd), Sd, Extend & ExtendBits.Direction, ExtendRatio);

    private static IEnumerable<Quantity>? QuantitiesOf(string token) => token.ToLowerInvariant() switch
    {
        "length" or "tape" => [Quantity.Length],
        "count" or "counter" => [Quantity.Counter],
        "depth" or "depthchange" => [Quantity.Depth],
        "easting" or "dx" => [Quantity.Dx],
        "northing" or "dy" => [Quantity.Dy],
        "altitude" or "dz" => [Quantity.Dz],
        "bearing" or "compass" => [Quantity.Bearing],
        "gradient" or "clino" => [Quantity.Gradient],
        "dimensions" => [Quantity.Up, Quantity.Down, Quantity.Left, Quantity.Right],
        "up" or "ceiling" => [Quantity.Up],
        "down" or "floor" => [Quantity.Down],
        "left" => [Quantity.Left],
        "right" => [Quantity.Right],
        "x" or "y" or "z" or "position" or "gps" => [],
        _ => null,
    };

    /// <summary><c>units &lt;quantities&gt; [&lt;factor&gt;] &lt;unit&gt;</c> (thdata.cxx set_data_units).</summary>
    public void ApplyUnits(List<TherionArg> args, FlatProject project, SourceSpan span)
    {
        if (args.Count < 3) return;
        var unitName = args[^1].Value;
        int end = args.Count - 1;
        double? factor = null;
        if (end > 2 && TherionArgs.Parse(args[end - 1].Value, out var f) == NumberKind.Number) { factor = f; end--; }
        var quantities = new List<Quantity>();
        for (int i = 1; i < end; i++)
        {
            if (QuantitiesOf(args[i].Value) is not { } q)
            {
                project.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Warning,
                    $"Unknown quantity '{args[i].Value}' in 'units'.", span);
                continue;
            }
            quantities.AddRange(q);
        }
        if (MeasurementUnits.TryLength(unitName) is { } lu)
        {
            if (factor is { } given) StickyLengthFactor = given;
            double metres = StickyLengthFactor * UnitConverter.Instance.ToMetres(new Length(1.0, lu));
            foreach (var q in quantities)
                if (q is not (Quantity.Bearing or Quantity.Gradient)) LengthScale[(int)q] = metres;
        }
        else if (MeasurementUnits.TryAngle(unitName) is { } au)
        {
            if (factor is { } given) StickyAngleFactor = given;
            foreach (var q in quantities)
            {
                if (q == Quantity.Bearing) { BearingUnit = au; BearingFactor = StickyAngleFactor; }
                if (q == Quantity.Gradient) { GradientUnit = au; GradientFactor = StickyAngleFactor; }
            }
        }
        else
            project.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, $"Unknown unit '{unitName}'.", span);
    }

    /// <summary><c>calibrate &lt;quantities&gt; &lt;zero error&gt; [&lt;scale&gt;]</c> or a table (thdata.cxx set_data_calibration).</summary>
    public void ApplyCalibrate(List<TherionArg> args, FlatProject project, SourceSpan span)
    {
        var quantities = new List<Quantity>();
        int i = 1;
        for (; i < args.Count; i++)
        {
            var token = args[i].Value.ToLowerInvariant();
            if (token is "backlength" or "backtape") continue;              // accepted, changes nothing
            if (QuantitiesOf(token) is not { } q) break;
            quantities.AddRange(q);
        }
        var numbers = new List<double>();
        for (; i < args.Count; i++)
            if (TherionArgs.Parse(args[i].Value, out var v) == NumberKind.Number) numbers.Add(v);
            else
            {
                project.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, $"Invalid calibration '{args[i].Value}'.", span);
                return;
            }
        Calibration cal;
        switch (numbers.Count)
        {
            case 1: cal = new Calibration(numbers[0], 1.0, ImmutableArray<(double, double)>.Empty); break;
            case 2: cal = new Calibration(numbers[0], numbers[1], ImmutableArray<(double, double)>.Empty); break;
            case > 2 when numbers.Count % 2 == 0:
                var table = new List<(double, double)>();
                for (int k = 0; k < numbers.Count; k += 2) table.Add((numbers[k], numbers[k + 1]));
                table.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                cal = new Calibration(0, 1, [.. table]);
                break;
            default:
                project.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, "Invalid calibration.", span);
                return;
        }
        foreach (var q in quantities) Cal[(int)q] = cal;
    }

    /// <summary><c>flags [not] surface|duplicate|splay|approximate …</c>.</summary>
    public void ApplyFlags(List<TherionArg> args)
    {
        bool negate = false;
        foreach (var a in args.Skip(1))
        {
            var t = a.Value.ToLowerInvariant();
            if (t == "not") { negate = true; continue; }
            var flag = t switch
            {
                "surface" => ShotFlags.Surface,
                "duplicate" => ShotFlags.Duplicate,
                "splay" => ShotFlags.Splay,
                "approximate" or "approx" => ShotFlags.Approximate,
                _ => ShotFlags.None,
            };
            if (flag != ShotFlags.None) Flags = negate ? Flags & ~flag : Flags | flag;
            negate = false;
        }
    }

    /// <summary>A length-like reading: calibrated, then converted to metres.</summary>
    public double Len(Quantity q, double reading) => Cal[(int)q].Apply(reading) * LengthScale[(int)q];

    public double Angle(Quantity q, double reading)
    {
        double v = Cal[(int)q].Apply(reading) * (q == Quantity.Bearing ? BearingFactor : GradientFactor);
        var unit = q == Quantity.Bearing ? BearingUnit : GradientUnit;
        return ProjectReader.ToDegrees(v, unit);
    }
}

internal sealed partial class ProjectReader
{
    /// <summary>The rows of one centreline: interleaved runs and rows split by <c>newline</c> are joined here.</summary>
    private sealed class CentrelineContext(ProjectReader reader, CentrelineInfo info, SurveyNode scope)
    {
        public CentrelineInfo Info { get; } = info;
        public SurveyNode Scope { get; } = scope;
        private FlatProject P => reader._p;

        // A station-interleaved run: the previous station row, and the readings row after it.
        private (string Station, Dictionary<string, string> Values, SourceSpan Span)? _previous;
        private Dictionary<string, string>? _pendingLeg;
        private string? _pendingComment;
        private bool _expectLeg;
        // A row split by `newline` without stations: the values read so far.
        private List<string>? _partial;

        public void EndRun()
        {
            _previous = null;
            _pendingLeg = null;
            _expectLeg = false;
            _partial = null;
        }

        public void Row(DataRow row, List<TherionArg> args, CentrelineState state)
        {
            var data = state.Data;
            if (data.Style == DataStyle.Unknown) return;
            var values = args.Select(a => a.Value).ToList();
            string? comment = row.TrailingComment;

            if (data.Style == DataStyle.Dimensions)
            {
                if (Map(data.Readings.Where(r => r != "newline").ToImmutableArray(), values, row.Span) is not { } dims) return;
                if (dims.TryGetValue("station", out var st) && st is not ("-" or "."))
                    P.DimsRows.Add(new DimsRecord(reader.Station(st, Scope, state, row.Span),
                        DimsOf(dims, state, row.Span), row.Span));
                return;
            }

            if (data.Interleaved)
            {
                InterleavedRow(data, values, row.Span, comment, state);
                return;
            }

            if (data.HasNewline)
            {
                // One shot written over several lines: gather them until the order is complete.
                (_partial ??= new()).AddRange(values);
                if (_partial.Count < data.Readings.Count(r => r != "newline")) return;
                values = _partial;
                _partial = null;
            }

            var readings = data.Readings.Where(r => r != "newline").ToImmutableArray();
            if (Map(readings, values, row.Span) is not { } rec) return;
            if (!rec.TryGetValue("from", out var from) || !rec.TryGetValue("to", out var to))
            {
                P.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, "A data row without from/to stations.", row.Span);
                return;
            }
            int fromIndex = readings.IndexOf("from"), toIndex = readings.IndexOf("to");
            BuildLeg(from, to, rec, null, null, fromBeforeTo: fromIndex < toIndex, row.Span, comment, state);
        }

        private void InterleavedRow(DataDefinition data, List<string> values, SourceSpan span, string? comment, CentrelineState state)
        {
            int nl = data.Readings.IndexOf("newline");
            var stationPart = nl < 0 ? data.Readings : data.Readings[..nl];
            var legPart = nl < 0 ? ImmutableArray<string>.Empty : data.Readings[(nl + 1)..];
            if (stationPart.IsEmpty || stationPart[0] != "station")
            {
                P.Report(FlattenDiagnosticCodes.NotCarried, DiagnosticSeverity.Error,
                    "Interleaved data must start its order with 'station'; these rows are skipped.", span);
                return;
            }
            if (_expectLeg && !legPart.IsEmpty)
            {
                _pendingLeg = Map(legPart, values, span);
                _pendingComment = comment;
                _expectLeg = false;
                return;
            }
            if (Map(stationPart, values, span) is not { } stationValues) return;
            var station = stationValues["station"];
            if (_previous is { } prev && (_pendingLeg is not null || legPart.IsEmpty))
            {
                var leg = _pendingLeg ?? new Dictionary<string, string>();
                BuildLeg(prev.Station, station, leg, prev.Values, stationValues, fromBeforeTo: true, span,
                    _pendingComment ?? comment, state);
            }
            _previous = (station, stationValues, span);
            _pendingLeg = null;
            _pendingComment = null;
            _expectLeg = !legPart.IsEmpty;
        }

        /// <summary>The values of a row by reading name; null (and an error) when the count is wrong.</summary>
        private Dictionary<string, string>? Map(ImmutableArray<string> readings, List<string> values, SourceSpan span)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            int v = 0;
            foreach (var r in readings)
            {
                if (r == "ignoreall") { v = values.Count; break; }
                if (v >= values.Count)
                {
                    P.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, "Not enough readings in the row; it is skipped.", span);
                    return null;
                }
                if (r != "ignore") map[r] = values[v];
                v++;
            }
            if (v < values.Count)
            {
                P.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, "Too many readings in the row; it is skipped.", span);
                return null;
            }
            return map;
        }

        private bool Number(Dictionary<string, string>? rec, string reading, out double value, out NumberKind kind, bool dms = false)
        {
            value = double.NaN;
            kind = NumberKind.Missing;
            if (rec is null || !rec.TryGetValue(reading, out var raw)) return false;
            kind = dms ? TherionArgs.ParseDms(raw, out value) : TherionArgs.Parse(raw, out value);
            return true;
        }

        private void Bad(string what, string? raw, SourceSpan span) =>
            P.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, $"Invalid {what} reading '{raw}'; the shot is skipped.", span);

        private Dims DimsOf(Dictionary<string, string> rec, CentrelineState state, SourceSpan span)
        {
            double One(string reading, Quantity q)
            {
                if (!rec.TryGetValue(reading, out var raw)) return double.NaN;
                var kind = TherionArgs.Parse(raw, out var v);
                if (kind == NumberKind.Missing) return double.NaN;
                if (kind != NumberKind.Number || v < 0) { Bad(reading, raw, span); return double.NaN; }
                return state.Len(q, v);
            }
            return new Dims(One("up", Quantity.Up), One("down", Quantity.Down), One("left", Quantity.Left), One("right", Quantity.Right));
        }

        /// <summary>
        /// The dimensions of a normal row (thdata_parse_dim): one value is the "to" station's, a
        /// bracketed pair gives both ends in the order the from/to columns have.
        /// </summary>
        private (Dims From, Dims To) RowDims(Dictionary<string, string> rec, bool fromBeforeTo, CentrelineState state, SourceSpan span)
        {
            double[] from = [double.NaN, double.NaN, double.NaN, double.NaN];
            double[] to = [double.NaN, double.NaN, double.NaN, double.NaN];
            string[] names = ["up", "down", "left", "right"];
            Quantity[] qs = [Quantity.Up, Quantity.Down, Quantity.Left, Quantity.Right];
            for (int k = 0; k < 4; k++)
            {
                if (!rec.TryGetValue(names[k], out var raw)) continue;
                var items = TherionArgs.ListItems(raw);
                double Read(string s)
                {
                    var kind = TherionArgs.Parse(s, out var v);
                    if (kind == NumberKind.Missing) return double.NaN;
                    if (kind != NumberKind.Number || v < 0) { Bad(names[k], raw, span); return double.NaN; }
                    return state.Len(qs[k], v);
                }
                if (items.Count == 2)
                {
                    double first = Read(items[0]), second = Read(items[1]);
                    (from[k], to[k]) = fromBeforeTo ? (first, second) : (second, first);
                }
                else if (items.Count == 1) to[k] = Read(items[0]);
            }
            return (new Dims(from[0], from[1], from[2], from[3]), new Dims(to[0], to[1], to[2], to[3]));
        }

        private void BuildLeg(string fromRaw, string toRaw, Dictionary<string, string> rec,
            Dictionary<string, string>? fromStation, Dictionary<string, string>? toStation,
            bool fromBeforeTo, SourceSpan span, string? comment, CentrelineState state)
        {
            bool fromAnon = fromRaw is "-" or ".", toAnon = toRaw is "-" or ".";
            if (fromAnon && toAnon)
            {
                P.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error,
                    "A shot between two unnamed stations; it is skipped.", span);
                return;
            }
            var leg = new FlatLeg
            {
                Centreline = Info,
                State = state.LegState(),
                Flags = state.Flags,
                From = fromAnon ? null : reader.Station(fromRaw, Scope, state, span),
                To = toAnon ? null : reader.Station(toRaw, Scope, state, span),
                FromAnonymous = fromAnon ? fromRaw : null,
                ToAnonymous = toAnon ? toRaw : null,
                DeclaredDeclination = state.Declination,
                GridNorth = state.GridNorth,
                GridCs = state.GridCs,
                ExtendOnce = state.Extend & (ExtendBits.Start | ExtendBits.Ignore | ExtendBits.Hide),
                Comment = comment,
                Span = span,
            };
            state.Extend &= ExtendBits.Direction | ExtendBits.Ignore | ExtendBits.Hide;   // `start` holds for one shot

            bool forward = true;
            if (rec.TryGetValue("direction", out var dir))
            {
                if (dir.Equals("b", StringComparison.OrdinalIgnoreCase)) forward = false;
                else if (!dir.Equals("f", StringComparison.OrdinalIgnoreCase)) { Bad("direction", dir, span); return; }
            }

            if (fromStation is not null || toStation is not null)
            {
                leg.FromDims = fromStation is null ? Dims.None : DimsOf(fromStation, state, span);
                leg.ToDims = toStation is null ? Dims.None : DimsOf(toStation, state, span);
            }
            else
                (leg.FromDims, leg.ToDims) = RowDims(rec, fromBeforeTo, state, span);

            switch (state.Data.Style)
            {
                case DataStyle.Nosurvey:
                    leg.Kind = LegKind.NoSurvey;
                    leg.Style = DataStyle.Nosurvey;
                    break;
                case DataStyle.Cartesian:
                    if (!Cartesian(leg, rec, state, forward, span)) return;
                    break;
                default:
                    if (!Polar(leg, rec, fromStation, toStation, state, forward, span)) return;
                    break;
            }
            P.Legs.Add(leg);
        }

        private bool Cartesian(FlatLeg leg, Dictionary<string, string> rec, CentrelineState state, bool forward, SourceSpan span)
        {
            double Read(string reading, Quantity q, out bool ok)
            {
                ok = Number(rec, reading, out var v, out var kind) && kind == NumberKind.Number;
                if (!ok) Bad(reading, rec.GetValueOrDefault(reading), span);
                return ok ? state.Len(q, v) : 0;
            }
            double dx = Read("easting", Quantity.Dx, out var okX);
            double dy = Read("northing", Quantity.Dy, out var okY);
            double dz = Read("altitude", Quantity.Dz, out var okZ);
            if (!(okX && okY && okZ)) return false;
            if (!forward) { dx = -dx; dy = -dy; dz = -dz; }
            double h = Math.Sqrt(dx * dx + dy * dy);
            leg.Kind = LegKind.Polar;
            leg.Style = DataStyle.Cartesian;
            (leg.Dx, leg.Dy, leg.Dz) = (dx, dy, dz);
            leg.Length = Math.Sqrt(h * h + dz * dz);
            leg.Bearing = h == 0 ? double.NaN : NormalizeBearing(Math.Atan2(dx, dy) * 180.0 / Math.PI);
            leg.Gradient = leg.Length == 0 ? 0 : Math.Atan2(dz, h) * 180.0 / Math.PI;
            return true;
        }

        private bool Polar(FlatLeg leg, Dictionary<string, string> rec, Dictionary<string, string>? fromStation,
            Dictionary<string, string>? toStation, CentrelineState state, bool forward, SourceSpan span)
        {
            var style = state.Data.Style;

            // Length: the tape (and its backsight), or the counter difference (topofil).
            double length = double.NaN;
            if (Number(rec, "length", out var l, out var lk))
            {
                if (lk != NumberKind.Number || l < 0) { Bad("length", rec["length"], span); return false; }
                length = state.Len(Quantity.Length, l);
                if (length < 0) { Bad("length", rec["length"], span); return false; }
            }
            if (Number(rec, "backlength", out var bl, out var blk))
            {
                if (blk != NumberKind.Number || bl < 0) { Bad("backlength", rec["backlength"], span); return false; }
                double back = state.Len(Quantity.Length, bl);
                length = double.IsNaN(length) ? back : (length + back) / 2.0;
            }
            if (double.IsNaN(length))
            {
                double fc = double.NaN, tc = double.NaN;
                if (Number(rec, "fromcount", out var a, out var ak) && ak == NumberKind.Number) fc = state.Len(Quantity.Counter, a);
                if (Number(rec, "tocount", out var b, out var bk) && bk == NumberKind.Number) tc = state.Len(Quantity.Counter, b);
                if (Number(fromStation, "count", out var c1, out var c1k) && c1k == NumberKind.Number) fc = state.Len(Quantity.Counter, c1);
                if (Number(toStation, "count", out var c2, out var c2k) && c2k == NumberKind.Number) tc = state.Len(Quantity.Counter, c2);
                if (!double.IsNaN(fc) && !double.IsNaN(tc))
                {
                    if (fc > tc) { Bad("counter", "negative difference", span); return false; }
                    length = tc - fc;
                }
            }
            if (double.IsNaN(length)) { Bad("length", "(none)", span); return false; }

            // Bearing and its backsight (circular mean, as thdb1d.cxx).
            double bearing = double.NaN;
            bool degrees = state.BearingUnit == AngleUnit.Degree;
            if (Number(rec, "bearing", out var br, out var bk2, degrees))
            {
                if (bk2 == NumberKind.Number) bearing = NormalizeBearing(state.Angle(Quantity.Bearing, br));
                else if (bk2 != NumberKind.Missing) { Bad("compass", rec["bearing"], span); return false; }
            }
            if (Number(rec, "backbearing", out var bb, out var bbk, degrees))
            {
                if (bbk == NumberKind.Number)
                {
                    double back = NormalizeBearing(NormalizeBearing(state.Angle(Quantity.Bearing, bb)) - 180.0);
                    if (double.IsNaN(bearing)) bearing = back;
                    else
                    {
                        double sx = Math.Cos((90.0 - bearing) * Math.PI / 180.0) + Math.Cos((90.0 - back) * Math.PI / 180.0);
                        double sy = Math.Sin((90.0 - bearing) * Math.PI / 180.0) + Math.Sin((90.0 - back) * Math.PI / 180.0);
                        bearing = 90.0 - Math.Atan2(sy, sx) * 180.0 / Math.PI;
                        if (bearing < 0) bearing += 360.0;
                    }
                }
                else if (bbk != NumberKind.Missing) { Bad("backcompass", rec["backbearing"], span); return false; }
            }

            double gradient;
            if (style == DataStyle.Normal)
            {
                gradient = double.NaN;
                bool gDegrees = state.GradientUnit == AngleUnit.Degree;
                if (Number(rec, "gradient", out var g, out var gk, gDegrees))
                {
                    if (!Gradient(gk, g, state, out gradient)) { Bad("clino", rec["gradient"], span); return false; }
                }
                if (Number(rec, "backgradient", out var bg, out var bgk, gDegrees))
                {
                    if (!Gradient(bgk, bg, state, out var back)) { Bad("backclino", rec["backgradient"], span); return false; }
                    back = -back;
                    if (double.IsNaN(gradient)) gradient = back;
                    else if (double.IsInfinity(gradient) || double.IsInfinity(back))
                    {
                        if (gradient != back) { Bad("backclino", "plumbed shot with a gradient reading", span); return false; }
                    }
                    else gradient = (gradient + back) / 2.0;
                }
                if (double.IsNaN(gradient)) { Bad("clino", "(none)", span); return false; }
            }
            else
            {
                // Diving and cylindrical polar: a depth change instead of a clino (thdb1d.cxx).
                double change = double.NaN;
                if (Number(rec, "depthchange", out var dc, out var dck) && dck == NumberKind.Number)
                    change = state.Len(Quantity.Depth, dc);
                else
                {
                    double fd = double.NaN, td = double.NaN;
                    if (Number(rec, "fromdepth", out var a, out var ak) && ak == NumberKind.Number) fd = state.Len(Quantity.Depth, a);
                    if (Number(rec, "todepth", out var b, out var bk) && bk == NumberKind.Number) td = state.Len(Quantity.Depth, b);
                    if (Number(fromStation, "depth", out var c1, out var c1k) && c1k == NumberKind.Number) fd = state.Len(Quantity.Depth, c1);
                    if (Number(toStation, "depth", out var c2, out var c2k) && c2k == NumberKind.Number) td = state.Len(Quantity.Depth, c2);
                    change = td - fd;
                }
                if (double.IsNaN(change)) { Bad("depth", "(none)", span); return false; }
                leg.Style = style;
                leg.DepthChange = forward ? change : -change;
                leg.HorizontalLength = length;
                if (style == DataStyle.Diving)
                {
                    if (Math.Abs(change) - length > 1e-6)
                    {
                        P.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error,
                            "The length is shorter than the change in depth; the shot is skipped.", span);
                        return false;
                    }
                    gradient = length == 0 ? 0 : Math.Asin(Math.Clamp(change / length, -1, 1)) * 180.0 / Math.PI;
                }
                else
                {
                    double horizontal = length;
                    length = Math.Sqrt(horizontal * horizontal + change * change);
                    gradient = Math.Atan2(change, horizontal) * 180.0 / Math.PI;
                }
            }

            bool vertical = double.IsInfinity(gradient) || Math.Abs(gradient) >= 90.0;
            if (double.IsNaN(bearing) && !vertical) bearing = 0.0;      // Therion reads a missing compass as 0
            if (!forward)
            {
                if (!double.IsNaN(bearing)) bearing = NormalizeBearing(bearing + 180.0);
                else if (!vertical) bearing = 180.0;
                gradient = -gradient;
            }
            leg.Kind = LegKind.Polar;
            leg.Length = length;
            leg.Bearing = bearing;
            leg.Gradient = gradient;
            return true;
        }

        /// <summary>A clino reading in degrees (thdata.cxx): up/down are plumbed, 270–360 wraps to −90–0.</summary>
        private static bool Gradient(NumberKind kind, double raw, CentrelineState state, out double degrees)
        {
            degrees = double.NaN;
            switch (kind)
            {
                case NumberKind.Up: degrees = double.PositiveInfinity; return true;
                case NumberKind.Down: degrees = double.NegativeInfinity; return true;
                case NumberKind.Number:
                    double v = state.Angle(Quantity.Gradient, raw);
                    if (v is >= 269.99999 and <= 360.00001) v -= 360.0;
                    if (v <= -90.00001 || v >= 90.00001) return false;
                    degrees = Math.Clamp(v, -90.0, 90.0);
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static double NormalizeBearing(double degrees)
    {
        if (double.IsNaN(degrees)) return degrees;
        double b = degrees % 360.0;
        if (b < 0) b += 360.0;
        return b >= 360.0 ? 0.0 : b;
    }
}
