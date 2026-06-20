// Implementation Plan §9 — round-trip / code generation.
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

            case DataRow r:
                Indent(sb, indent); sb.Append(string.Join(' ', r.Values)).Append('\n'); break;

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
                    sb.Append(v.X.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                      .Append(v.Y.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
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
        }
    }

    private static void Indent(StringBuilder sb, int n)
    {
        for (int i = 0; i < n; i++) sb.Append("  ");
    }
}
