// A seeded .th2 generator: scraps holding points, lines (straight and Bézier vertices) and areas,
// with and without their end lines, joins between objects, comment and blank lines, random
// indentation and LF / CRLF / lone-CR line breaks. It records what it wrote, so a property can
// check the parse against it. Everything is invented; nothing is read from disk.

using System.Globalization;
using System.Text;

namespace Therion.Syntax.Tests.Properties;

internal enum G2Kind { Point, Line, Area, Join }

/// <summary>One object as written: its kind, whether its end line was written, where its keyword starts.</summary>
internal sealed record G2Object(G2Kind Kind, bool Terminated, int HeaderOffset)
{
    /// <summary>Line vertices: the end point of each, and whether it was written as a Bézier triple.</summary>
    public List<(double X, double Y, bool Bezier)> Vertices { get; } = new();
}

internal sealed record G2Scrap(string Id, bool Terminated, int HeaderOffset, List<G2Object> Objects);

internal sealed record G2File(string Text, List<G2Scrap> Scraps);

internal static class Th2Soup
{
    /// <param name="terminators">False: every end line is written (a well-formed file).</param>
    public static G2File Generate(int seed, bool missingTerminators = true)
    {
        var rnd = new SeededRandom(seed);
        var sb = new StringBuilder();
        var breaks = rnd.Next(4) switch
        {
            0 => new[] { "\n" },
            1 => new[] { "\r\n" },
            2 => new[] { "\r" },
            _ => new[] { "\n", "\r\n", "\r" },
        };
        string indentUnit = rnd.Pick(new[] { "", "  ", "\t" });
        var scraps = new List<G2Scrap>();
        int lineIds = 0;

        int Line(int depth, string text)
        {
            for (int i = 0; i < depth; i++) sb.Append(indentUnit);
            int at = sb.Length;
            sb.Append(text).Append(rnd.Pick(breaks));
            return at;
        }
        void Noise(int depth)
        {
            if (rnd.Chance(0.1)) Line(depth, "# note " + rnd.Next(100));
            if (rnd.Chance(0.1)) sb.Append(rnd.Pick(breaks));
        }
        string N() => (rnd.Next(-5000, 5000) / 4.0).ToString(CultureInfo.InvariantCulture);

        if (rnd.Chance(0.2)) Line(0, "encoding utf-8");
        int scrapCount = rnd.Next(1, 5);
        for (int s = 0; s < scrapCount; s++)
        {
            string id = "s" + s;
            int header = Line(0, rnd.Chance(0.5) ? $"scrap {id} -projection plan" : $"scrap {id}");
            var objects = new List<G2Object>();
            int count = rnd.Next(0, 7);
            for (int o = 0; o < count; o++)
            {
                Noise(1);
                bool closed = !missingTerminators || rnd.Chance(0.75);
                switch (rnd.Next(10))
                {
                    case < 3:
                        objects.Add(new G2Object(G2Kind.Point, true,
                            Line(1, $"point {N()} {N()} {rnd.Pick(new[] { "station -name 1", "debris", "label -text \"hall\"", "entrance" })}")));
                        break;
                    case < 7:
                    {
                        var line = new G2Object(G2Kind.Line, closed,
                            Line(1, rnd.Chance(0.5) ? $"line wall -id L{lineIds++}" : "line pit"));
                        int vertices = rnd.Next(1, 6);
                        for (int v = 0; v < vertices; v++)
                        {
                            string x = N(), y = N();
                            bool bezier = v > 0 && rnd.Chance(0.4);
                            Line(2, bezier ? $"{N()} {N()} {N()} {N()} {x} {y}" : $"{x} {y}");
                            line.Vertices.Add((double.Parse(x, CultureInfo.InvariantCulture),
                                double.Parse(y, CultureInfo.InvariantCulture), bezier));
                            if (rnd.Chance(0.15)) Line(2, "smooth off");
                        }
                        if (closed) Line(1, "endline");
                        objects.Add(line);
                        break;
                    }
                    case < 9:
                    {
                        var area = new G2Object(G2Kind.Area, closed, Line(1, rnd.Chance(0.5) ? "area water" : "area sand"));
                        int borders = rnd.Next(1, 4);
                        for (int b = 0; b < borders; b++) Line(2, "B" + rnd.Next(1000));
                        if (closed) Line(1, "endarea");
                        objects.Add(area);
                        break;
                    }
                    default:
                        objects.Add(new G2Object(G2Kind.Join, true, Line(1, $"join L{rnd.Next(9)} L{rnd.Next(9)}")));
                        break;
                }
            }
            Noise(0);
            // Only the last scrap may lose its endscrap: a scrap left open mid-file swallows the next
            // one, which is the parser's documented recovery and not what this generator models.
            bool scrapClosed = !missingTerminators || s < scrapCount - 1 || rnd.Chance(0.7);
            if (scrapClosed) Line(0, rnd.Chance(0.3) ? "endscrap " + id : "endscrap");
            scraps.Add(new G2Scrap(id, scrapClosed, header, objects));
        }
        return new G2File(sb.ToString(), scraps);
    }
}
