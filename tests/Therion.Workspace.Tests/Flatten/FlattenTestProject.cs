using Therion.Syntax;
using Therion.Workspace.Flatten;

namespace Therion.Workspace.Tests.Flatten;

/// <summary>An invented project held in memory: files by path relative to a virtual root.</summary>
internal sealed class FlattenTestProject
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "thide-flatten-virtual");
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public FlattenTestProject Add(string relative, string text)
    {
        _files[Full(relative)] = text.Replace("\r\n", "\n");
        return this;
    }

    public string Full(string relative) => Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    public FlattenFileSource Files => new(p => _files.TryGetValue(Path.GetFullPath(p), out var t) ? t : null,
        p => _files.ContainsKey(Path.GetFullPath(p)));

    public FlattenResult Flatten(string entry, FlattenOptions? options = null) =>
        SurveyFlattener.Flatten(Full(entry), options ?? new FlattenOptions(), Files);

    /// <summary>The data rows of the merged centreline (lines that are neither commands nor comments).</summary>
    public static List<string[]> Rows(string text)
    {
        var rows = new List<string[]>();
        var file = new ThParser().Parse("flat.th", text, null).Value!;
        foreach (var node in Descend(file.Children))
            if (node is DataRow row) rows.Add(Split(text.Substring(row.Span.StartOffset, row.Span.Length)));
        return rows;
    }

    /// <summary>A row's values as Therion splits them: a bracketed pair (<c>[1 2]</c>) is one value.</summary>
    private static string[] Split(string line)
    {
        var values = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i])) { i++; continue; }
            if (line[i] == '#') break;
            int start = i;
            if (line[i] == '[') { while (i < line.Length && line[i] != ']') i++; i = Math.Min(line.Length, i + 1); }
            else while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            values.Add(line[start..i]);
        }
        return [.. values];
    }

    /// <summary>The trimmed lines of the merged file that start with <paramref name="keyword"/>.</summary>
    public static List<string> Lines(string text, string keyword) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l == keyword || l.StartsWith(keyword + " ", StringComparison.Ordinal)).ToList();

    private static IEnumerable<TherionNode> Descend(IEnumerable<TherionNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            if (n is BlockCommand b) foreach (var c in Descend(b.Children)) yield return c;
        }
    }
}
