// Names for the stations of the merged centreline: one per set of equated stations, unique in the one
// survey they now share, and valid Therion names.

using Therion.Core;

namespace Therion.Workspace.Flatten;

internal static class StationNamer
{
    /// <summary>True when every character of <paramref name="part"/> may stand in a Therion station name.</summary>
    public static bool IsNamePart(string? part, bool allowEmpty)
    {
        if (string.IsNullOrEmpty(part)) return allowEmpty;
        foreach (var c in part)
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '/' or '+' or '*' or '.' or ',' or '\''))
                return false;
        return true;
    }

    /// <summary>The name of each class, keyed by its representative.</summary>
    public static Dictionary<StationId, string> Name(List<StationClass> classes, FlattenOptions o, FlatProject p)
    {
        var names = new Dictionary<StationId, string>();
        if (classes.Count == 0) return names;
        string sep = o.Separator;

        // The survey part of a name leaves out the surveys every station is in (usually the cave itself).
        var paths = classes.Select(c => c.Representative.Namespace.Path).ToList();
        int common = paths[0].Length;
        foreach (var path in paths)
        {
            int k = 0;
            while (k < common && k < path.Length && path[k] == paths[0][k]) k++;
            common = k;
        }
        var relative = paths.Select(path => path.Skip(common).ToArray()).ToList();
        var distinct = relative.Select(r => string.Join('\u0001', r)).Distinct().Select(k => k.Length == 0 ? [] : k.Split('\u0001')).ToList();
        var labels = new Dictionary<string, string>();
        foreach (var r in distinct)
            labels[string.Join('\u0001', r)] = Label(r, distinct, sep);
        string LabelOf(int i) => labels[string.Join('\u0001', relative[i])];
        string Prefixed(int i)
        {
            var label = LabelOf(i);
            var point = classes[i].Representative.Point;
            return label.Length == 0 ? point : label + sep + point;
        }

        var chosen = new string[classes.Count];
        switch (o.Naming)
        {
            case FlattenStationNaming.Sequential:
                for (int i = 0; i < classes.Count; i++) chosen[i] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case FlattenStationNaming.SurveyPrefix:
                for (int i = 0; i < classes.Count; i++) chosen[i] = Prefixed(i);
                break;
            default:
                var byPoint = classes.Select((c, i) => (c.Representative.Point, i)).GroupBy(t => t.Point, StringComparer.Ordinal);
                foreach (var group in byPoint)
                {
                    bool clash = group.Count() > 1;
                    foreach (var (_, i) in group) chosen[i] = clash ? Prefixed(i) : classes[i].Representative.Point;
                }
                break;
        }

        // Unique whatever happened above (a station literally called "sump_1" beside sump's "1").
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < classes.Count; i++)
        {
            var name = o.Prefix + chosen[i];
            if (!used.Add(name))
            {
                int n = 2;
                while (!used.Add(name + sep + n)) n++;
                p.Report(FlattenDiagnosticCodes.NotCarried, DiagnosticSeverity.Info,
                    $"Station name '{name}' was taken; {classes[i].Representative} is called '{name + sep + n}'.", SourceSpan.None);
                name = name + sep + n;
            }
            names[classes[i].Representative] = name;
        }
        return names;
    }

    /// <summary>The fewest innermost survey names that tell <paramref name="path"/> apart from the others.</summary>
    private static string Label(string[] path, List<string[]> all, string sep)
    {
        if (path.Length == 0) return "";
        for (int k = 1; k <= path.Length; k++)
        {
            var tail = path[^k..];
            bool unique = all.All(other => ReferenceEquals(other, path) || other.SequenceEqual(path)
                || other.Length < k || !other[^k..].SequenceEqual(tail));
            if (unique) return string.Join(sep, tail);
        }
        return string.Join(sep, path);
    }
}
