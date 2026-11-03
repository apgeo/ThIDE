// Deterministic text generators for the span-fidelity properties: raw character soup over the
// characters the tokenizer treats specially, and survey-shaped text built from Therion line
// templates with random line breaks, indentation, continuations, comments and broken blocks.
// Everything is invented; nothing is read from disk.

using System.Text;

namespace Therion.Syntax.Tests.Properties;

/// <summary>A small seeded random source. <see cref="Random(int)"/> is deterministic for a given seed.</summary>
internal sealed class SeededRandom
{
    private readonly Random _random;

    public SeededRandom(int seed) => _random = new Random(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public bool Chance(double p) => _random.NextDouble() < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];
}

/// <summary>How many cases a property runs: the fixed default, or THIDE_PROPERTY_RUNS when set.</summary>
internal static class PropertyRuns
{
    public static int Count(int defaultRuns)
    {
        var raw = Environment.GetEnvironmentVariable("THIDE_PROPERTY_RUNS");
        return int.TryParse(raw, out var n) && n > 0 ? n : defaultRuns;
    }
}

internal static class SourceSoup
{
    // Every character class the tokenizer branches on, plus a BMP letter outside Latin-1 (ș) and a
    // character outside the BMP (a surrogate pair), which must count as two UTF-16 columns.
    private static readonly string[] SoupAlphabet =
    {
        "a", "b", "e", "n", "d", "x", "Z", "0", "1", "7", "9", "\"", "\"\"", "\\", "#", "@", ".", "-", "+",
        "[", "]", ":", "=", " ", " ", "\t", "\r", "\n", "\r\n", "ș", "\U0001D11E", "e1", "1e+",
    };

    /// <summary>Random text of up to <paramref name="maxPieces"/> alphabet pieces.</summary>
    public static string CharacterSoup(int seed, int maxPieces = 120)
    {
        var rnd = new SeededRandom(seed);
        int n = rnd.Next(0, maxPieces + 1);
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(rnd.Pick(SoupAlphabet));
        return sb.ToString();
    }

    private static readonly string[] Stations =
        { "1", "02", "17", "a12", "end", "END", "end1", "x-1", "ș3", "p\U0001D11E", "1@sub", "2@a.b", "endpoint" };

    private static readonly string[] Words =
        { "Big", "\"\"Hall\"\"", "ș", "\U0001D11E", "#", "a\\b", "x-1", "-title", "end", "@", "1.5" };

    private static readonly string[] Numbers =
        { "0", "1.5", "-3", "12.25", "359.9", "-89.5", "+2", ".5", "1e2", "-0.75" };

    /// <summary>
    /// Survey-shaped text: nested survey / centreline / group blocks with data rows, fixes,
    /// equates, station and mark commands, strings with doubled quotes, opaque comment / map
    /// (and optionally surface) blocks, junk lines, and blocks left open or closed wrongly.
    /// </summary>
    /// <param name="loneCr">Allow a bare <c>\r</c> as a line break.</param>
    /// <param name="surfaceBlocks">Allow <c>surface … endsurface</c> blocks.</param>
    public static string SurveyShaped(int seed, bool loneCr, bool surfaceBlocks)
    {
        var rnd = new SeededRandom(seed);
        var w = new Writer(rnd, loneCr);
        var open = new Stack<string>();   // terminators of the open blocks
        int lines = rnd.Next(4, 45);

        if (rnd.Chance(0.2)) w.Line(0, "encoding utf-8");
        for (int i = 0; i < lines; i++)
        {
            int depth = open.Count;
            string? inner = open.Count > 0 ? open.Peek() : null;
            bool inCentreline = inner is "endcentreline" or "endcenterline" or "endgroup";
            int pick = rnd.Next(100);

            if (pick < 8 && depth < 4)
            {
                w.Line(depth, "survey", "s" + i, rnd.Chance(0.5) ? "-title" : null, rnd.Chance(0.5) ? Str(rnd) : null);
                open.Push("endsurvey");
            }
            else if (pick < 14 && depth < 4 && !inCentreline)
            {
                bool us = rnd.Chance(0.5);
                w.Line(depth, us ? "centerline" : "centreline");
                open.Push(us ? "endcenterline" : "endcentreline");
            }
            else if (pick < 17 && depth < 4 && inCentreline)
            {
                w.Line(depth, "group");
                open.Push("endgroup");
            }
            else if (pick < 25 && open.Count > 0)
            {
                var t = open.Pop();
                int roll = rnd.Next(10);
                if (roll == 0) continue;                                // terminator left out
                if (roll == 1) w.Line(depth - 1, "endsurvey");          // possibly the wrong one
                else w.Line(depth - 1, t, t == "endsurvey" && rnd.Chance(0.3) ? "s0" : null);
            }
            else if (pick < 30)
            {
                var order = new List<string> { "from", "to", "length", "compass", "clino" };
                if (rnd.Chance(0.5)) (order[0], order[2]) = (order[2], order[0]);
                if (rnd.Chance(0.3)) order.Add("left");
                w.Line(depth, new[] { "data", "normal" }.Concat(order).ToArray());
            }
            else if (pick < 52)
            {
                w.Line(depth, rnd.Pick(Stations), rnd.Pick(Stations), rnd.Pick(Numbers), rnd.Pick(Numbers),
                    rnd.Chance(0.1) ? "down" : rnd.Pick(Numbers), rnd.Chance(0.2) ? "# " + rnd.Pick(Words) : null);
            }
            else if (pick < 56)
            {
                string Coord() => rnd.Chance(0.5) ? rnd.Pick(Numbers) : $"{rnd.Next(-60, 60)}:{rnd.Next(60):00}:{rnd.Next(60)}.{rnd.Next(10)}";
                w.Line(depth, "fix", rnd.Pick(Stations), Coord(), Coord(), rnd.Pick(Numbers));
            }
            else if (pick < 60)
            {
                w.Line(depth, "equate", rnd.Pick(Stations), rnd.Pick(Stations), rnd.Chance(0.3) ? rnd.Pick(Stations) : null);
            }
            else if (pick < 64)
            {
                w.Line(depth, "station", rnd.Pick(Stations), Str(rnd), rnd.Chance(0.5) ? "entrance" : null);
            }
            else if (pick < 66)
            {
                w.Line(depth, "mark", rnd.Pick(Stations), rnd.Pick(Stations), "fixed");
            }
            else if (pick < 69)
            {
                w.Line(depth, "flags", rnd.Chance(0.5) ? "not" : null, rnd.Pick(new[] { "duplicate", "surface", "splay" }));
            }
            else if (pick < 71)
            {
                w.Line(depth, "units", rnd.Pick(new[] { "length", "compass clino", "tape" }), rnd.Pick(new[] { "feet", "grads", "metres", "percent" }));
            }
            else if (pick < 74)
            {
                w.Line(depth, rnd.Pick(new[] { "team", "title", "explo-team" }), Str(rnd));
            }
            else if (pick < 76)
            {
                w.Line(depth, "date", "2024.05.0" + rnd.Next(1, 10));
            }
            else if (pick < 78)
            {
                w.Line(depth, "input", rnd.Chance(0.5) ? "\"part ș.th\"" : "part.th");
            }
            else if (pick < 80)
            {
                w.Line(depth, "comment");
                w.Line(depth + 1, "free text", Str(rnd), "endpoint");
                if (rnd.Chance(0.8)) w.Line(depth, "endcomment");
            }
            else if (pick < 82)
            {
                w.Line(depth, "map", "m" + i, "-projection", "plan");
                w.Line(depth + 1, "scrap1");
                w.Line(depth + 1, "sub\U0001D11E@x", "0", "0");
                if (rnd.Chance(0.8)) w.Line(depth, "endmap");
            }
            else if (pick < 84 && surfaceBlocks)
            {
                w.Line(depth, "surface", rnd.Chance(0.5) ? "-title" : null, rnd.Chance(0.5) ? Str(rnd) : null);
                w.Line(depth + 1, "grid", "0", "0", "10", "10", "2", "2");
                w.Line(depth + 1, "1", "2.5");
                w.Line(depth + 1, "3", "4");
                w.Line(depth, "endsurface");
            }
            else if (pick < 87)
            {
                w.Line(depth, "station", rnd.Pick(Stations), "\"unclosed " + rnd.Pick(Words));
            }
            else if (pick < 92)
            {
                w.Raw(CharacterSoup(seed * 31 + i, 25).Replace("\r", " ").Replace("\n", " "));
            }
            else if (pick < 96)
            {
                w.Line(depth, "# " + rnd.Pick(Words) + " " + rnd.Pick(Words));
            }
            else
            {
                w.Blank();
            }
        }

        if (rnd.Chance(0.7))
            while (open.Count > 0) w.Line(open.Count - 1, open.Pop());
        return w.ToString(trailingNewLine: rnd.Chance(0.7));
    }

    private static string Str(SeededRandom rnd)
    {
        var sb = new StringBuilder("\"");
        int n = rnd.Next(1, 4);
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(rnd.Pick(Words));
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Joins words into lines with random whitespace, line breaks and backslash continuations.</summary>
    private sealed class Writer
    {
        private readonly SeededRandom _rnd;
        private readonly StringBuilder _sb = new();
        private readonly string[] _breaks;
        private readonly string _indentUnit;

        public Writer(SeededRandom rnd, bool loneCr)
        {
            _rnd = rnd;
            _breaks = rnd.Next(3) switch
            {
                0 => new[] { "\n" },
                1 => new[] { "\r\n" },
                _ => loneCr ? new[] { "\n", "\r\n", "\r" } : new[] { "\n", "\r\n" },
            };
            _indentUnit = rnd.Pick(new[] { "", "  ", "    ", "\t" });
        }

        private string Break() => _rnd.Pick(_breaks);

        private string Gap() => _rnd.Pick(new[] { " ", " ", "  ", "\t", " \t" });

        public void Line(int depth, params string?[] words)
        {
            for (int i = 0; i < depth; i++) _sb.Append(_indentUnit);
            bool first = true;
            foreach (var word in words)
            {
                if (word is null) continue;
                if (!first)
                {
                    // A continuation sits between two words: backslash, optional blanks, line break.
                    if (_rnd.Chance(0.06)) _sb.Append(" \\").Append(_rnd.Chance(0.3) ? "  " : "").Append(Break()).Append(_indentUnit);
                    else _sb.Append(Gap());
                }
                _sb.Append(word);
                first = false;
            }
            if (_rnd.Chance(0.05)) _sb.Append(Gap());   // trailing blanks
            _sb.Append(Break());
        }

        public void Raw(string text) => _sb.Append(text).Append(Break());

        public void Blank() => _sb.Append(Break());

        public string ToString(bool trailingNewLine)
        {
            var s = _sb.ToString();
            if (!trailingNewLine) s = s.TrimEnd('\r', '\n');
            return s;
        }
    }
}
