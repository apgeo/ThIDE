// Writes a derived survey as Therion source: a provenance header that says, every time, that the data
// was derived from a model and not measured; a survey with one centreline (or sub-surveys of a set
// size, equated where they meet); the instrument line naming the derivation; the fix; the legs and
// splays as tape/compass/clino (or cartesian) from the model's coordinates, rounded to instrument
// resolution, with synthetic error only when asked for; LRUD as station dimensions; continuations
// where the model stops. Deterministic: the same survey and settings give the same bytes.

using System.Globalization;
using System.Text;
using Therion.Cave3D;
using Therion.MeshSurvey.Plan;

namespace Therion.MeshSurvey.Output;

public static class TherionCentrelineWriter
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>Gives every station its name (in place) by the naming rule.</summary>
    public static void Name(DerivedSurvey survey, StationNaming naming)
    {
        var order = new List<int>();
        var seen = new HashSet<int>();
        foreach (var b in survey.Branches)
            foreach (int s in b.Stations)
                if (seen.Add(s)) order.Add(s);
        foreach (var s in survey.Stations)
            if (seen.Add(s.Index)) order.Add(s.Index);
        if (naming == StationNaming.Sequential)
        {
            for (int i = 0; i < order.Count; i++) survey.Stations[order[i]].Name = (i + 1).ToString(C);
            return;
        }
        // Branch: the mainline (and every other component's first line) numbered, each lateral a letter
        // prefix; side stations take their parent's name with "s" and a count.
        var named = new HashSet<int>();
        int number = 1, letter = 0;
        foreach (var b in survey.Branches)
        {
            if (b.Level == 0 && b.Parent < 0)
            {
                foreach (int s in b.Stations)
                    if (named.Add(s)) survey.Stations[s].Name = (number++).ToString(C);
                continue;
            }
            string prefix = Letters(letter++);
            int k = 1;
            foreach (int s in b.Stations)
                if (named.Add(s)) survey.Stations[s].Name = prefix + (k++).ToString(C);
        }
        var sideCount = new Dictionary<int, int>();
        foreach (var leg in survey.Legs.Where(l => l.Role == LegRole.Side))
        {
            if (named.Contains(leg.To)) continue;
            int n = sideCount.GetValueOrDefault(leg.From) + 1;
            sideCount[leg.From] = n;
            survey.Stations[leg.To].Name = survey.Stations[leg.From].Name + "s" + n.ToString(C);
            named.Add(leg.To);
        }
        foreach (var s in survey.Stations)
            if (named.Add(s.Index)) s.Name = "x" + (number++).ToString(C);
    }

    private static string Letters(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0) { index--; sb.Insert(0, (char)('A' + index % 26)); index /= 26; }
        return sb.ToString();
    }

    /// <summary>A Therion identifier from any text (letters, digits, '_' and '-').</summary>
    public static string Identifier(string text)
    {
        var sb = new StringBuilder();
        foreach (char ch in text) sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_');
        var id = sb.ToString().Trim('_');
        return id.Length == 0 ? "derived" : id;
    }

    public static string Write(DerivedSurvey survey, MeshSurveyOptions options, string? sourceName = null, string? sourceSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(options);
        if (survey.Stations.Any(s => string.IsNullOrEmpty(s.Name))) Name(survey, options.Output.Naming);
        var r = options.Readings;
        var noise = r.Noise is null ? null : new Random(options.Seed);
        var sb = new StringBuilder();
        string file = sourceName ?? survey.Name;

        sb.Append("encoding utf-8\n");
        sb.Append("# ------------------------------------------------------------------------------------\n");
        sb.Append("# DERIVED, NOT MEASURED. This centreline was placed by software (ThIDE mesh survey ")
          .Append(MeshSurveyInfo.Version).Append(") on the 3-D model\n");
        sb.Append("# \"").Append(file).Append("\"");
        if (sourceSha256 is not null) sb.Append(" (SHA-256 ").Append(sourceSha256).Append(')');
        sb.Append(".\n# No instrument was used and nobody surveyed these stations. Do not report it as a survey.\n");
        if (r.Noise is { } n)
            sb.Append("# The readings contain SYNTHETIC instrument error (seed ").Append(options.Seed.ToString(C)).Append("): tape ")
              .Append(F(n.TapeSigma, 3)).Append(" m + ").Append(F(n.TapeSigmaRelative * 100, 2)).Append(" %, compass ")
              .Append(F(n.CompassSigma, 2)).Append("°, clino ").Append(F(n.ClinoSigma, 2)).Append("°.\n");
        sb.Append("# Settings: preferred leg ").Append(F(options.Legs.Preferred, 2)).Append(" m (").Append(F(options.Legs.Min, 2)).Append('–')
          .Append(F(options.Legs.Max, 2)).Append(" m, ").Append(options.Legs.Strategy.ToString().ToLowerInvariant()).Append("), stations ")
          .Append(options.Anchor.Mode.ToString().ToLowerInvariant()).Append(", splays ").Append(options.Splays.Mode.ToString().ToLowerInvariant())
          .Append(", coverage target ").Append(F(options.Coverage.Target * 100, 0)).Append(" % (reached ")
          .Append(F(survey.Report.Plan.CoverageReached * 100, 1)).Append(" %).\n");
        sb.Append("# Full settings and findings: the .mesh-survey.json report written beside this file.\n");
        sb.Append("# ------------------------------------------------------------------------------------\n\n");

        string surveyId = Identifier(options.Output.SurveyName ?? survey.Name);
        sb.Append("survey ").Append(surveyId).Append(" -title \"").Append(Escape(survey.Name)).Append(" (derived from a 3-D model)\"\n");

        var legOrder = LegOrder(survey);
        var parts = Parts(survey, legOrder, options.Output.StationsPerSurvey);
        if (parts.Count == 1)
        {
            WriteCentreline(sb, survey, options, legOrder, survey.Stations.Select(s => s.Index).ToHashSet(), fixHere: true, noise, "  ", file);
        }
        else
        {
            var home = new Dictionary<int, int>();
            for (int k = 0; k < parts.Count; k++)
                foreach (int li in parts[k])
                {
                    home.TryAdd(survey.Legs[li].From, k);
                    home.TryAdd(survey.Legs[li].To, k);
                }
            for (int k = 0; k < parts.Count; k++)
            {
                sb.Append("\n  survey ").Append(surveyId).Append('_').Append((k + 1).ToString(C)).Append('\n');
                var owned = home.Where(h => h.Value == k).Select(h => h.Key).ToHashSet();
                bool fix = home.GetValueOrDefault(survey.StartStation) == k;
                WriteCentreline(sb, survey, options, parts[k], owned, fix, noise, "    ", file);
                sb.Append("  endsurvey\n");
            }
            // Stations shared by two parts are the same station.
            var equates = new List<string>();
            foreach (var s in survey.Stations)
            {
                var inParts = Enumerable.Range(0, parts.Count).Where(k => parts[k].Any(li => survey.Legs[li].From == s.Index || survey.Legs[li].To == s.Index)).ToList();
                if (inParts.Count > 1)
                    equates.Add(string.Join(" ", inParts.Select(k => s.Name + "@" + surveyId + "_" + (k + 1).ToString(C))));
            }
            if (equates.Count > 0)
            {
                sb.Append("\n  centreline\n");
                foreach (var e in equates) sb.Append("    equate ").Append(e).Append('\n');
                sb.Append("  endcentreline\n");
            }
        }
        sb.Append("endsurvey\n");
        return sb.ToString();
    }

    /// <summary>Legs in survey order: branch by branch, along each branch.</summary>
    private static List<int> LegOrder(DerivedSurvey survey)
    {
        var order = new List<int>();
        var used = new HashSet<int>();
        foreach (var b in survey.Branches.OrderBy(b => b.Index))
            for (int k = 1; k < b.Stations.Count; k++)
            {
                int a = b.Stations[k - 1], c = b.Stations[k];
                for (int li = 0; li < survey.Legs.Count; li++)
                {
                    var l = survey.Legs[li];
                    if (used.Contains(li)) continue;
                    if ((l.From == a && l.To == c) || (l.From == c && l.To == a)) { order.Add(li); used.Add(li); break; }
                }
            }
        for (int li = 0; li < survey.Legs.Count; li++) if (used.Add(li)) order.Add(li);
        return order;
    }

    private static List<List<int>> Parts(DerivedSurvey survey, List<int> legOrder, int? stationsPerSurvey)
    {
        if (stationsPerSurvey is not { } max || survey.Stations.Count <= max) return [legOrder];
        var parts = new List<List<int>>();
        var current = new List<int>();
        var stations = new HashSet<int>();
        foreach (int li in legOrder)
        {
            var l = survey.Legs[li];
            int extra = (stations.Contains(l.From) ? 0 : 1) + (stations.Contains(l.To) ? 0 : 1);
            if (current.Count > 0 && stations.Count + extra > max)
            {
                parts.Add(current);
                current = [];
                stations.Clear();
            }
            current.Add(li);
            stations.Add(l.From);
            stations.Add(l.To);
        }
        if (current.Count > 0) parts.Add(current);
        return parts;
    }

    private static void WriteCentreline(StringBuilder sb, DerivedSurvey survey, MeshSurveyOptions options, List<int> legs, HashSet<int> owned,
        bool fixHere, Random? noise, string indent, string file)
    {
        var r = options.Readings;
        string i2 = indent + "  ";
        sb.Append(indent).Append("centreline\n");
        sb.Append(i2).Append("instrument insts \"derived from the 3-D model ").Append(Escape(file)).Append(" by ThIDE: not measured\"\n");
        if (survey.CoordinateSystem is { } cs)
        {
            sb.Append(i2).Append("cs ").Append(cs).Append('\n');
            sb.Append(i2).Append("grid-angle 0 degrees\n");
        }
        else
        {
            sb.Append(i2).Append("declination 0 degrees\n");
        }
        if (fixHere)
        {
            var start = survey.Stations[survey.StartStation];
            sb.Append(i2).Append("fix ").Append(start.Name).Append(' ').Append(F(start.Position.X, 3)).Append(' ')
              .Append(F(start.Position.Y, 3)).Append(' ').Append(F(start.Position.Z, 3)).Append('\n');
            // Other parts of the model (separate caves) are fixed at their own first station.
            foreach (var b in survey.Branches.Where(b => b.Parent < 0 && b.Level == 0 && b.Stations.Count > 0 && b.Stations[0] != survey.StartStation))
            {
                var s = survey.Stations[b.Stations[0]];
                if (!owned.Contains(s.Index)) continue;
                sb.Append(i2).Append("fix ").Append(s.Name).Append(' ').Append(F(s.Position.X, 3)).Append(' ')
                  .Append(F(s.Position.Y, 3)).Append(' ').Append(F(s.Position.Z, 3)).Append(" # a separate part of the model\n");
            }
        }
        sb.Append('\n');
        bool cartesian = r.Style == ReadingStyle.Cartesian;
        sb.Append(i2).Append(cartesian ? "data cartesian from to easting northing altitude\n" : "data normal from to length compass clino\n");
        foreach (int li in legs)
        {
            var l = survey.Legs[li];
            var a = survey.Stations[l.From];
            var b = survey.Stations[l.To];
            if (l.Forced) sb.Append(i2).Append("# kept by the user although it ").Append(l.Check.Describe()).Append('\n');
            else if (!l.Check.IsUsable) sb.Append(i2).Append("# warning: this leg ").Append(l.Check.Describe()).Append('\n');
            sb.Append(i2).Append(a.Name).Append(' ').Append(b.Name).Append(' ').Append(Readings(b.Position - a.Position, r, noise, cartesian)).Append('\n');
        }
        var splays = survey.Splays.Where(s => owned.Contains(s.Station)).ToList();
        if (splays.Count > 0)
        {
            sb.Append('\n');
            foreach (var s in splays)
                sb.Append(i2).Append(survey.Stations[s.Station].Name).Append(" - ")
                  .Append(Readings(s.End - survey.Stations[s.Station].Position, r, noise, cartesian)).Append('\n');
        }
        if (r.EmitLrud)
        {
            var dims = survey.Stations.Where(s => owned.Contains(s.Index) && s.Lrud is not null).ToList();
            if (dims.Count > 0)
            {
                sb.Append('\n').Append(i2).Append("data dimensions station left right up down\n");
                foreach (var s in dims)
                {
                    var d = s.Lrud!.Value;
                    sb.Append(i2).Append(s.Name).Append(' ').Append(Dim(d.Left, r)).Append(' ').Append(Dim(d.Right, r)).Append(' ')
                      .Append(Dim(d.Up, r)).Append(' ').Append(Dim(d.Down, r)).Append('\n');
                }
            }
        }
        var continuations = survey.Stations.Where(s => owned.Contains(s.Index) && s.Continuation).ToList();
        if (continuations.Count > 0)
        {
            sb.Append('\n');
            foreach (var s in continuations)
                sb.Append(i2).Append("station ").Append(s.Name).Append(" \"the model ends here: the passage may go on\" continuation\n");
        }
        sb.Append(indent).Append("endcentreline\n");
    }

    private static string Readings(CaveVector3 d, ReadingOptions r, Random? noise, bool cartesian)
    {
        if (cartesian)
        {
            if (noise is not null && r.Noise is { } nc)
            {
                double s = nc.TapeSigma;
                d += new CaveVector3(Gauss(noise) * s, Gauss(noise) * s, Gauss(noise) * s);
            }
            return F(d.X, r.LengthDecimals) + " " + F(d.Y, r.LengthDecimals) + " " + F(d.Z, r.LengthDecimals);
        }
        double tape = d.Length;
        double horizontal = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        double compass = horizontal < 1e-9 ? 0 : Math.Atan2(d.X, d.Y) * 180 / Math.PI;
        double clino = tape < 1e-12 ? 0 : Math.Atan2(d.Z, horizontal) * 180 / Math.PI;
        if (noise is not null && r.Noise is { } n)
        {
            tape = tape * n.TapeScale + Gauss(noise) * (n.TapeSigma + n.TapeSigmaRelative * tape);
            compass += n.CompassBias + Gauss(noise) * n.CompassSigma;
            clino += n.ClinoBias + Gauss(noise) * n.ClinoSigma;
            if (n.BlunderRate > 0 && noise.NextDouble() < n.BlunderRate)
            {
                // A gross error: a misread compass (off by a tens digit) or a tape slipped by a metre.
                if (noise.NextDouble() < 0.5) compass += noise.NextDouble() < 0.5 ? 10 : -10;
                else tape += noise.NextDouble() < 0.5 ? 1 : -1;
            }
            tape = Math.Max(0, tape);
            clino = Math.Clamp(clino, -90, 90);
        }
        compass = ((compass % 360) + 360) % 360;
        double rounded = Math.Round(compass, r.AngleDecimals);
        if (rounded >= 360) rounded -= 360;
        return F(tape, r.LengthDecimals) + " " + F(rounded, r.AngleDecimals) + " " + F(clino, r.AngleDecimals);
    }

    private static double Gauss(Random random)
    {
        double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static string Dim(double v, ReadingOptions r) => v < 0 ? "-" : F(v, r.LengthDecimals);

    private static string F(double v, int decimals)
    {
        var s = Math.Round(v, decimals).ToString("F" + decimals.ToString(C), C);
        return s == "-" + 0.0.ToString("F" + decimals.ToString(C), C) ? s[1..] : s;
    }

    private static string Escape(string s) => s.Replace("\"", "'");
}
