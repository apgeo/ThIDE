// Implementation Plan �9 � round-trip / code generation.
// M6 stub: emitter visits the AST and writes Therion-formatted text.
// Preserves trivia (TrivialComment, raw option strings) where present.

using System.Globalization;
using System.Text;

namespace Therion.Syntax;

/// <summary>Emits Therion source text from an AST.</summary>
public interface ITherionWriter
{
    /// <summary>Serialize <paramref name="file"/> back to Therion source text.</summary>
    string Write(TherionFile file);
}

/// <summary>
/// Default writer for <c>.th</c> / <c>.thconfig</c> / <c>.th2</c> files.
/// Round-trip fidelity is best-effort: unknown commands keep their raw arguments,
/// known commands re-emit their typed fields with original options appended raw.
/// A node it has no form for (a map, surface, layout, … block) throws
/// <see cref="NotSupportedException"/> rather than being left out of the text.
/// </summary>
public sealed class TherionWriter : ITherionWriter
{
    public string Write(TherionFile file)
    {
        var sb = new StringBuilder();
        foreach (var child in file.Children)
            WriteNode(sb, child, indent: 0);
        return sb.ToString();
    }

    private void WriteNode(StringBuilder sb, TherionNode node, int indent)
    {
        switch (node)
        {
            case TrivialComment c:
                Indent(sb, indent); sb.Append(c.Text).Append('\n'); break;

            case SurveyCommand s:
                Indent(sb, indent); sb.Append("survey ").Append(s.Name);
                if (!string.IsNullOrWhiteSpace(s.OptionsRaw)) sb.Append(' ').Append(s.OptionsRaw);
                sb.Append('\n');
                foreach (var c in s.Children) WriteNode(sb, c, indent + 1);
                if (s.IsTerminated) { Indent(sb, indent); sb.Append("endsurvey\n"); }
                break;

            case CentrelineCommand cl:
                Indent(sb, indent); sb.Append("centreline");
                if (!string.IsNullOrWhiteSpace(cl.OptionsRaw)) sb.Append(' ').Append(cl.OptionsRaw);
                sb.Append('\n');
                foreach (var c in cl.Children) WriteNode(sb, c, indent + 1);
                if (cl.IsTerminated) { Indent(sb, indent); sb.Append("endcentreline\n"); }
                break;

            case DataCommand d:
                Indent(sb, indent); sb.Append("data ").Append(d.Style);
                foreach (var f in d.Fields) sb.Append(' ').Append(f);
                sb.Append('\n');
                break;

            case FlagsCommand fl:
                Indent(sb, indent); sb.Append("flags");
                foreach (var tk in fl.Tokens) sb.Append(' ').Append(tk);
                sb.Append('\n');
                break;

            case DataRow r:
                Indent(sb, indent); sb.Append(string.Join(' ', r.Values));
                if (!string.IsNullOrEmpty(r.TrailingComment)) sb.Append(" # ").Append(r.TrailingComment);
                sb.Append('\n');
                break;

            case StationFix f:
                Indent(sb, indent); sb.Append("fix ").Append(f.Station).Append(' ')
                    .Append(f.X.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(f.Y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(f.Z.ToString("R", CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(f.OptionsRaw)) sb.Append(' ').Append(f.OptionsRaw);
                sb.Append('\n');
                break;

            case EquateCommand e:
                Indent(sb, indent); sb.Append("equate ").Append(string.Join(' ', e.Stations)).Append('\n');
                break;

            case InputCommand i:
                Indent(sb, indent); sb.Append("input ").Append(i.Path).Append('\n'); break;

            case TeamCommand t:
                Indent(sb, indent); sb.Append("team \"").Append(t.Name).Append('"');
                if (!string.IsNullOrWhiteSpace(t.OptionsRaw)) sb.Append(' ').Append(t.OptionsRaw);
                sb.Append('\n');
                break;

            case DateCommand d:
                Indent(sb, indent); sb.Append("date ").Append(d.Value).Append('\n'); break;

            case ScrapBlock sc:
                Indent(sb, indent); sb.Append("scrap ").Append(sc.Id);
                if (!string.IsNullOrWhiteSpace(sc.OptionsRaw)) sb.Append(' ').Append(sc.OptionsRaw);
                sb.Append('\n');
                foreach (var c in sc.Children) WriteNode(sb, c, indent + 1);
                if (sc.IsTerminated) { Indent(sb, indent); sb.Append("endscrap\n"); }
                break;

            case PointObject p:
                Indent(sb, indent); sb.Append("point ")
                    .Append(p.X.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(p.Y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(p.PointType);
                if (!string.IsNullOrWhiteSpace(p.OptionsRaw)) sb.Append(' ').Append(p.OptionsRaw);
                sb.Append('\n');
                break;

            case LineObject l:
                Indent(sb, indent); sb.Append("line ").Append(l.LineType);
                if (!string.IsNullOrWhiteSpace(l.OptionsRaw)) sb.Append(' ').Append(l.OptionsRaw);
                sb.Append('\n');
                foreach (var v in l.Vertices)
                {
                    Indent(sb, indent + 1);
                    if (v.Control1 is { } c1 && v.Control2 is { } c2)
                        sb.Append(Num(c1.X)).Append(' ').Append(Num(c1.Y)).Append(' ')
                          .Append(Num(c2.X)).Append(' ').Append(Num(c2.Y)).Append(' ');
                    sb.Append(Num(v.X)).Append(' ').Append(Num(v.Y));
                    if (!string.IsNullOrWhiteSpace(v.OptionsRaw)) sb.Append(' ').Append(v.OptionsRaw);
                    sb.Append('\n');
                }
                if (l.IsTerminated) { Indent(sb, indent); sb.Append("endline\n"); }
                break;

            case AreaObject a:
                Indent(sb, indent); sb.Append("area ").Append(a.AreaType);
                if (!string.IsNullOrWhiteSpace(a.OptionsRaw)) sb.Append(' ').Append(a.OptionsRaw);
                sb.Append('\n');
                foreach (var id in a.BorderLineIds)
                {
                    Indent(sb, indent + 1); sb.Append(id).Append('\n');
                }
                if (a.IsTerminated) { Indent(sb, indent); sb.Append("endarea\n"); }
                break;

            case UnknownCommand u:
                Indent(sb, indent); sb.Append(u.Keyword);
                if (!string.IsNullOrWhiteSpace(u.RawArguments)) sb.Append(' ').Append(u.RawArguments);
                sb.Append('\n');
                break;

            // Commands whose arguments the parser keeps raw are written back from that text.
            case UnitsCommand un: Line(sb, indent, "units", un.Raw); break;
            case CalibrateCommand ca: Line(sb, indent, "calibrate", ca.Raw); break;
            case DeclinationCommand de: Line(sb, indent, "declination", de.Raw); break;
            case SdCommand sd: Line(sb, indent, "sd", sd.Raw); break;

            case GridAngleCommand g:
                Line(sb, indent, "grid-angle", g.Value is { } gv ? Num(gv) : null, g.Unit); break;
            case JoinCommand j:
                Line(sb, indent, "join", string.Join(' ', j.Targets), j.OptionsRaw); break;
            case MarkCommand m:
                Line(sb, indent, "mark", string.Join(' ', m.Stations), m.MarkType); break;
            case ExtendCommand ex:
                Line(sb, indent, "extend", ex.Spec, string.Join(' ', ex.Stations)); break;
            case StationCommand st:
                Line(sb, indent, "station", st.Station,
                    st.Comment is null && st.Flags.IsEmpty ? null : Quote(st.Comment ?? ""), string.Join(' ', st.Flags));
                break;
            case CsCommand cs: Line(sb, indent, "cs", cs.System); break;
            case BreakCommand: Line(sb, indent, "break"); break;
            case WallsCommand wa: Line(sb, indent, "walls", wa.Value); break;
            case ExploDateCommand ed: Line(sb, indent, "explo-date", ed.Value); break;

            case GroupCommand gr:
                Line(sb, indent, "group");
                foreach (var c in gr.Children) WriteNode(sb, c, indent + 1);
                Line(sb, indent, "endgroup");
                break;

            default:
                throw new NotSupportedException($"TherionWriter cannot write a {node.GetType().Name} node.");
        }
    }

    /// <summary>One line: the keyword, then each non-blank part after a space.</summary>
    private static void Line(StringBuilder sb, int indent, string keyword, params string?[] parts)
    {
        Indent(sb, indent); sb.Append(keyword);
        foreach (var p in parts)
            if (!string.IsNullOrWhiteSpace(p)) sb.Append(' ').Append(p);
        sb.Append('\n');
    }

    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static void Indent(StringBuilder sb, int n)
    {
        for (int i = 0; i < n; i++) sb.Append("  ");
    }
}
