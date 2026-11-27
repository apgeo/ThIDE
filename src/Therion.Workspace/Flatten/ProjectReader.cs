// Reads a whole Therion project the way Therion's own reader does — the configuration's `source`
// files, every `input` in place inside the survey that names it — and reduces each shot to metres and
// degrees: calibration and units applied, backsights averaged, diving, cylindrical-polar, cartesian,
// topofil and station-interleaved data turned into length / bearing / gradient. Station names stay as
// written (with the survey they were written in) until every survey is known.
//
// Reference: Therion's thdata.cxx (reading a data row), thdb1d.cxx (reducing a leg) and thdate.cxx
// (decimal years), which this mirrors; the cases are noted where they are handled.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Flatten;

/// <summary>The file access the reader needs: text by absolute path (null when missing) and existence.</summary>
public sealed record FlattenFileSource(Func<string, string?> ReadText, Func<string, bool> Exists)
{
    /// <summary>The files on disk, read with the encoding each one declares.</summary>
    public static FlattenFileSource Disk { get; } = new(
        path =>
        {
            try { return File.Exists(path) ? EncodingResolver.ReadAllText(path) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        },
        File.Exists);
}

internal sealed partial class ProjectReader
{
    private readonly FlattenFileSource _files;
    private readonly FlatProject _p = new();
    private readonly Dictionary<string, string> _texts = new(FilePaths.Comparer);
    private readonly HashSet<string> _open = new(FilePaths.Comparer);
    private int _centrelineIds;

    private ProjectReader(FlattenFileSource files) => _files = files;

    public static FlatProject Read(string entryPath, FlattenFileSource files)
    {
        var reader = new ProjectReader(files);
        reader.ReadEntry(Path.GetFullPath(entryPath));
        return reader._p;
    }

    // ---- files -------------------------------------------------------------------------------------

    private static bool IsConfig(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".thconfig" or ".thc" or "";
    }

    private void ReadEntry(string path)
    {
        if (IsConfig(path)) ReadConfig(path, SourceSpan.None);
        else ReadTh(path, _p.Root, SourceSpan.None);
    }

    private string? Load(string path, SourceSpan site)
    {
        var text = _files.ReadText(path);
        if (text is null)
        {
            _p.Report(FlattenDiagnosticCodes.FileMissing, DiagnosticSeverity.Error, $"File not found: {path}", site);
            return null;
        }
        _texts[path] = text;
        if (!_p.Files.Contains(path, FilePaths.Comparer)) _p.Files.Add(path);
        return text;
    }

    private string Raw(SourceSpan span)
    {
        if (span.IsEmpty || !_texts.TryGetValue(span.FilePath, out var text)) return string.Empty;
        int start = Math.Clamp(span.StartOffset, 0, text.Length);
        int length = Math.Clamp(span.Length, 0, text.Length - start);
        return text.Substring(start, length);
    }

    private void ForwardParseErrors(ParseResult<TherionFile> parsed)
    {
        foreach (var d in parsed.Diagnostics)
            if (d.Severity == DiagnosticSeverity.Error) _p.Diagnostics.Add(d);
    }

    private void ReadConfig(string path, SourceSpan site)
    {
        if (!_open.Add(path)) return;
        try
        {
            if (Load(path, site) is not { } text) return;
            var parsed = TherionWorkspace.ParseText(path, text);
            ForwardParseErrors(parsed);
            if (parsed.Value is not { } file) return;
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            foreach (var node in file.Children)
            {
                switch (node)
                {
                    case CsCommand cs when !string.IsNullOrWhiteSpace(cs.System):
                        _p.ConfigCs = cs.System.Trim();
                        break;
                    case UnknownCommand cmd when cmd.Keyword.Equals("source", StringComparison.OrdinalIgnoreCase)
                                              && string.IsNullOrWhiteSpace(cmd.RawArguments)
                                              && cmd.Span.End.Line > cmd.Span.Start.Line:
                        ReadInlineSource(path, text, cmd.Span);
                        break;
                    case UnknownCommand cmd when cmd.Keyword.ToLowerInvariant() is "source" or "input" or "load":
                        foreach (var token in TherionStrings.SplitArguments(cmd.RawArguments))
                        {
                            if (SourceGraph.ResolveDependencyToken(token, dir, _files.Exists) is not { } target) continue;
                            if (IsConfig(target) && !target.EndsWith(".th", StringComparison.OrdinalIgnoreCase))
                                ReadConfig(target, cmd.Span);
                            else
                                ReadTh(target, _p.Root, cmd.Span);
                        }
                        break;
                }
            }
        }
        finally { _open.Remove(path); }
    }

    /// <summary>
    /// A <c>source … endsource</c> block of inline data: parsed as a .th of its own, from a copy of the
    /// configuration with everything outside the block blanked, so spans and raw text still line up.
    /// </summary>
    private void ReadInlineSource(string path, string text, SourceSpan block)
    {
        int start = Math.Clamp(block.StartOffset, 0, text.Length);
        int end = Math.Clamp(block.StartOffset + block.Length, start, text.Length);
        int bodyStart = text.IndexOf('\n', start) is var nl && nl >= 0 && nl < end ? nl + 1 : end;
        int bodyEnd = text.LastIndexOf('\n', Math.Max(bodyStart, end - 1), Math.Max(0, end - bodyStart));
        if (bodyEnd < bodyStart) bodyEnd = bodyStart;
        var masked = new char[text.Length];
        for (int i = 0; i < text.Length; i++)
            masked[i] = (i >= bodyStart && i < bodyEnd) || text[i] is '\n' or '\r' ? text[i] : ' ';
        var parsed = new ThParser().Parse(path, new string(masked), null);
        ForwardParseErrors(parsed);
        if (parsed.Value is { } file) WalkSurveyBody(file.Children, _p.Root, path);
    }

    private void ReadTh(string path, SurveyNode scope, SourceSpan site)
    {
        if (path.EndsWith(".th2", StringComparison.OrdinalIgnoreCase)) return;   // drawings are not needed
        if (!_open.Add(path))
        {
            _p.Report(FlattenDiagnosticCodes.FileMissing, DiagnosticSeverity.Warning,
                $"'{Path.GetFileName(path)}' includes itself (directly or through other files); the repeat is skipped.", site);
            return;
        }
        try
        {
            if (Load(path, site) is not { } text) return;
            var parsed = TherionWorkspace.ParseText(path, text);
            ForwardParseErrors(parsed);
            if (parsed.Value is { } file) WalkSurveyBody(file.Children, scope, path);
        }
        finally { _open.Remove(path); }
    }

    // ---- survey level ------------------------------------------------------------------------------

    private void WalkSurveyBody(ImmutableArray<TherionNode> nodes, SurveyNode scope, string file)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case SurveyCommand sv when !string.IsNullOrEmpty(sv.Name):
                    var child = scope.Child(sv.Name);
                    ApplySurveyOptions(child, sv);
                    WalkSurveyBody(sv.Children, child, file);
                    break;
                case CentrelineCommand cl:
                    ReadCentreline(cl, scope, file);
                    break;
                case InputCommand input when !string.IsNullOrWhiteSpace(input.Path):
                    var dir = Path.GetDirectoryName(file) ?? string.Empty;
                    if (SourceGraph.ResolveDependencyToken(TherionStrings.Unquote(input.Path), dir, _files.Exists) is { } target)
                        ReadTh(target, scope, input.Span);
                    break;
                case EquateCommand eq:
                    RecordEquate(eq, scope, StationNames.None);
                    break;
                case GradeCommand { IsBlockDefinition: true } grade:
                    _p.GradeDefinitions.Add(Raw(grade.Span).TrimEnd());
                    break;
                case GroupCommand group:
                    WalkSurveyBody(group.Children, scope, file);
                    break;
                case UnknownCommand u when u.Keyword.Equals("import", StringComparison.OrdinalIgnoreCase):
                    _p.Report(FlattenDiagnosticCodes.NotCarried, DiagnosticSeverity.Warning,
                        "'import' brings in data compiled by another program (a .3d/.plt); it cannot be merged and is left out.", u.Span);
                    break;
            }
        }
    }

    private void ApplySurveyOptions(SurveyNode survey, SurveyCommand sv)
    {
        var args = TherionArgs.Split(sv.OptionsRaw);
        for (int i = 0; i < args.Count; i++)
        {
            var key = args[i].Value.ToLowerInvariant();
            string? value = i + 1 < args.Count ? args[i + 1].Value : null;
            switch (key)
            {
                case "-title" when value is not null:
                    survey.Title = value; i++; break;
                case "-namespace" when value is not null:
                    survey.NamespaceOff = value.Equals("off", StringComparison.OrdinalIgnoreCase); i++; break;
                case "-entrance" when value is not null:
                    survey.Entrance = new RawStation(value, survey, sv.Span); i++; break;
                case "-declination" when value is not null:
                    survey.Declination = ParseSurveyDeclination(value, sv.Span); i++; break;
            }
        }
    }

    /// <summary>A survey's <c>-declination [&lt;date&gt; &lt;value&gt; … &lt;units&gt;]</c> (thsurvey.cxx parse_declination).</summary>
    private SurveyDeclination? ParseSurveyDeclination(string value, SourceSpan span)
    {
        var items = TherionArgs.ListItems(value);
        if (items.Count == 0) return null;
        if (items.Count == 1 || (items.Count % 2 == 0 && items.Count > 3))
        {
            _p.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Warning,
                $"Invalid survey declination '{value}'; it is ignored.", span);
            return null;
        }
        var unit = AngleUnitOf(items[^1]) ?? AngleUnit.Degree;
        if (items.Count == 2)
        {
            if (TherionArgs.Parse(items[0], out var d) != NumberKind.Number) return null;
            double deg = ToDegrees(d, unit);
            return new SurveyDeclination(deg, [(0.0, deg)]);
        }
        double? undated = null;
        var table = new List<(double, double)>();
        for (int k = 0; k + 1 < items.Count - 1; k += 2)
        {
            if (TherionArgs.Parse(items[k + 1], out var d) != NumberKind.Number) continue;
            double deg = ToDegrees(d, unit);
            if (TherionArgs.Parse(items[k], out _) == NumberKind.Missing) undated = deg;
            else if (TherionDates.Parse(items[k]) is { } date) table.Add((date.Start, deg));
        }
        table.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return new SurveyDeclination(undated, table.Count > 0 ? [.. table] : [(0.0, undated ?? 0.0)]);
    }

    // ---- centreline --------------------------------------------------------------------------------

    private void ReadCentreline(CentrelineCommand cl, SurveyNode scope, string file)
    {
        var info = new CentrelineInfo { Id = ++_centrelineIds, Survey = scope, Span = cl.Span };
        var state = new CentrelineState();
        var opts = TherionArgs.Split(cl.OptionsRaw);
        for (int i = 0; i < opts.Count; i++)
            if (opts[i].Value.Equals("-station-names", StringComparison.OrdinalIgnoreCase) && i + 2 < opts.Count)
            {
                state.Names = new StationNames(opts[i + 1].Value, opts[i + 2].Value);
                i += 2;
            }
        var ctx = new CentrelineContext(this, info, scope);
        WalkCentreline(cl.Children, ctx, state, file);
        ctx.EndRun();

        // Several `date` lines join into one range, as Therion's thdate::join does.
        double? first = null, last = null;
        foreach (var raw in info.Dates)
            if (TherionDates.Parse(raw) is { } d)
            {
                first = first is { } f ? Math.Min(f, d.Start) : d.Start;
                double end = d.End ?? d.Start;
                last = last is { } l ? Math.Max(l, end) : end;
            }
            else
                _p.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Warning,
                    $"Date '{raw}' could not be read; the centreline counts as undated.", cl.Span);
        info.StartYear = first;
        info.EndYear = last > first ? last : null;
        _p.Centrelines.Add(info);
    }

    private void WalkCentreline(ImmutableArray<TherionNode> nodes, CentrelineContext ctx, CentrelineState state, string file)
    {
        foreach (var node in nodes)
        {
            if (node is TrivialComment) continue;
            var text = Raw(node.Span);
            var args = TherionArgs.Split(text);
            switch (node)
            {
                case DataRow row:
                    ctx.Row(row, args, state);
                    break;
                case DataCommand:
                    ctx.EndRun();
                    state.Data = DataDefinition.From(args, _p, node.Span) ?? state.Data;
                    break;
                case BreakCommand:
                    ctx.EndRun();
                    break;
                case GroupCommand group:
                {
                    ctx.EndRun();
                    var inner = state.Clone();
                    WalkCentreline(group.Children, ctx, inner, file);
                    ctx.EndRun();
                    break;
                }
                case UnitsCommand:
                    state.ApplyUnits(args, _p, node.Span);
                    break;
                case CalibrateCommand:
                    state.ApplyCalibrate(args, _p, node.Span);
                    break;
                case FlagsCommand:
                    state.ApplyFlags(args);
                    break;
                case DeclinationCommand or GridAngleCommand:
                    ApplyDeclination(state, args, node is GridAngleCommand, node.Span);
                    break;
                case DateCommand:
                    if (Rest(text, args, 1) is { Length: > 0 } date) ctx.Info.Dates.Add(date);
                    break;
                case TeamCommand:
                    if (Rest(text, args, 1) is { Length: > 0 } team) ctx.Info.Team.Add(team);
                    break;
                case ExploDateCommand:
                    if (Rest(text, args, 1) is { Length: > 0 } xd) ctx.Info.ExploDates.Add(xd);
                    break;
                case ExploTeamCommand:
                    if (Rest(text, args, 1) is { Length: > 0 } xt) ctx.Info.ExploTeam.Add(xt);
                    break;
                case InstrumentCommand:
                    if (Rest(text, args, 1) is { Length: > 0 } ins) ctx.Info.Instruments.Add(ins);
                    break;
                case CsCommand when args.Count > 1:
                    state.Cs = args[1].Value;
                    break;
                case StationFix fix when args.Count > 1:
                    _p.Fixes.Add(new FixRecord(Station(args[1].Value, ctx.Scope, state, fix.Span), state.Cs,
                        [.. args.Skip(2).Select(a => a.Value)], fix.Span));
                    break;
                case EquateCommand eq:
                    RecordEquate(eq, ctx.Scope, state.Names);
                    break;
                case StationCommand st when args.Count > 1:
                    _p.StationCommands.Add(new StationCommandRecord(Station(args[1].Value, ctx.Scope, state, st.Span),
                        Rest(text, args, 2), st.Span));
                    break;
                case MarkCommand mk when args.Count > 1:
                    RecordMark(mk, args, ctx.Scope, state);
                    break;
                case ExtendCommand ex when args.Count > 1:
                    RecordExtend(ex, args, ctx.Scope, state);
                    break;
                case InferCommand when args.Count > 2:
                    bool on = args[2].Value.ToLowerInvariant() is "on" or "true" or "yes";
                    if (args[1].Value.Equals("plumbs", StringComparison.OrdinalIgnoreCase)) state.InferPlumbs = on;
                    else if (args[1].Value.Equals("equates", StringComparison.OrdinalIgnoreCase)) state.InferEquates = on;
                    break;
                case SdCommand:
                    state.Sd = state.Sd.Add(text.Trim());
                    break;
                case GradeCommand { IsBlockDefinition: false }:
                    state.Sd = state.Sd.Add(text.Trim());
                    break;
                case GradeCommand grade:
                    _p.GradeDefinitions.Add(Raw(grade.Span).TrimEnd());
                    break;
                case WallsCommand when args.Count > 1:
                    state.Walls = args[1].Value;
                    break;
                case VThresholdCommand:
                    state.VThreshold = Rest(text, args, 1);
                    break;
                case StationNamesCommand when args.Count > 2:
                    state.Names = new StationNames(args[1].Value, args[2].Value);
                    break;
                case InputCommand input:
                    _p.Report(FlattenDiagnosticCodes.NotCarried, DiagnosticSeverity.Warning,
                        $"'input' inside a centreline is not followed; '{input.Path}' is left out.", input.Span);
                    break;
            }
        }
    }

    /// <summary>The text of a command after its first <paramref name="skip"/> arguments, trimmed.</summary>
    private static string Rest(string text, List<TherionArg> args, int skip) =>
        args.Count > skip ? text[args[skip].Start..args[^1].End].Trim() : string.Empty;

    private void ApplyDeclination(CentrelineState state, List<TherionArg> args, bool grid, SourceSpan span)
    {
        if (args.Count < 2) return;
        var kind = TherionArgs.Parse(args[1].Value, out var v);
        if (kind == NumberKind.Missing) { state.Declination = double.NaN; state.GridNorth = false; return; }
        if (kind != NumberKind.Number)
        {
            _p.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, $"Invalid declination '{args[1].Value}'.", span);
            return;
        }
        if (args.Count < 3 || AngleUnitOf(args[2].Value) is not { } unit)
        {
            _p.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error, "Declination without angular units.", span);
            return;
        }
        if (grid && state.Cs is null)
            _p.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Error,
                "'grid-angle' needs the centreline's coordinate system ('cs').", span);
        state.Declination = ToDegrees(v, unit);
        state.GridNorth = grid;
        state.GridCs = grid ? state.Cs : null;
    }

    private RawStation Station(string raw, SurveyNode scope, CentrelineState state, SourceSpan span) =>
        new(state.Names.Apply(raw), scope, span);

    private void RecordEquate(EquateCommand eq, SurveyNode scope, StationNames names)
    {
        var args = TherionArgs.Split(Raw(eq.Span));
        if (args.Count < 3) return;
        _p.Equates.Add(new EquateRecordRaw(
            [.. args.Skip(1).Select(a => new RawStation(names.Apply(a.Value), scope, eq.Span))], eq.Span));
    }

    private static readonly HashSet<string> MarkTypes = new(StringComparer.OrdinalIgnoreCase) { "fixed", "painted", "temporary", "natural" };

    private void RecordMark(MarkCommand mk, List<TherionArg> args, SurveyNode scope, CentrelineState state)
    {
        var tokens = args.Skip(1).Select(a => a.Value).ToList();
        string type = tokens.Count > 1 && MarkTypes.Contains(tokens[^1]) ? tokens[^1] : "";
        if (type.Length > 0) tokens.RemoveAt(tokens.Count - 1);
        _p.Marks.Add(new MarkRecord([.. tokens.Select(t => Station(t, scope, state, mk.Span))], type, mk.Span));
    }

    private void RecordExtend(ExtendCommand ex, List<TherionArg> args, SurveyNode scope, CentrelineState state)
    {
        var spec = args[1].Value;
        if (args.Count == 2)
        {
            switch (spec.ToLowerInvariant())
            {
                case "normal": state.Extend = (state.Extend & ~ExtendBits.Direction) | ExtendBits.Normal; break;
                case "reverse": state.Extend = (state.Extend & ~ExtendBits.Direction) | ExtendBits.Reverse; break;
                case "left": state.Extend = (state.Extend & ~ExtendBits.Direction) | ExtendBits.Left; break;
                case "right": state.Extend = (state.Extend & ~ExtendBits.Direction) | ExtendBits.Right; break;
                case "vertical": state.Extend = (state.Extend & ~ExtendBits.Direction) | ExtendBits.Vertical; break;
                case "start": state.Extend |= ExtendBits.Start; break;
                case "ignore": state.Extend |= ExtendBits.Ignore; break;
                case "hide": state.Extend |= ExtendBits.Hide; break;
                default:
                    if (TherionArgs.Parse(spec, out var ratio) == NumberKind.Number && ratio is >= 0 and <= 200)
                        state.ExtendRatio = ratio / 100.0;
                    else
                        _p.Report(FlattenDiagnosticCodes.BadReading, DiagnosticSeverity.Warning, $"Unknown extend '{spec}'.", ex.Span);
                    break;
            }
            return;
        }
        _p.Extends.Add(new ExtendRecord(spec, [.. args.Skip(2).Take(3).Select(a => Station(a.Value, scope, state, ex.Span))], ex.Span));
    }

    // ---- units --------------------------------------------------------------------------------------

    internal static AngleUnit? AngleUnitOf(string name) => MeasurementUnits.TryAngle(name);

    internal static double ToDegrees(double value, AngleUnit unit) =>
        unit == AngleUnit.Degree ? value : UnitConverter.Instance.ToDegrees(new Angle(value, unit));
}

/// <summary>Therion's <c>station-names &lt;prefix&gt; &lt;suffix&gt;</c>, applied to a name as written (thobjectname.cxx).</summary>
internal readonly record struct StationNames(string Prefix, string Suffix)
{
    public static StationNames None { get; } = new("", "");

    public string Apply(string raw) =>
        raw is "-" or "." || (Prefix.Length == 0 && Suffix.Length == 0) ? raw : Prefix + raw + Suffix;
}

/// <summary>Decimal years of a Therion date, as thdate.cxx works them out.</summary>
internal static class TherionDates
{
    /// <summary>
    /// The start (and the end of a range) of a <c>date</c> value as decimal years: a missing month is
    /// the middle of the year, a missing day the 15th, a missing time midday (thdate_d2y).
    /// </summary>
    public static (double Start, double? End)? Parse(string raw)
    {
        var compact = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0 || compact == "-") return null;
        int dash = compact.IndexOf('-', 1);
        if (dash > 0)
        {
            var a = Single(compact[..dash]);
            var b = Single(compact[(dash + 1)..]);
            return a is { } s && b is { } e ? (s, e) : null;
        }
        return Single(compact) is { } y ? (y, null) : null;
    }

    private static double? Single(string token)
    {
        string time = "";
        int at = token.IndexOf('@');
        if (at >= 0) { time = token[(at + 1)..]; token = token[..at]; }
        var parts = token.Split('.');
        if (parts.Length is < 1 or > 3 || !int.TryParse(parts[0], out int year)) return null;
        int month = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : -1;
        int day = parts.Length > 2 && int.TryParse(parts[2], out var d) ? d : -1;
        int hour = -1, min = -1; double sec = -1;
        if (time.Length > 0)
        {
            var t = time.Split(':');
            if (t.Length > 0 && int.TryParse(t[0], out var h)) hour = h;
            if (t.Length > 1 && int.TryParse(t[1], out var mi)) min = mi;
            if (t.Length > 2 && double.TryParse(t[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var se)) sec = se;
        }
        return DecimalYear(year, month, day, hour, min, sec);
    }

    public static double DecimalYear(int year, int month, int day, int hour, int min, double sec)
    {
        double ret = year;
        if (month is < 1 or > 12) return ret + 0.5;
        double[] startDay = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
        double msd = startDay[month - 1];
        double yl = 365.0;
        if (year % 4 == 0 && (year % 100 != 0 || year % 400 == 0))
        {
            yl = 366.0;
            if (month > 2) ++msd;
        }
        double doy = msd;
        if (day is >= 1 and <= 32)
        {
            doy += day - 1;
            if (hour >= 0)
            {
                doy += hour / 24.0;
                if (min >= 0)
                {
                    doy += min / (24.0 * 60.0);
                    if (sec >= 0) doy += sec / (24.0 * 60.0 * 60.0);
                }
            }
            else doy += 0.5;
        }
        else doy += 15 - 1;
        return ret + doy / yl;
    }
}
