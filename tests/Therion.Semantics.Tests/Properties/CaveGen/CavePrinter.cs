// Writes a generated cave as Therion .th text. Every choice it makes is meaning-preserving:
// the same cave must bind to the same model however it is printed. Each family of choices can be
// switched off, which is how a failing case is narrowed down to the feature that breaks it.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Therion.Core;

namespace Therion.Semantics.Tests.Properties.CaveGen;

/// <summary>The families of printing choices the printer may use. Each is a switch.</summary>
internal sealed record PrintFeatures(
    bool LineBreaks = true,       // CRLF, or mixed LF / CRLF / lone CR, instead of LF
    bool Layout = true,           // indentation with spaces or tabs, and random gaps between words
    bool Noise = true,            // blank lines, comment lines and trailing comments
    bool Continuations = true,    // backslash line continuations between words
    bool Groups = true,           // group … endgroup around row runs, with local flags / units / data
    bool SplitFiles = true,       // child surveys in their own file, pulled in with input
    bool SplitCentrelines = true, // a survey's shots spread over several centrelines
    bool Units = true,            // feet / cm / yards (with factors), grads, mils, percent
    bool ColumnOrder = true,      // permuted reading order, tape / bearing / gradient, LRUD columns
    bool BackSights = true,       // back bearings and back clinos instead of, or as well as, fore ones
    bool Sexagesimal = true,      // fix coordinates as deg:min:sec
    bool Aliases = true,          // centerline, endsurvey <id>, implicit default data order, encoding line
    bool Calibration = false)     // calibrate tape / clino zero errors and scales (off: the binder ignores calibrate)
{
    public static PrintFeatures All { get; } = new();

    public static PrintFeatures None { get; } = new(false, false, false, false, false, false, false, false, false, false, false, false);

    public static IReadOnlyList<string> Names { get; } = new[]
    {
        nameof(LineBreaks), nameof(Layout), nameof(Noise), nameof(Continuations), nameof(Groups), nameof(SplitFiles),
        nameof(SplitCentrelines), nameof(Units), nameof(ColumnOrder), nameof(BackSights), nameof(Sexagesimal), nameof(Aliases),
    };

    public bool Get(string name) => (bool)typeof(PrintFeatures).GetProperty(name)!.GetValue(this)!;

    public PrintFeatures With(string name, bool value)
    {
        var copy = this with { };
        typeof(PrintFeatures).GetProperty(name)!.SetValue(copy, value);
        return copy;
    }

    public override string ToString() =>
        string.Join(", ", Names.Append(nameof(Calibration)).Where(Get)) is { Length: > 0 } on ? on : "(plain)";
}

/// <summary>One printed file: its (virtual) path, its text, and the survey path it is input under.</summary>
internal sealed record PrintedFile(string Path, string Text, ImmutableArray<string> Prefix);

internal static class CavePrinter
{
    /// <summary>Virtual folder of the printed files. Nothing is written to it.</summary>
    public static readonly string Folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thide-cavegen-virtual");

    public static List<PrintedFile> Print(GCave cave, PrintFeatures features, int seed)
    {
        var files = new List<PrintedFile>();
        var printer = new Printer(new SeededRandom(seed), features, files);
        printer.PrintFile("main.th", cave.Root, ImmutableArray<string>.Empty);
        return files;
    }

    internal static string Fmt(double v)
    {
        if (v == 0) v = 0;   // no "-0"
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    internal static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    /// <summary>The text that names <paramref name="target"/> from survey <paramref name="home"/>: bare, or point@inner.outer.</summary>
    internal static string RefText(ImmutableArray<string> home, GRef target)
    {
        if (target.Survey.SequenceEqual(home)) return target.Station;
        if (target.Survey.Length <= home.Length || !target.Survey.Take(home.Length).SequenceEqual(home))
            throw new InvalidOperationException($"{target} is not below {string.Join('.', home)}");
        var below = target.Survey.Skip(home.Length).Reverse();
        return target.Station + "@" + string.Join('.', below);
    }

    private static double ToUnit(double degrees, AngleUnit unit) => unit switch
    {
        AngleUnit.Grad => degrees / 0.9,
        AngleUnit.Mil => degrees * 6400.0 / 360.0,
        AngleUnit.PercentSlope => Math.Tan(degrees * Math.PI / 180.0) * 100.0,
        _ => degrees,
    };

    // Units in force, and the calibration: true value = (read value - zero error) * scale.
    private sealed record ReadingUnits(double TapeScale, AngleUnit Compass, AngleUnit Clino,
        double TapeZero = 0, double TapeCalScale = 1, double ClinoZero = 0)
    {
        public static ReadingUnits Default { get; } = new(1.0, AngleUnit.Degree, AngleUnit.Degree);
    }

    private sealed record State(ImmutableArray<string> Columns, ReadingUnits Units, ShotFlags Flags);

    private static readonly ImmutableArray<string> DefaultColumns = ImmutableArray.Create("from", "to", "length", "compass", "clino");

    // A centreline body entry: a shot row, a command line, or a group boundary.
    private abstract record Entry;
    private sealed record RowEntry(GShot Shot) : Entry;
    private sealed record LineEntry(string?[] Words) : Entry;
    private sealed record GroupStart : Entry;
    private sealed record GroupEnd : Entry;

    private sealed class Printer
    {
        private readonly SeededRandom _rnd;
        private readonly PrintFeatures _f;
        private readonly List<PrintedFile> _files;

        public Printer(SeededRandom rnd, PrintFeatures features, List<PrintedFile> files)
        {
            _rnd = rnd;
            _f = features;
            _files = files;
        }

        public void PrintFile(string name, GSurvey survey, ImmutableArray<string> prefix)
        {
            var w = new Writer(_rnd, _f);
            if (_f.Aliases && _rnd.Chance(0.3)) w.Line(0, "encoding", "utf-8");
            if (_f.Noise && _rnd.Chance(0.3)) w.Comment(0, "generated cave");
            PrintSurvey(w, survey, 0);
            _files.Add(new PrintedFile(System.IO.Path.Combine(Folder, name), w.Text, prefix));
        }

        private void PrintSurvey(Writer w, GSurvey s, int depth)
        {
            w.Line(depth, "survey", s.Name, s.Title is null ? null : "-title", s.Title is null ? null : Quote(s.Title));

            // Equates go at survey level or into one of the survey's centrelines.
            var surveyLevel = s.Equates.Where(_ => _rnd.Chance(0.5)).ToList();
            var inCentrelines = s.Equates.Except(surveyLevel).ToList();

            var parts = new List<Action>();
            foreach (var cl in SplitShots(s, inCentrelines))
            {
                var shots = cl;
                parts.Add(() => PrintCentreline(w, s, shots.Rows, shots.Lines, depth + 1));
            }
            foreach (var child in s.Children)
            {
                var c = child;
                if (_f.SplitFiles && _rnd.Chance(0.5))
                {
                    var file = c.Name + ".th";
                    parts.Add(() => w.Line(depth + 1, "input", _f.Aliases && _rnd.Chance(0.3) ? Quote(file) : file));
                    PrintFile(file, c, s.Path);
                }
                else
                {
                    parts.Add(() => PrintSurvey(w, c, depth + 1));
                }
            }
            foreach (var eq in surveyLevel)
            {
                var e = eq;
                parts.Add(() => w.Line(depth + 1, EquateWords(s, e)));
            }
            _rnd.Shuffle(parts);
            foreach (var p in parts) p();

            w.Line(depth, "endsurvey", _f.Aliases && _rnd.Chance(0.3) ? s.Name : null);
        }

        private string?[] EquateWords(GSurvey home, GEquate e) =>
            new[] { "equate" }.Concat(e.Members.Select(m => RefText(home.Path, m))).ToArray();

        /// <summary>Spreads the survey's shots and its fix / station / equate lines over its centrelines.</summary>
        private List<(List<GShot> Rows, List<string?[]> Lines)> SplitShots(GSurvey s, List<GEquate> equates)
        {
            int count = _f.SplitCentrelines ? Math.Min(s.Shots.Count, _rnd.Next(1, 4)) : 1;
            var parts = Enumerable.Range(0, Math.Max(1, count)).Select(_ => (Rows: new List<GShot>(), Lines: new List<string?[]>())).ToList();
            for (int i = 0; i < s.Shots.Count; i++)
                parts[i < parts.Count ? i : _rnd.Next(parts.Count)].Rows.Add(s.Shots[i]);

            foreach (var fix in s.Fixes)
            {
                bool dms = _f.Sexagesimal && _rnd.Chance(0.5);
                string Coord(GCoord c) => dms
                    ? $"{(c.Negative ? "-" : "")}{c.Degrees}:{c.Minutes:00}:{Fmt(c.Seconds)}"
                    : Fmt(c.Value);
                _rnd.Pick(parts).Lines.Add(new[] { "fix", RefText(s.Path, fix.Station), Coord(fix.X), Coord(fix.Y), Fmt(fix.Z) });
            }
            foreach (var note in s.Notes)
                _rnd.Pick(parts).Lines.Add(new[] { "station", RefText(s.Path, note.Station), Quote(note.Comment), note.Entrance ? "entrance" : null });
            foreach (var eq in equates)
                _rnd.Pick(parts).Lines.Add(EquateWords(s, eq));
            return parts;
        }

        private void PrintCentreline(Writer w, GSurvey s, List<GShot> rows, List<string?[]> lines, int depth)
        {
            bool us = _f.Aliases && _rnd.Chance(0.3);
            w.Line(depth, us ? "centerline" : "centreline");

            var entries = rows.Select(r => (Entry)new RowEntry(r)).ToList();
            foreach (var line in lines) entries.Insert(_rnd.Next(entries.Count + 1), new LineEntry(line));
            if (_f.Groups && entries.Count > 0 && _rnd.Chance(0.5))
            {
                int start = _rnd.Next(entries.Count);
                int end = _rnd.Next(start, entries.Count) + 1;
                entries.Insert(end, new GroupEnd());
                entries.Insert(start, new GroupStart());
            }

            var state = new State(DefaultColumns, ReadingUnits.Default, ShotFlags.None);
            state = Configure(w, depth + 1, state, first: true);
            var stack = new Stack<State>();
            foreach (var e in entries)
            {
                switch (e)
                {
                    case GroupStart:
                        w.Line(depth + 1, "group");
                        stack.Push(state);
                        depth++;
                        // Inside the group: maybe new units or a new reading order, local to it.
                        state = Configure(w, depth + 1, state, first: false);
                        break;
                    case GroupEnd:
                        depth--;
                        w.Line(depth + 1, "endgroup");
                        state = stack.Pop();   // Therion restores everything at endgroup
                        break;
                    case LineEntry le:
                        w.Line(depth + 1, le.Words);
                        break;
                    case RowEntry re:
                        state = SyncFlags(w, depth + 1, state, re.Shot.Flags);
                        w.Line(depth + 1, RowWords(s, re.Shot, state), trailingComment: _f.Noise && _rnd.Chance(0.1));
                        break;
                }
            }
            w.Line(depth, us ? "endcenterline" : "endcentreline");
        }

        /// <summary>Chooses (and writes) the reading order and units in force for the rows that follow.</summary>
        private State Configure(Writer w, int depth, State state, bool first)
        {
            if (first || (_f.ColumnOrder || _f.BackSights) && _rnd.Chance(0.4))
            {
                var columns = ChooseColumns();
                bool implicitDefault = first && _f.Aliases && columns.SequenceEqual(DefaultColumns) && _rnd.Chance(0.5);
                if (!implicitDefault)
                    w.Line(depth, new[] { "data", "normal" }.Concat(columns).ToArray());
                state = state with { Columns = columns };
            }
            if (_f.Units && (first || _rnd.Chance(0.4)))
                state = state with { Units = ChooseUnits(w, depth, state.Units) };
            if (_f.Calibration && (first || _rnd.Chance(0.4)))
                state = state with { Units = ChooseCalibration(w, depth, state.Units) };
            return state;
        }

        private ImmutableArray<string> ChooseColumns()
        {
            if (!_f.ColumnOrder && !_f.BackSights) return DefaultColumns;
            string length = _f.ColumnOrder && _rnd.Chance(0.4) ? "tape" : "length";
            int compassMode = _f.BackSights ? _rnd.Next(3) : 0;   // 0 fore, 1 back, 2 both
            int clinoMode = _f.BackSights ? _rnd.Next(3) : 0;
            bool synonyms = _f.ColumnOrder && _rnd.Chance(0.4);
            var cols = new List<string> { "from", "to", length };
            if (compassMode != 1) cols.Add(synonyms ? "bearing" : "compass");
            if (compassMode != 0) cols.Add(synonyms ? "backbearing" : "backcompass");
            if (clinoMode != 1) cols.Add(synonyms ? "gradient" : "clino");
            if (clinoMode != 0) cols.Add(synonyms ? "backgradient" : "backclino");
            if (_f.ColumnOrder)
            {
                foreach (var d in new[] { "left", "right", "up", "down" })
                    if (_rnd.Chance(0.25)) cols.Add(d);
                _rnd.Shuffle(cols);
            }
            return cols.ToImmutableArray();
        }

        private ReadingUnits ChooseUnits(Writer w, int depth, ReadingUnits current)
        {
            var units = current;
            if (_rnd.Chance(0.6))
            {
                var (name, metres) = _rnd.Pick(new[]
                {
                    ("metres", 1.0), ("m", 1.0), ("feet", 0.3048), ("ft", 0.3048), ("cm", 0.01),
                    ("centimetres", 0.01), ("yards", 0.9144), ("yd", 0.9144),
                });
                double factor = _rnd.Pick(new[] { 1.0, 1.0, 2.0, 0.5 });
                w.Line(depth, "units", _rnd.Chance(0.5) ? "length" : "tape", factor == 1.0 ? null : Fmt(factor), name);
                units = units with { TapeScale = factor * metres };
            }
            var angles = new[] { ("degrees", AngleUnit.Degree), ("grads", AngleUnit.Grad), ("mils", AngleUnit.Mil) };
            var compass = _rnd.Chance(0.5) ? _rnd.Pick(angles) : default;
            var clino = _rnd.Chance(0.5)
                ? _rnd.Pick(angles.Append(("percent", AngleUnit.PercentSlope)).ToArray())
                : default;
            if (compass.Item1 is not null && clino.Item1 is not null && compass.Item2 == clino.Item2 && _rnd.Chance(0.5))
            {
                w.Line(depth, "units", "compass", "clino", compass.Item1);
            }
            else
            {
                if (compass.Item1 is not null) w.Line(depth, "units", _rnd.Chance(0.3) ? "bearing" : "compass", compass.Item1);
                if (clino.Item1 is not null) w.Line(depth, "units", _rnd.Chance(0.3) ? "gradient" : "clino", clino.Item1);
            }
            if (compass.Item1 is not null) units = units with { Compass = compass.Item2 };
            if (clino.Item1 is not null) units = units with { Clino = clino.Item2 };
            return units;
        }

        private ReadingUnits ChooseCalibration(Writer w, int depth, ReadingUnits units)
        {
            if (_rnd.Chance(0.7))
            {
                double zero = _rnd.Pick(new[] { 0.1, -0.05, 0.25 });
                double? scale = _rnd.Chance(0.5) ? _rnd.Pick(new[] { 1.01, 0.99, 2.0 }) : null;
                w.Line(depth, "calibrate", _rnd.Chance(0.5) ? "length" : "tape", Fmt(zero), scale is { } sc ? Fmt(sc) : null);
                units = units with { TapeZero = zero, TapeCalScale = scale ?? 1 };
            }
            if (_rnd.Chance(0.5))
            {
                double zero = _rnd.Pick(new[] { 0.5, -1.0, 2.0 });
                w.Line(depth, "calibrate", "clino", Fmt(zero));
                units = units with { ClinoZero = zero };
            }
            return units;
        }

        private State SyncFlags(Writer w, int depth, State state, ShotFlags wanted)
        {
            if (state.Flags == wanted) return state;
            var words = new List<string?> { "flags" };
            foreach (var (flag, word) in new[]
                     {
                         (ShotFlags.Duplicate, "duplicate"), (ShotFlags.Surface, "surface"),
                         (ShotFlags.Approximate, "approximate"), (ShotFlags.Splay, "splay"),
                     })
            {
                bool have = (state.Flags & flag) != 0, want = (wanted & flag) != 0;
                if (have == want) continue;
                if (!want) words.Add("not");
                words.Add(word);
            }
            w.Line(depth, words.ToArray());
            return state with { Flags = wanted };
        }

        private string?[] RowWords(GSurvey home, GShot shot, State state)
        {
            var u = state.Units;
            string Plumb(bool reverse) =>
                (shot.Plumb == GPlumb.Up) != reverse
                    ? _rnd.Pick(new[] { "up", "Up", "UP" })      // the spellings Therion accepts
                    : _rnd.Pick(new[] { "down", "Down", "DOWN" });

            return state.Columns.Select(col => col switch
            {
                "from" => RefText(home.Path, shot.From),
                "to" => shot.To is null ? shot.SplayMarker : RefText(home.Path, shot.To),
                "length" or "tape" => Fmt(shot.LengthM / u.TapeScale / u.TapeCalScale + u.TapeZero),
                "compass" or "bearing" => shot.CompassDeg is { } c ? Fmt(ToUnit(c, u.Compass)) : "-",
                "backcompass" or "backbearing" => shot.CompassDeg is { } b ? Fmt(ToUnit((b + 180.0) % 360.0, u.Compass)) : "-",
                "clino" or "gradient" => shot.Plumb != GPlumb.None ? Plumb(false) : Fmt(ToUnit(shot.ClinoDeg, u.Clino) + u.ClinoZero),
                "backclino" or "backgradient" => shot.Plumb != GPlumb.None ? Plumb(true) : Fmt(ToUnit(-shot.ClinoDeg, u.Clino)),   // calibrate clino leaves backclino alone
                _ => Fmt(Math.Round(0.1 + _rnd.NextDouble() * 4, 1)),   // left / right / up / down
            }).ToArray<string?>();
        }
    }

    /// <summary>Joins words into lines with the chosen line breaks, indentation, gaps and continuations.</summary>
    private sealed class Writer
    {
        private readonly SeededRandom _rnd;
        private readonly PrintFeatures _f;
        private readonly StringBuilder _sb = new();
        private readonly string[] _breaks;
        private readonly string _indent;

        public Writer(SeededRandom rnd, PrintFeatures f)
        {
            _rnd = rnd;
            _f = f;
            _breaks = !f.LineBreaks ? new[] { "\n" } : rnd.Next(3) switch
            {
                0 => new[] { "\n" },
                1 => new[] { "\r\n" },
                _ => new[] { "\n", "\r\n", "\r" },
            };
            _indent = f.Layout ? rnd.Pick(new[] { "  ", "    ", "\t", " \t" }) : "";
        }

        public string Text => _sb.ToString();

        private string Break() => _rnd.Pick(_breaks);

        private string Gap() => _f.Layout ? _rnd.Pick(new[] { " ", " ", "  ", "\t", " \t " }) : " ";

        private void Indent(int depth)
        {
            for (int i = 0; i < depth; i++) _sb.Append(_indent);
        }

        public void Comment(int depth, string text)
        {
            Indent(depth);
            _sb.Append("# ").Append(text).Append(Break());
        }

        public void Line(int depth, params string?[] words) => Line(depth, words, trailingComment: false);

        public void Line(int depth, string?[] words, bool trailingComment)
        {
            if (_f.Noise && _rnd.Chance(0.08)) _sb.Append(Break());
            if (_f.Noise && _rnd.Chance(0.05)) Comment(depth, _rnd.Pick(new[] { "note", "a \"quoted\" ș", "1 2 3 4 5" }));
            Indent(depth);
            bool first = true;
            foreach (var word in words)
            {
                if (word is null) continue;
                if (!first)
                {
                    if (_f.Continuations && _rnd.Chance(0.08))
                    {
                        _sb.Append(" \\");
                        if (_f.Layout && _rnd.Chance(0.3)) _sb.Append("  ");   // blanks after the backslash
                        _sb.Append(Break());
                        Indent(depth + 1);
                    }
                    else
                    {
                        _sb.Append(Gap());
                    }
                }
                _sb.Append(word);
                first = false;
            }
            if (trailingComment) _sb.Append(Gap()).Append("# ").Append(_rnd.Pick(new[] { "wet", "x \"y\"", "ș" }));
            _sb.Append(Break());
        }
    }
}
