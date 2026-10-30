// Shot tables from anywhere — a spreadsheet selection, a CSV/TSV export, a PocketTopo or DistoX text
// dump — turned into Therion `data` rows. The columns come from a header row when there is one, else
// from the column count; units are inferred where the numbers leave no doubt (compass above 360 =
// grads) and every implausible reading is reported with its line. A table with no station columns
// (a raw instrument dump) gets station names from a start station, and repeated shots of one leg
// (the DistoX habit of three identical shots) become a single averaged leg with the rest as splays.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Therion.Workspace.Templates;

namespace Therion.Workspace.Import;

public enum ShotColumn { From, To, Length, Compass, Clino, Left, Right, Up, Down, Ignore }

public enum ShotLengthUnit { Meters, Feet }

public enum ShotAngleUnit { Degrees, Grads }

public enum ShotIssueKind
{
    /// <summary>Nothing in the text reads as a shot.</summary>
    NoShots,
    /// <summary>A value that should be a number is not (<see cref="ShotIssue.Detail"/> = the value). The row is left out.</summary>
    NotANumber,
    /// <summary>The row has fewer readings than the columns need. The row is left out.</summary>
    TooFewValues,
    /// <summary>Columns beyond the known ones were ignored (Detail = how many).</summary>
    ExtraColumns,
    NegativeLength,
    /// <summary>A leg longer than <see cref="ShotTableReader.LongLegMeters"/> (in the table's unit).</summary>
    LongLength,
    CompassOutOfRange,
    ClinoOutOfRange,
    /// <summary>Compass values above 360 (and none above 400): the angles were read as grads.</summary>
    GradsInferred,
    /// <summary>A leg from a station to itself.</summary>
    SameStation,
    /// <summary>Repeated shots were merged into legs (Detail = how many legs).</summary>
    RepeatedLegsMerged,
    /// <summary>No header and an unusual column count: the column guess may be wrong.</summary>
    ColumnsGuessed,
    /// <summary>A station cell that is empty or cannot be a station name (Detail = the cell). The row is left out.</summary>
    BadStation,
}

/// <param name="Line">1-based line in the pasted text, 0 for the table as a whole.</param>
public sealed record ShotIssue(int Line, ShotIssueKind Kind, string Detail = "");

/// <param name="Values">The readings as written in the output (decimal point, averaged where merged), keyed by column.</param>
public sealed record ShotRow(int Line, string From, string To, IReadOnlyDictionary<ShotColumn, string> Values, bool Splay, int Shots = 1);

/// <param name="Columns">null = take them from the header, else from the column count.</param>
/// <param name="StartStation">The first station of a table without station columns.</param>
/// <param name="LengthUnit">null = from the header (ft / feet), else meters.</param>
/// <param name="AngleUnit">null = inferred (grads when a compass value is above 360).</param>
/// <param name="MergeRepeats">In a table without stations, this many consecutive near-identical shots
/// make one leg (0 = every shot is a leg).</param>
public sealed record ShotTableOptions(
    IReadOnlyList<ShotColumn>? Columns = null,
    string StartStation = "0",
    ShotLengthUnit? LengthUnit = null,
    ShotAngleUnit? AngleUnit = null,
    int MergeRepeats = 3);

public sealed record ShotTable(
    IReadOnlyList<ShotColumn> Columns,
    bool HasHeader,
    bool HasStations,
    ShotLengthUnit LengthUnit,
    ShotAngleUnit AngleUnit,
    IReadOnlyList<ShotRow> Rows,
    IReadOnlyList<ShotIssue> Issues)
{
    public bool HasDimensions => Columns.Any(c => c is ShotColumn.Left or ShotColumn.Right or ShotColumn.Up or ShotColumn.Down);
}

public static partial class ShotTableReader
{
    public const double LongLegMeters = 100;
    public const double LengthTolerance = 0.05;
    public const double AngleTolerance = 1.5;

    private static readonly ShotColumn[] Lrud = [ShotColumn.Left, ShotColumn.Right, ShotColumn.Up, ShotColumn.Down];

    /// <summary>
    /// True when <paramref name="text"/> looks like a block of shots rather than Therion source: most
    /// non-empty lines hold three or more numbers and none starts with a Therion block keyword.
    /// </summary>
    public static bool LooksLikeShots(string text)
    {
        var lines = DataLines(text).Select(l => l.Text).ToList();
        if (lines.Count == 0 || lines.Any(l => Regex.IsMatch(l, @"^\s*(survey|centreline|centerline|data|units|scrap|endsurvey)\b", RegexOptions.IgnoreCase))) return false;
        var delimiter = DetectDelimiter(lines);
        bool comma = UsesDecimalComma(lines, delimiter);
        int numeric = lines.Count(l => Split(l, delimiter).Count(t => TryNumber(t, comma, out _)) >= 3);
        return numeric >= Math.Max(1, (lines.Count + 1) / 2);
    }

    public static ShotTable Read(string text, ShotTableOptions? options = null)
    {
        options ??= new ShotTableOptions();
        var issues = new List<ShotIssue>();
        var lines = DataLines(text).ToList();
        var delimiter = DetectDelimiter(lines.Select(l => l.Text).ToList());
        bool comma = UsesDecimalComma(lines.Select(l => l.Text).ToList(), delimiter);

        // Header: the first line whose cells mostly name known columns.
        IReadOnlyList<ShotColumn>? columns = options.Columns;
        bool hasHeader = false;
        ShotLengthUnit? headerLength = null;
        ShotAngleUnit? headerAngle = null;
        if (lines.Count > 0 && Header(Split(lines[0].Text, delimiter)) is { } header)
        {
            hasHeader = true;
            columns ??= header.Columns;
            headerLength = header.Length;
            headerAngle = header.Angle;
            lines.RemoveAt(0);
        }

        var cells = lines.Select(l => (l.Line, Cells: Split(l.Text, delimiter))).ToList();
        if (columns is null)
        {
            int width = cells.Count == 0 ? 0 : cells.GroupBy(c => c.Cells.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            columns = Guess(width);
            if (width is not (3 or 5 or 9) && width > 0) issues.Add(new ShotIssue(0, ShotIssueKind.ColumnsGuessed, width.ToString(CultureInfo.InvariantCulture)));
        }
        int extra = cells.Count == 0 ? 0 : cells.Max(c => c.Cells.Count) - columns.Count;
        if (extra > 0 && !hasHeader) issues.Add(new ShotIssue(0, ShotIssueKind.ExtraColumns, extra.ToString(CultureInfo.InvariantCulture)));

        bool hasStations = columns.Contains(ShotColumn.From);
        var parsed = new List<(int Line, string From, string To, Dictionary<ShotColumn, double?> V)>();
        foreach (var (line, row) in cells)
        {
            var v = new Dictionary<ShotColumn, double?>();
            string from = "", to = "";
            bool bad = false;
            // A row one cell short in a table with stations is a splay that leaves 'to' out (PocketTopo).
            var map = columns;
            if (hasStations && columns.Contains(ShotColumn.To) && row.Count == Needed(columns) - 1)
                map = columns.Where(c => c != ShotColumn.To).ToList();
            if (row.Count < Needed(map)) { issues.Add(new ShotIssue(line, ShotIssueKind.TooFewValues)); continue; }
            for (int i = 0; i < map.Count && i < row.Count; i++)
            {
                var cell = row[i];
                switch (map[i])
                {
                    case ShotColumn.Ignore: break;
                    case ShotColumn.From: from = cell; break;
                    case ShotColumn.To: to = cell; break;
                    default:
                        if (Lrud.Contains(map[i]) && cell is "" or "-") { v[map[i]] = null; break; }
                        if (!TryNumber(cell, comma, out var d)) { issues.Add(new ShotIssue(line, ShotIssueKind.NotANumber, cell)); bad = true; }
                        else v[map[i]] = d;
                        break;
                }
                if (bad) break;
            }
            if (bad) continue;
            if (hasStations && (!IsStation(from) || !IsSplay(to) && !IsStation(to)))
            {
                issues.Add(new ShotIssue(line, ShotIssueKind.BadStation, IsStation(from) ? to : from));
                continue;
            }
            if (!v.ContainsKey(ShotColumn.Length) || !v.ContainsKey(ShotColumn.Compass) || !v.ContainsKey(ShotColumn.Clino))
            {
                issues.Add(new ShotIssue(line, ShotIssueKind.TooFewValues));
                continue;
            }
            parsed.Add((line, from, to, v));
        }

        var angle = options.AngleUnit ?? headerAngle ?? InferAngle(parsed.Select(p => p.V[ShotColumn.Compass]!.Value), issues);
        var length = options.LengthUnit ?? headerLength ?? ShotLengthUnit.Meters;
        double fullCircle = angle == ShotAngleUnit.Grads ? 400 : 360, rightAngle = fullCircle / 4;
        foreach (var (line, from, to, v) in parsed)
        {
            double l = v[ShotColumn.Length]!.Value, c = v[ShotColumn.Compass]!.Value, k = v[ShotColumn.Clino]!.Value;
            if (l < 0) issues.Add(new ShotIssue(line, ShotIssueKind.NegativeLength, F(l)));
            else if (l > (length == ShotLengthUnit.Feet ? LongLegMeters / 0.3048 : LongLegMeters)) issues.Add(new ShotIssue(line, ShotIssueKind.LongLength, F(l)));
            if (c < 0 || c > fullCircle) issues.Add(new ShotIssue(line, ShotIssueKind.CompassOutOfRange, F(c)));
            if (Math.Abs(k) > rightAngle) issues.Add(new ShotIssue(line, ShotIssueKind.ClinoOutOfRange, F(k)));
            if (hasStations && from.Length > 0 && from == to) issues.Add(new ShotIssue(line, ShotIssueKind.SameStation, from));
        }

        var rows = hasStations
            ? parsed.Select(p => new ShotRow(p.Line, p.From, IsSplay(p.To) ? "-" : p.To, Render(p.V), IsSplay(p.To))).ToList()
            : Sequence(parsed.Select(p => (p.Line, p.V)).ToList(), options, fullCircle, issues);
        if (rows.Count == 0) issues.Insert(0, new ShotIssue(0, ShotIssueKind.NoShots));

        var outColumns = new List<ShotColumn> { ShotColumn.From, ShotColumn.To, ShotColumn.Length, ShotColumn.Compass, ShotColumn.Clino };
        outColumns.AddRange(Lrud.Where(columns.Contains));
        return new ShotTable(outColumns, hasHeader, hasStations, length, angle, rows, issues);
    }

    /// <summary>
    /// The Therion text for a table: <c>units</c> lines when not meters/degrees, the <c>data</c> line
    /// and the aligned rows. With <paramref name="wrap"/> they sit in a <c>centreline</c> block.
    /// </summary>
    public static string ToTherion(ShotTable table, string indent = "  ", bool wrap = false, string newline = "\n")
    {
        var body = new List<string>();
        if (table.LengthUnit == ShotLengthUnit.Feet) body.Add("units length" + (table.HasDimensions ? " dimensions" : "") + " feet");
        if (table.AngleUnit == ShotAngleUnit.Grads) body.Add("units compass clino grads");
        body.Add("data normal " + string.Join(' ', table.Columns.Select(Keyword)));

        var cells = table.Rows.Select(r => table.Columns.Select(c => c switch
        {
            ShotColumn.From => r.From,
            ShotColumn.To => r.To,
            _ => r.Values.TryGetValue(c, out var s) ? s : "-",
        }).ToList()).ToList();
        var widths = table.Columns.Select((_, i) => cells.Count == 0 ? 0 : cells.Max(row => row[i].Length)).ToList();
        foreach (var row in cells)
            body.Add(string.Join(' ', row.Select((s, i) => i == row.Count - 1 ? s : s.PadRight(widths[i]))).TrimEnd());

        var sb = new StringBuilder();
        var pad = wrap ? indent : "";
        if (wrap) sb.Append("centreline").Append(newline);
        foreach (var l in body) sb.Append(pad).Append(l).Append(newline);
        if (wrap) sb.Append("endcentreline").Append(newline);
        return sb.ToString();
    }

    /// <summary>A whole <c>.th</c> file holding the table as one survey (the import of a CSV file).</summary>
    public static string ToSurvey(ShotTable table, string surveyName, string indent = "  ")
    {
        var sb = new StringBuilder();
        sb.Append("encoding utf-8\n");
        sb.Append("survey ").Append(surveyName).Append('\n');
        foreach (var line in ToTherion(table, indent, wrap: true).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            sb.Append(indent).Append(line).Append('\n');
        sb.Append("endsurvey\n");
        return sb.ToString();
    }

    /// <summary>An English sentence for an issue (the CLI's wording; the app localizes by kind).</summary>
    public static string Describe(ShotIssue issue)
    {
        var where = issue.Line > 0 ? $"line {issue.Line}: " : "";
        return where + issue.Kind switch
        {
            ShotIssueKind.NoShots => "no shots found",
            ShotIssueKind.NotANumber => $"'{issue.Detail}' is not a number; row left out",
            ShotIssueKind.TooFewValues => "too few values; row left out",
            ShotIssueKind.ExtraColumns => $"{issue.Detail} extra column(s) ignored",
            ShotIssueKind.NegativeLength => $"negative length {issue.Detail}",
            ShotIssueKind.LongLength => $"length {issue.Detail} is unusually long",
            ShotIssueKind.CompassOutOfRange => $"compass {issue.Detail} is out of range",
            ShotIssueKind.ClinoOutOfRange => $"clino {issue.Detail} is out of range",
            ShotIssueKind.GradsInferred => "compass values above 360: angles read as grads",
            ShotIssueKind.SameStation => $"leg from '{issue.Detail}' to itself",
            ShotIssueKind.RepeatedLegsMerged => $"{issue.Detail} leg(s) made from repeated shots; single shots became splays",
            ShotIssueKind.ColumnsGuessed => $"no header and {issue.Detail} columns: the column guess may be wrong",
            ShotIssueKind.BadStation => $"'{issue.Detail}' is not a station name; row left out",
            _ => issue.Kind.ToString(),
        };
    }

    /// <summary>Parses a column list such as <c>from to length compass clino</c> (Therion's data keywords and the usual synonyms).</summary>
    public static IReadOnlyList<ShotColumn>? ParseColumns(string text)
    {
        var result = new List<ShotColumn>();
        foreach (var word in text.Split([' ', ',', '\t', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (ColumnFor(word) is { } c) result.Add(c);
            else if (word is "-" or "_" or "ignore" or "skip") result.Add(ShotColumn.Ignore);
            else return null;
        }
        return result.Contains(ShotColumn.Length) && result.Contains(ShotColumn.Compass) && result.Contains(ShotColumn.Clino) ? result : null;
    }

    public static string Keyword(ShotColumn c) => c switch
    {
        ShotColumn.From => "from", ShotColumn.To => "to", ShotColumn.Length => "length",
        ShotColumn.Compass => "compass", ShotColumn.Clino => "clino", ShotColumn.Left => "left",
        ShotColumn.Right => "right", ShotColumn.Up => "up", ShotColumn.Down => "down", _ => "ignore",
    };

    // ── Rows without stations: name them, merging repeated shots into legs ──────────────────────

    private static List<ShotRow> Sequence(List<(int Line, Dictionary<ShotColumn, double?> V)> shots, ShotTableOptions options, double fullCircle, List<ShotIssue> issues)
    {
        // Runs of near-identical consecutive shots.
        var runs = new List<(int Start, int Count)>();
        for (int i = 0; i < shots.Count;)
        {
            int j = i + 1;
            while (j < shots.Count && Same(shots[i].V, shots[j].V, fullCircle)) j++;
            runs.Add((i, j - i));
            i = j;
        }
        bool merge = options.MergeRepeats >= 2 && runs.Any(r => r.Count >= options.MergeRepeats);

        var rows = new List<ShotRow>();
        var station = string.IsNullOrWhiteSpace(options.StartStation) ? "0" : options.StartStation.Trim();
        int legs = 0;
        foreach (var (start, count) in runs)
        {
            var run = shots.GetRange(start, count);
            if (!merge)
            {
                foreach (var s in run)
                {
                    var next = Next(station);
                    rows.Add(new ShotRow(s.Line, station, next, Render(s.V), false));
                    station = next;
                }
                continue;
            }
            if (count >= options.MergeRepeats)
            {
                var next = Next(station);
                rows.Add(new ShotRow(run[0].Line, station, next, Render(Average(run.Select(r => r.V).ToList(), fullCircle)), false, count));
                station = next;
                legs++;
            }
            else
                foreach (var s in run) rows.Add(new ShotRow(s.Line, station, "-", Render(s.V), true));
        }
        if (merge) issues.Add(new ShotIssue(0, ShotIssueKind.RepeatedLegsMerged, legs.ToString(CultureInfo.InvariantCulture)));
        return rows;
    }

    private static string Next(string station) => SnippetVariables.NextStation(station) ?? station + "1";

    private static bool Same(Dictionary<ShotColumn, double?> a, Dictionary<ShotColumn, double?> b, double fullCircle) =>
        Math.Abs(a[ShotColumn.Length]!.Value - b[ShotColumn.Length]!.Value) <= LengthTolerance
        && AngleDiff(a[ShotColumn.Compass]!.Value, b[ShotColumn.Compass]!.Value, fullCircle) <= AngleTolerance * fullCircle / 360
        && Math.Abs(a[ShotColumn.Clino]!.Value - b[ShotColumn.Clino]!.Value) <= AngleTolerance * fullCircle / 360;

    private static double AngleDiff(double x, double y, double full)
    {
        var d = Math.Abs(x - y) % full;
        return Math.Min(d, full - d);
    }

    private static Dictionary<ShotColumn, double?> Average(List<Dictionary<ShotColumn, double?>> shots, double fullCircle)
    {
        double toRad = 2 * Math.PI / fullCircle;
        double sx = shots.Sum(s => Math.Cos(s[ShotColumn.Compass]!.Value * toRad)), sy = shots.Sum(s => Math.Sin(s[ShotColumn.Compass]!.Value * toRad));
        var compass = Math.Atan2(sy, sx) / toRad;
        if (compass < 0) compass += fullCircle;
        var result = new Dictionary<ShotColumn, double?>
        {
            [ShotColumn.Length] = Math.Round(shots.Average(s => s[ShotColumn.Length]!.Value), 3),
            [ShotColumn.Compass] = Math.Round(compass, 2) % fullCircle,
            [ShotColumn.Clino] = Math.Round(shots.Average(s => s[ShotColumn.Clino]!.Value), 2),
        };
        // Passage dimensions: the mean of the shots that have one (a merged leg keeps its LRUD).
        foreach (var c in Lrud)
        {
            var values = shots.Select(s => s.GetValueOrDefault(c)).OfType<double>().ToList();
            if (values.Count > 0) result[c] = Math.Round(values.Average(), 2);
            else if (shots.Any(s => s.ContainsKey(c))) result[c] = null;
        }
        return result;
    }

    // ── Cells, numbers, headers ──────────────────────────────────────────────────────────────────

    private static IEnumerable<(int Line, string Text)> DataLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith('#') || t.StartsWith("//", StringComparison.Ordinal)) continue;
            yield return (i + 1, t);
        }
    }

    private static char DetectDelimiter(IReadOnlyList<string> lines)
    {
        var sample = lines.Take(20).ToList();
        if (sample.Count == 0) return ' ';
        if (sample.Count(l => l.Contains('\t')) * 2 >= sample.Count) return '\t';
        if (sample.Count(l => l.Contains(';')) * 2 >= sample.Count) return ';';
        // Commas separate cells when every line splits into the same number (≥3) of comma cells.
        var counts = sample.Select(l => SplitQuoted(l, ',').Count).Distinct().ToList();
        if (counts.Count == 1 && counts[0] >= 3 && sample.All(l => !Regex.IsMatch(l, @"\d,\d+\s+\S"))) return ',';
        return ' ';
    }

    // A decimal comma: the cells are not comma-separated and the numbers are written 12,5.
    // In a comma-separated file such numbers can only appear quoted ("4,5").
    private static bool UsesDecimalComma(IReadOnlyList<string> lines, char delimiter) =>
        lines.Take(20).Any(l => Split(l, delimiter).Any(t => DecimalComma().IsMatch(t)));

    private static List<string> Split(string line, char delimiter) =>
        delimiter == ' '
            ? line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList()
            : SplitQuoted(line, delimiter);

    // CSV cells: a quoted cell may hold the delimiter ("4,5") and doubled quotes.
    private static List<string> SplitQuoted(string line, char delimiter)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == delimiter && !quoted) { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    private static bool TryNumber(string cell, bool decimalComma, out double value)
    {
        var s = cell.Trim();
        if (decimalComma) s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private static bool IsSplay(string to) => to is "" or "-" or "." or "*";

    // One token that Therion can read as a station: no spaces, not an option, no quote.
    private static bool IsStation(string s) => s.Length > 0 && s[0] != '-' && !s.Any(c => char.IsWhiteSpace(c) || c is '"' or '#');

    private static int Needed(IReadOnlyList<ShotColumn> columns)
    {
        // Trailing LRUD may be missing; everything up to the last core column must be there.
        int last = 0;
        for (int i = 0; i < columns.Count; i++)
            if (columns[i] is ShotColumn.From or ShotColumn.To or ShotColumn.Length or ShotColumn.Compass or ShotColumn.Clino) last = i + 1;
        return last;
    }

    private static IReadOnlyList<ShotColumn> Guess(int width) => width switch
    {
        <= 3 => [ShotColumn.Length, ShotColumn.Compass, ShotColumn.Clino],
        < 9 => [ShotColumn.From, ShotColumn.To, ShotColumn.Length, ShotColumn.Compass, ShotColumn.Clino],
        _ => [ShotColumn.From, ShotColumn.To, ShotColumn.Length, ShotColumn.Compass, ShotColumn.Clino, ShotColumn.Left, ShotColumn.Right, ShotColumn.Up, ShotColumn.Down],
    };

    private static ShotAngleUnit InferAngle(IEnumerable<double> compass, List<ShotIssue> issues)
    {
        var values = compass.ToList();
        if (values.Any(c => c > 360) && values.All(c => c <= 400))
        {
            issues.Add(new ShotIssue(0, ShotIssueKind.GradsInferred));
            return ShotAngleUnit.Grads;
        }
        return ShotAngleUnit.Degrees;
    }

    private static Dictionary<ShotColumn, string> Render(Dictionary<ShotColumn, double?> v) =>
        v.ToDictionary(p => p.Key, p => p.Value is { } d ? F(d) : "-");

    private static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    private static (IReadOnlyList<ShotColumn> Columns, ShotLengthUnit? Length, ShotAngleUnit? Angle)? Header(List<string> cells)
    {
        if (cells.Count < 3 || cells.Any(c => TryNumber(c, false, out _))) return null;
        var columns = new List<ShotColumn>();
        ShotLengthUnit? length = null;
        ShotAngleUnit? angle = null;
        int known = 0;
        foreach (var cell in cells)
        {
            var lower = cell.ToLowerInvariant();
            var unit = UnitInHeader().Match(lower);
            var name = UnitInHeader().Replace(lower, "").Trim();
            var col = ColumnFor(name);
            if (col is not null) known++;
            columns.Add(col ?? ShotColumn.Ignore);
            if (unit.Success)
            {
                var u = unit.Groups[1].Value.Trim();
                if (col == ShotColumn.Length && u is "ft" or "feet" or "foot") length = ShotLengthUnit.Feet;
                if (col == ShotColumn.Length && u is "m" or "meters" or "metres") length = ShotLengthUnit.Meters;
                if (col is ShotColumn.Compass or ShotColumn.Clino && u is "grad" or "grads" or "gon") angle = ShotAngleUnit.Grads;
                if (col is ShotColumn.Compass or ShotColumn.Clino && u is "deg" or "°" or "degrees") angle = ShotAngleUnit.Degrees;
            }
        }
        bool core = columns.Contains(ShotColumn.Length) && columns.Contains(ShotColumn.Compass) && columns.Contains(ShotColumn.Clino);
        return core && known >= 3 ? (columns, length, angle) : null;
    }

    private static ShotColumn? ColumnFor(string name) => name.Trim().ToLowerInvariant() switch
    {
        "from" or "from station" or "station" or "st" or "fromstation" => ShotColumn.From,
        "to" or "to station" or "tostation" => ShotColumn.To,
        "length" or "tape" or "distance" or "dist" or "len" or "slope distance" => ShotColumn.Length,
        "compass" or "azimuth" or "azim" or "azi" or "bearing" or "az" => ShotColumn.Compass,
        "clino" or "inclination" or "incl" or "inc" or "clinometer" or "gradient" or "vertical angle" or "dip" => ShotColumn.Clino,
        "left" or "l" => ShotColumn.Left,
        "right" or "r" => ShotColumn.Right,
        "up" or "u" or "ceiling" => ShotColumn.Up,
        "down" or "d" or "floor" => ShotColumn.Down,
        _ => null,
    };

    [GeneratedRegex(@"^-?\d+,\d+$")]
    private static partial Regex DecimalComma();

    [GeneratedRegex(@"[\(\[]([^\)\]]*)[\)\]]")]
    private static partial Regex UnitInHeader();
}
